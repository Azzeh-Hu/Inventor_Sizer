// GR edits that the drawing makes: a P350 across a face between two supports (a span: horizontal
// between a P150 and a P350, or vertical on an end face between two P150), dividers moved by the
// gap between them, and profile lengths changed from the cut list.
// A span is a copy of a P350 that hangs on two joints in Inventor; in Inventor its joints mate to
// the supports through the named faces: joint Base to the seat of each support (P150 Seat_1 /
// Seat_2, P350 Seat_Side_A / Seat_Side_B), its Out flush with the supports' outside, and its side
// flush with the start of the support (the position).
// Kept apart from the panel code (FP / TP / RP).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace InventorSizer.Core
{
    // A part a new P350 can hang on: its name and where its faces are along the P350 (layout mm).
    public class GrSupport { public string Name, Kind; public double Lo, Hi; public double Mid => (Lo + Hi) / 2; }

    public static partial class Gr
    {
        // A P350 between two supports is this much longer than the clear space between them (it sits
        // in their seats): 10 mm, as the P350 1210 between the P150 of a 1200 box. GR.json "spanAdd".
        public static double SpanAdd = 10;

        static readonly string Axes = "LWH";
        static int AxisIndex(string dim) => dim == "L" ? 0 : dim == "W" ? 1 : 2;
        static string Short(string name) => Regex.Replace(name ?? "", @":\d+$", "");

        static int LongAxis((double[] Min, double[] Max) b)
        {
            var ext = Enumerable.Range(0, 3).Select(i => b.Max[i] - b.Min[i]).ToArray();
            return Array.IndexOf(ext, ext.Max());
        }

        // The parts on face (faceAxis, side) of box f that run across `along` (along the face's other
        // axis) at `at` (layout mm along that axis): the two frame profiles at the face's edges and the
        // P350 lying on the face. Ordered along `along`.
        public static List<GrSupport> Supports(GrModel m, GrFrame f, int faceAxis, int side, int along, double at)
        {
            int run = 3 - faceAxis - along;
            double pw = GrOpenings.ProfileWidth, plane = f.Pos[faceAxis] + (side == 0 ? 0 : f.Size[faceAxis] + CornerAdd);
            var list = new List<GrSupport>();
            foreach (var c in m.Components.Values.Where(c => c.Kind == "frameProfile" && c.Frame == f.Id && c.Follow.Count > 0 && AxisIndex(c.Follow[0].Dim) == run))
            {
                var b = GrOpenings.ProfileBox(m, f, c.Name, run);
                if (b == null) continue;
                double mid = (b.Value.Min[faceAxis] + b.Value.Max[faceAxis]) / 2;
                if (Math.Abs(mid - (side == 0 ? plane + pw / 2 : plane - pw / 2)) > pw / 2 + 5) continue;
                list.Add(new GrSupport { Name = c.Name, Kind = c.Kind, Lo = b.Value.Min[along], Hi = b.Value.Max[along] });
            }
            foreach (var c in m.Components.Values.Where(c => c.Kind == "divider" && c.Frame == f.Id))
            {
                var b = GrOpenings.LayoutBox(m, c);
                if (b == null || LongAxis(b.Value) != run) continue;
                var v = b.Value;
                if (v.Max[faceAxis] - v.Min[faceAxis] > 150 || Math.Abs((v.Min[faceAxis] + v.Max[faceAxis]) / 2 - plane) > 150) continue;
                if (at < v.Min[run] - 1 || at > v.Max[run] + 1) continue;
                list.Add(new GrSupport { Name = c.Name, Kind = c.Kind, Lo = v.Min[along], Hi = v.Max[along] });
            }
            return list.OrderBy(s => s.Mid).ToList();
        }

        // The two supports either side of `near` (layout mm along the span).
        public static (GrSupport Low, GrSupport High) SupportsAround(List<GrSupport> list, double near)
        {
            if (list.Count < 2) return (null, null);
            int i = list.FindLastIndex(s => s.Mid <= near);
            if (i < 0) i = 0;
            if (i >= list.Count - 1) i = list.Count - 2;
            return (list[i], list[i + 1]);
        }

        // A P350 to copy for a span: one in Inventor that hangs on (at least) two joints. Plain P350
        // before milled ones (gefraest: they sit on top of the frame), then the most common one.
        public static string SpanTemplate(GrModel m)
        {
            var joints = m.Components.Values.Where(c => c.Kind == "joint").Select(c => GrOpenings.LayoutBox(m, c)).Where(b => b != null).Select(b => b.Value).ToList();
            var list = new List<GrComponent>();
            foreach (var c in m.Components.Values.Where(c => c.Kind == "divider" && c.CloneOf == "" && !string.IsNullOrEmpty(c.OrigBase) && c.Geo != null))
            {
                var b = GrOpenings.LayoutBox(m, c);
                if (b == null) continue;
                int n = joints.Count(j => Enumerable.Range(0, 3).All(i => j.Min[i] < b.Value.Max[i] + 5 && j.Max[i] > b.Value.Min[i] - 5));
                if (n >= 2) list.Add(c);
            }
            return list.OrderBy(c => c.Base.Contains("gefr") ? 1 : 0).ThenByDescending(c => list.Count(x => x.Base == c.Base)).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                       .Select(c => c.Name).FirstOrDefault();
        }

        // The axis a P350 on a face of its box is thin across (the face normal), and the side of the box
        // it lies on (0 = min, 1 = max).
        public static (int Axis, int Side) FaceOf(GrModel m, GrComponent c)
        {
            var b = GrOpenings.LayoutBox(m, c); var f = m.ById(c.Frame);
            if (b == null || f == null) return (-1, 0);
            int k = LongAxis(b.Value);
            int best = -1, side = 0; double bestD = double.MaxValue;
            foreach (int a in Enumerable.Range(0, 3).Where(a => a != k))
                foreach (int s in new[] { 0, 1 })
                {
                    double plane = f.Pos[a] + (s == 0 ? 0 : f.Size[a] + CornerAdd);
                    double d = Math.Abs((b.Value.Min[a] + b.Value.Max[a]) / 2 - plane);
                    if (d < bestD) { bestD = d; best = a; side = s; }
                }
            return bestD > 150 ? (-1, 0) : (best, side);
        }

        // A new P350 across face (faceAxis, side) of box frameId, running along `along`, at `position`
        // mm from the box's outer start along the face's other axis, between the two supports either
        // side of `near` (layout mm along `along`). In the model at once; in Inventor on Write new GR.
        public static GrRingAdd AddSpan(GrModel m, int frameId, int faceAxis, int side, int along, double position, double near)
        {
            var f = m.ById(frameId) ?? throw new SizerException("That box is not in the GR.");
            if (along == faceAxis || along < 0 || along > 2) throw new SizerException("A P350 across a face runs along that face.");
            if (f.RealOrigin == null) throw new SizerException($"{f.Label} has no real positions: read the GR from Inventor (a new box: write it first).");
            if (GrOpenings.TouchesOtherBox(m, f, faceAxis, side)) throw new SizerException($"That face of {f.Label} is inside the unit (another box is against it).");
            int run = 3 - faceAxis - along;
            double outer = f.Size[run] + CornerAdd, pw = GrOpenings.ProfileWidth;
            if (position <= pw || position >= outer - pw) throw new SizerException($"{U.F(position)} mm is not inside the frame of {f.Label} ({U.F(pw)} to {U.F(outer - pw)} mm along {Axes[run]}).");
            double at = f.Pos[run] + position;
            var (lo, hi) = SupportsAround(Supports(m, f, faceAxis, side, along, at), near);
            if (lo == null) throw new SizerException($"Nothing to hang a P350 on there: the frame profiles of {f.Label} need real positions (read the GR from Inventor).");
            if (hi.Lo - lo.Hi < 100) throw new SizerException($"{Short(lo.Name)} and {Short(hi.Name)} are only {U.F(Math.Round(hi.Lo - lo.Hi))} mm apart.");
            string template = SpanTemplate(m) ?? throw new SizerException("There is no P350 hanging on two joints in this GR to copy.");
            var t = m.Components[template];
            var tb = GrOpenings.LayoutBox(m, t).Value; var tf = m.ById(t.Frame);
            int tk = LongAxis(tb);
            var tface = FaceOf(m, t);
            int tn = tface.Axis >= 0 ? tface.Axis : Enumerable.Range(0, 3).First(i => i != tk);
            double depth = tb.Max[tn] - tb.Min[tn], width = tb.Max[3 - tk - tn] - tb.Min[3 - tk - tn];
            // as far in from the outside of the box as the P350 it copies
            double tplane = tf.Pos[tn] + (tface.Side == 0 ? 0 : tf.Size[tn] + CornerAdd);
            double inset = tface.Side == 0 ? tb.Min[tn] - tplane : tplane - tb.Max[tn];
            if (tface.Axis < 0) inset = 0;
            double plane = f.Pos[faceAxis] + (side == 0 ? 0 : f.Size[faceAxis] + CornerAdd);
            var mn = new double[3]; var mx = new double[3];
            mn[along] = lo.Hi - SpanAdd / 2; mx[along] = hi.Lo + SpanAdd / 2;
            if (side == 0) { mn[faceAxis] = plane + inset; mx[faceAxis] = mn[faceAxis] + depth; } else { mx[faceAxis] = plane - inset; mn[faceAxis] = mx[faceAxis] - depth; }
            mn[run] = at - width / 2; mx[run] = at + width / 2;
            var geo = new Geo
            {
                Min = Enumerable.Range(0, 3).Select(i => mn[i] - f.Pos[i]).ToArray(), Max = Enumerable.Range(0, 3).Select(i => mx[i] - f.Pos[i]).ToArray(),
                Size0 = (double[])f.Size.Clone(), Pos0 = position
            };
            int k = 0; string nn;
            do nn = $"{Short(t.Name)}:new{++k + m.Components.Count(x => x.Value.CloneOf != "")}"; while (m.Components.ContainsKey(nn));
            var n = new GrComponent
            {
                Name = nn, File = t.File, Base = t.Base, OrigBase = "", Kind = "divider", Length = t.Length, Frame = f.Id, CloneOf = template, Geo = geo,
                Position = position, OrigPosition = position, PosConstraint = "(new)", MoveAxis = run, MoveSign = 1
            };
            SetComponentLength(n, 0, Math.Round(hi.Lo - lo.Hi + SpanAdd, 1));
            // between the two frame profiles of the face: it follows the box size along its length
            if (lo.Kind == "frameProfile" && hi.Kind == "frameProfile") n.Follow = new List<Follow> { new Follow { Dim = Axes[along].ToString(), Offset = n.Length - f.Size[along] } };
            m.Components[nn] = n;
            f.Dividers.Add(nn);
            var add = new GrRingAdd { Template = template, Position = position, Frame = f.Id, Span = true, Axis = run, Along = along, FaceAxis = faceAxis, FaceSide = side };
            add.Supports[0] = lo.Name; add.Supports[1] = hi.Name;
            add.Names[template] = nn;
            m.RingAdds.Add(add); m.Edited = true;
            return add;
        }

        static string SpanText(GrModel m, GrRingAdd r)
        {
            var f = m.ById(r.Frame);
            string name = r.Names.Values.FirstOrDefault();
            var c = name != null && m.Components.TryGetValue(name, out var x) ? x : null;
            return $"new {Short(c?.Base ?? r.Template)} across the {GrOpenings.FaceName(m, r.FaceAxis, r.FaceSide)} of {f?.Label}, between {Short(r.Supports[0])} and {Short(r.Supports[1])}, "
                 + $"{U.F(c?.Position ?? r.Position)} mm along {Axes[Math.Max(0, r.Axis)]} (a copy of {r.Template} with its joints)";
        }

        // ------------------------------------------------------------------ moves from the drawing

        // Moves a divider by delta mm along GR axis `axis`: the ring it belongs to (its leader's offset),
        // or a new P350. Returns the divider that was moved.
        public static string MoveDivider(GrModel m, string name, int axis, double delta)
        {
            string lead = RingOf(m, name);
            if (!m.Components.TryGetValue(lead, out var c) || c.PosConstraint == "" || c.Position == null)
                throw new SizerException($"{Short(name)} has no position of its own (it hangs on joints): change the gap to the P350 it hangs on.");
            if (c.MoveAxis < 0) throw new SizerException($"{Short(lead)} has no real position: read the GR from Inventor to move it from the drawing (or use the Dividers window).");
            if (c.MoveAxis != axis) throw new SizerException($"{Short(lead)} moves along {Axes[c.MoveAxis]}, not along {Axes[axis]}.");
            SetDividerPosition(m, lead, Math.Round(c.Position.Value + delta * c.MoveSign, 1));
            return lead;
        }

        // ------------------------------------------------------------------ the cut list

        // The lengths of one kind in the cut list: base name, length, count.
        public static List<(string Base, double Length, int Count)> CutGroups(GrModel m, string kind) =>
            m.Components.Values.Where(c => c.Kind == kind).GroupBy(c => c.Base).Select(g => (g.Key, g.First().Length, g.Count()))
             .OrderByDescending(g => g.Item2).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();

        // What changing a length of the cut list changes: P150 are box sizes (every box size of that
        // length), a P350 that follows a box size changes that size, a P350 that follows nothing gets
        // the new length itself.
        public static string CutEffect(GrModel m, string kind, string baseName)
        {
            var items = m.Components.Values.Where(c => c.Kind == kind && c.Base == baseName).ToList();
            var sizes = items.SelectMany(c => c.Follow.Select(r => $"{m.ById(c.Frame)?.Label} {r.Dim}")).Distinct().OrderBy(x => x).ToList();
            int own = items.Count(c => c.Follow.Count == 0);
            var parts = new List<string>();
            if (sizes.Count > 0) parts.Add((kind == "frameProfile" ? "the P150 are the box sizes: " : "they follow the box sizes: ") + string.Join(", ", sizes) + " change with them");
            if (own > 0) parts.Add($"{own} P350 follow no box size and get the new length themselves");
            return string.Join("; ", parts);
        }

        public static List<string> SetCutLength(GrModel m, string kind, string baseName, double length)
        {
            if (length <= 0) throw new SizerException("A length must be greater than 0.");
            var items = m.Components.Values.Where(c => c.Kind == kind && c.Base == baseName).ToList();
            if (items.Count == 0) throw new SizerException($"{baseName} is no longer in the GR.");
            var done = new List<string>();
            var sizes = items.SelectMany(c => c.Follow.Select(r => (Frame: m.ById(c.Frame), r.Dim, Size: length - r.Offset))).Where(x => x.Frame != null)
                             .GroupBy(x => x.Frame.Id + x.Dim).Select(g => g.First()).ToList();
            foreach (var s in sizes)
            {
                if (s.Size <= 0) throw new SizerException($"{U.F(length)} mm would make {s.Frame.Label} {s.Dim} {U.F(s.Size)} mm.");
                if (s.Frame.Get(s.Dim) == s.Size) continue;
                SetFrameSize(m, s.Frame.No, s.Dim, s.Size, false);
                done.Add($"{s.Frame.Label} {s.Dim} = {U.F(s.Size)}");
            }
            foreach (var c in items.Where(c => c.Follow.Count == 0 && c.Kind == "divider"))
            {
                double grow = length - c.Length;
                SetComponentLength(c, 0, length);
                if (c.Geo != null) { int k = LongAxis((c.Geo.Min, c.Geo.Max)); c.Geo.Max[k] += grow; }
                done.Add($"{Short(c.Name)} = {U.F(length)} mm");
            }
            if (done.Count > 0) m.Edited = true;
            UpdateLayout(m);
            return done;
        }
    }
}
