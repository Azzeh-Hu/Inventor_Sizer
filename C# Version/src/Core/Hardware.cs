// Door hardware holes (hinges, handles, locks): a template per type (standards\<type>.json,
// "hardware"), each hole measured from its nearest panel edge or from the middle, so it keeps
// that distance at any size. Hinge / handle sets can be repeated evenly (HingeCount, ...).

using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorSizer.Core
{
    public class Anchor
    {
        public string From = "start";   // start, end or mid of the span
        public double Value;
        public double Resolve(double span) => From == "end" ? span - Value : From == "mid" ? span / 2 + Value : Value;
        public JsonObject ToJson() => new JsonObject { ["From"] = From, ["Value"] = Value };
        public static Anchor Of(JsonObject j) => j == null ? new Anchor() : new Anchor { From = j.Str("From", "start"), Value = j.Num("Value") };

        // The distance from the start, the end or the middle, whichever describes the position
        // best; the middle only when it is under half the edge distance.
        public static Anchor Best(double pos, double span)
        {
            double start = pos, end = span - pos, mid = pos - span / 2;
            double edge = Math.Min(Math.Abs(start), Math.Abs(end));
            if (Math.Abs(mid) < 0.5 * edge) return new Anchor { From = "mid", Value = Math.Round(mid, 3) };
            return Math.Abs(start) <= Math.Abs(end) ? new Anchor { From = "start", Value = Math.Round(start, 3) } : new Anchor { From = "end", Value = Math.Round(end, 3) };
        }
    }

    public class TemplateHole
    {
        public string Wall = "face", Side = "bottom", Group = "";
        public double Dia;
        public Anchor U = new Anchor(), V = new Anchor();
        public TemplateHole Copy() => new TemplateHole { Wall = Wall, Side = Side, Group = Group, Dia = Dia, U = new Anchor { From = U.From, Value = U.Value }, V = new Anchor { From = V.From, Value = V.Value } };
    }

    public class PlacedHole
    {
        public double X, Y, Z, Dia;     // X = width, Y = length, Z = height (mm)
        public string Axis, Wall, Side, Group;
        public bool Bad, Overlap;
    }

    public class HoleTemplate
    {
        public List<TemplateHole> Holes = new List<TemplateHole>();
        public string Source = "", Learned = "", Notes = "";
        public int RuleHolesSkipped;

        public static HoleTemplate FromJson(JsonObject j)
        {
            var t = new HoleTemplate { Source = j.Str("source"), Learned = j.Str("learned"), Notes = j.Str("notes") };
            foreach (var h in j.Arr("holes").OfType<JsonObject>())
                t.Holes.Add(new TemplateHole { Wall = h.Str("Wall", "face"), Side = h.Str("Side", "bottom"), Group = h.Str("Group"), Dia = h.Num("Dia"), U = Anchor.Of(h.Obj("U")), V = Anchor.Of(h.Obj("V")) });
            return t;
        }

        public JsonObject ToJson()
        {
            var holes = Holes.Select(h =>
            {
                var o = new JsonObject { ["Wall"] = h.Wall, ["Side"] = h.Side, ["Dia"] = h.Dia, ["U"] = h.U.ToJson(), ["V"] = h.V.ToJson() };
                if (h.Group != "") o["Group"] = h.Group;
                return (object)o;
            }).ToList();
            return new JsonObject { ["source"] = Source, ["learned"] = Learned, ["notes"] = Notes, ["holes"] = holes };
        }
    }

    public class Circle { public double[] Center, Normal; public double Dia; }

    public class GroupPitch { public int Count; public double First, Span, Pitch; public List<TemplateHole> Holes; }

    public static class Hardware
    {
        // Which axis of a box (mm) is the width, the length and the height.
        public static int[] PanelAxes(double[] size, double W, double L, double H)
        {
            var want = new[] { W, L, H };
            var axes = new[] { -1, -1, -1 };
            var taken = new bool[3];
            foreach (int k in new[] { 2, 0, 1 })
            {
                int best = -1; double diff = double.MaxValue;
                for (int a = 0; a < 3; a++) if (!taken[a] && Math.Abs(size[a] - want[k]) < diff) { best = a; diff = Math.Abs(size[a] - want[k]); }
                if (diff > 1.5)
                    throw new SizerException($"The panel in Inventor is {string.Join(" x ", size.Select(U.F))} mm, which does not match the name ({U.F(W)} x {U.F(L)} x {U.F(H)}). Open the master the name describes.");
                axes[k] = best; taken[best] = true;
            }
            return axes;
        }

        // Circles (assembly space, mm) -> template; rule wall holes (HoleDia at HoleZ) are left out.
        public static HoleTemplate FromCircles(IEnumerable<Circle> circles, double[] boxMin, double[] boxSize, double W, double L, double H, double ruleDia, double ruleZ)
        {
            var axes = PanelAxes(boxSize, W, L, H);
            var seen = new HashSet<string>();
            var t = new HoleTemplate();
            // Inventor's range box is a little larger than the part (0.002 mm per side on the TP
            // master): take half of the excess off, so 161.002 is read as 161.
            var want = new[] { W, L, H };
            var pad = new double[3];
            for (int i = 0; i < 3; i++) { double extra = (boxSize[axes[i]] - want[i]) / 2; pad[i] = extra > 0 && extra < 0.5 ? extra : 0; }
            foreach (var c in circles)
            {
                var p = new[] { c.Center[axes[0]] - boxMin[axes[0]] - pad[0], c.Center[axes[1]] - boxMin[axes[1]] - pad[1], c.Center[axes[2]] - boxMin[axes[2]] - pad[2] };
                var n = new[] { Math.Abs(c.Normal[axes[0]]), Math.Abs(c.Normal[axes[1]]), Math.Abs(c.Normal[axes[2]]) };
                int k = Array.IndexOf(n, n.Max());
                if (n[k] < 0.99) continue;   // tilted: not a drilled hole
                string wall, side; double u, v, su, sv;
                if (k == 2) { wall = "face"; side = p[2] < H / 2 ? "bottom" : "top"; u = p[0]; v = p[1]; su = W; sv = L; }
                else if (k == 0) { wall = "length"; side = p[0] < W / 2 ? "x0" : "xW"; u = p[1]; v = p[2]; su = L; sv = H; }
                else { wall = "width"; side = p[1] < L / 2 ? "y0" : "yL"; u = p[0]; v = p[2]; su = W; sv = H; }
                var key = $"{wall}|{side}|{u:0.0}|{v:0.0}|{c.Dia:0.00}";
                if (!seen.Add(key)) continue;
                // HoleZ is measured from the face on some masters and from the rim on others (TP).
                if (wall != "face" && Math.Abs(c.Dia - ruleDia) < 0.05 && (Math.Abs(v - ruleZ) < 1.6 || Math.Abs(H - v - ruleZ) < 1.6)) { t.RuleHolesSkipped++; continue; }
                t.Holes.Add(new TemplateHole
                {
                    Wall = wall, Side = side, Dia = Math.Round(c.Dia, 3), U = Anchor.Best(u, su),
                    V = wall == "face" ? Anchor.Best(v, sv) : new Anchor { From = "start", Value = Math.Round(v, 3) }
                });
            }
            return t;
        }

        // Learning reads holes only, not what they are for. A learned face hole takes the group
        // (hinge, handle) of the previous template's holes with the same diameter, when only one
        // group uses that diameter. Without groups the hinge / handle counts would stop working.
        public static int KeepGroups(HoleTemplate old, HoleTemplate learned)
        {
            if (old == null || learned == null) return 0;
            var byDia = old.Holes.Where(h => !string.IsNullOrEmpty(h.Group) && h.Wall == "face")
                .GroupBy(h => Math.Round(h.Dia, 1)).Where(g => g.Select(h => h.Group).Distinct().Count() == 1)
                .ToDictionary(g => g.Key, g => g.First().Group);
            int n = 0;
            foreach (var h in learned.Holes)
                if (h.Wall == "face" && string.IsNullOrEmpty(h.Group) && byDia.TryGetValue(Math.Round(h.Dia, 1), out string group)) { h.Group = group; n++; }
            return n;
        }

        // Hinge / handle sets per door. The holes anchored at the start are the first set; with a
        // count n the sets are spread evenly so the last one sits where the mirrored end set was:
        //   pitch = (L - 2 * first - span) / (n - 1)    (hinge: (L - 2*98 - 24) / (n - 1))
        // A count of 1 puts the set in the middle.
        public static Dictionary<string, GroupPitch> Pitches(HoleTemplate t, double L, Dictionary<string, double> counts)
        {
            var o = new Dictionary<string, GroupPitch>();
            if (t == null || counts == null) return o;
            foreach (var kv in counts)
            {
                int n = (int)kv.Value;
                var first = t.Holes.Where(h => h.Group == kv.Key && h.Wall == "face" && h.V.From == "start").ToList();
                if (first.Count == 0 || n < 1) continue;
                double lo = first.Min(h => h.V.Value), hi = first.Max(h => h.V.Value);
                o[kv.Key] = new GroupPitch { Count = n, First = lo, Span = hi - lo, Pitch = n > 1 ? (L - 2 * lo - (hi - lo)) / (n - 1) : 0, Holes = first };
            }
            return o;
        }

        public static HoleTemplate Expand(HoleTemplate t, double L, Dictionary<string, double> counts)
        {
            var pitches = Pitches(t, L, counts);
            if (pitches.Count == 0) return t;
            var r = new HoleTemplate { Source = t.Source };
            r.Holes.AddRange(t.Holes.Where(h => !pitches.ContainsKey(h.Group)));
            foreach (var p in pitches.Values)
                for (int k = 0; k < p.Count; k++)
                {
                    double shift = p.Count == 1 ? (L - p.Span) / 2 - p.First : k * p.Pitch;
                    foreach (var h in p.Holes) { var c = h.Copy(); c.V = new Anchor { From = "start", Value = h.V.Value + shift }; r.Holes.Add(c); }
                }
            return r;
        }

        // Hole positions for a size; Bad = outside the panel or running into another hole.
        public static List<PlacedHole> Place(HoleTemplate t, double W, double L, double H, Dictionary<string, double> counts = null)
        {
            var list = new List<PlacedHole>();
            if (t == null) return list;
            if (counts != null && counts.Count > 0) t = Expand(t, L, counts);
            foreach (var h in t.Holes)
            {
                double r = h.Dia / 2, x, y, z; string axis; bool bad;
                if (h.Wall == "face") { x = h.U.Resolve(W); y = h.V.Resolve(L); z = h.Side == "top" ? H : 0; axis = "z"; bad = x - r < 0 || x + r > W || y - r < 0 || y + r > L; }
                else if (h.Wall == "length") { y = h.U.Resolve(L); z = h.V.Resolve(H); x = h.Side == "xW" ? W : 0; axis = "x"; bad = y - r < 0 || y + r > L; }
                else { x = h.U.Resolve(W); z = h.V.Resolve(H); y = h.Side == "yL" ? L : 0; axis = "y"; bad = x - r < 0 || x + r > W; }
                list.Add(new PlacedHole { X = x, Y = y, Z = z, Axis = axis, Dia = h.Dia, Wall = h.Wall, Side = h.Side, Group = h.Group, Bad = bad });
            }
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    var a = list[i]; var b = list[j];
                    if (a.Wall != b.Wall || a.Side != b.Side) continue;
                    double d = Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
                    if (d < (a.Dia + b.Dia) / 2) { a.Bad = b.Bad = a.Overlap = b.Overlap = true; }
                }
            return list;
        }

        public static List<string> Notes(List<PlacedHole> holes)
        {
            var n = new List<string>();
            int over = holes.Count(h => h.Overlap), outside = holes.Count(h => h.Bad && !h.Overlap);
            if (outside > 0) n.Add($"Door hardware: {outside} hole(s) fall outside the panel at this size (red in the drawing).");
            if (over > 0) n.Add($"Door hardware: {over} hole(s) run into each other at this size, e.g. the 2 handles on a short door (red in the drawing).");
            return n;
        }

        public static Dictionary<string, double> Counts(Standard s, Dictionary<string, double> values)
        {
            var o = new Dictionary<string, double>();
            if (s == null) return o;
            foreach (var kv in s.HardwareCounts) if (values.TryGetValue(kv.Value.Param, out var v)) o[kv.Key] = v;
            return o;
        }

        // HingePitch / HandlePitch for the master's patterns. With a count of 1 there is no pitch:
        // the master keeps its own (a pattern spacing of 0 is not allowed in Inventor).
        public static List<Param> PitchParams(Standard s, double L, Dictionary<string, double> counts)
        {
            var o = new List<Param>();
            if (s == null || s.Hardware == null) return o;
            var p = Pitches(s.Hardware, L, counts);
            foreach (var kv in s.HardwareCounts) if (p.TryGetValue(kv.Key, out var g) && g.Count > 1) o.Add(new Param(kv.Value.PitchParam, g.Pitch, "mm"));
            return o;
        }
    }

    // RP handles: HandleCount handles in a row along the length (HandleDir = 0) or the width (1),
    // centred across the other direction. The row is cut into HandleCount equal sections with a
    // handle in the middle of each:
    //   HandlePitch  = span / HandleCount
    //   HandleOffset = HandlePitch * (HandleCount - 1) / 2   (first handle from the centre; 0 for one handle)
    // Each handle has 2 holes holeSpan apart along the row. The outer hole's centre must stay
    // minEdge from the panel end; that also keeps the handles apart.
    public class HandleRow
    {
        public string CountParam = "HandleCount", DirParam = "HandleDir", PitchParam = "HandlePitch", OffsetParam = "HandleOffset", Group = "handle";
        public double HoleSpan = 117, HoleDia = 9, MinEdge = 75;

        public static HandleRow FromJson(JsonObject j)
        {
            var r = new HandleRow();
            r.CountParam = j.Str("countParam", r.CountParam); r.DirParam = j.Str("dirParam", r.DirParam);
            r.PitchParam = j.Str("pitchParam", r.PitchParam); r.OffsetParam = j.Str("offsetParam", r.OffsetParam);
            r.Group = j.Str("group", r.Group);
            r.HoleSpan = j.Num("holeSpan", r.HoleSpan); r.HoleDia = j.Num("holeDia", r.HoleDia); r.MinEdge = j.Num("minEdge", r.MinEdge);
            return r;
        }

        public class Result
        {
            public int Count, Dir;
            public double Span, Pitch, Offset, Edge;
            public List<PlacedHole> Holes = new List<PlacedHole>();
            public List<string> Errors = new List<string>();
            public List<Param> Params = new List<Param>();
        }

        // The most handles that keep minEdge on a span.
        public int MaxCount(double span) => Math.Max(0, (int)Math.Floor(span / (2 * MinEdge + HoleSpan) + 1e-9));

        public Result Layout(double W, double L, Dictionary<string, double> values)
        {
            var r = new Result();
            double n = values.TryGetValue(CountParam, out var c) ? c : 1, d = values.TryGetValue(DirParam, out var dv) ? dv : 0;
            if (n < 1 || n != Math.Round(n)) { r.Errors.Add($"{CountParam}: the number of handles must be a whole number of 1 or more (got {U.F(n)})."); return r; }
            if (d != 0 && d != 1) { r.Errors.Add($"{DirParam}: 0 = handles along the length, 1 = along the width (got {U.F(d)})."); return r; }
            r.Count = (int)n; r.Dir = (int)d;
            string along = r.Dir == 0 ? "length" : "width";
            r.Span = r.Dir == 0 ? L : W;
            double cross = r.Dir == 0 ? W : L;
            r.Pitch = r.Span / r.Count;
            r.Offset = r.Pitch * (r.Count - 1) / 2;
            r.Edge = r.Pitch / 2 - HoleSpan / 2;
            if (r.Edge < MinEdge - 1e-9)
            {
                int max = MaxCount(r.Span);
                r.Errors.Add(max >= 1
                    ? $"Handles: {r.Count} along the {along} ({U.F(r.Span)} mm) leave {U.F(r.Edge)} mm from the panel end to the outer hole (minimum {U.F(MinEdge)}). At most {max} fit."
                    : $"Handles: the {along} ({U.F(r.Span)} mm) is too short for one handle ({U.F(HoleSpan)} mm between the holes + {U.F(MinEdge)} mm at each end).");
            }
            for (int i = 0; i < r.Count; i++)
            {
                double mid = (i + 0.5) * r.Pitch;
                foreach (double a in new[] { mid - HoleSpan / 2, mid + HoleSpan / 2 })
                {
                    double x = r.Dir == 0 ? cross / 2 : a, y = r.Dir == 0 ? a : cross / 2;
                    r.Holes.Add(new PlacedHole { X = x, Y = y, Z = 0, Dia = HoleDia, Axis = "z", Wall = "face", Side = "bottom", Group = Group, Bad = r.Errors.Count > 0 });
                }
            }
            r.Params.Add(new Param(DirParam, r.Dir, "ul"));
            r.Params.Add(new Param(PitchParam, r.Pitch, "mm"));
            r.Params.Add(new Param(OffsetParam, r.Offset, "mm"));
            return r;
        }
    }
}
