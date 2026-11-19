// GR openings: the empty spaces on the outside faces of each box that a panel can fill.
// A face is cut by its frame profiles (P150) and by the dividers (P350) lying on it into
// rectangles; each rectangle is filled (a panel sits there), blocked (damper, duct stub) or
// empty. A face that touches another box is inside the unit and has no openings.
// Coordinates: GR layout (0 = length, 1 = width, 2 = height), mm, as GrFrame.Pos.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace InventorSizer.Core
{
    public class GrOpening
    {
        public string Key, Face, State = "empty", Existing = "", ExistingType = "";
        public GrFrame Frame;
        public int Axis, Side, U, V;               // face normal (GR axis), side 0 = min / 1 = max, the two in-face axes
        public double Plane, U0, U1, V0, V1;       // layout coordinates of the face plane and the clear space
        public double ClearU => U1 - U0;
        public double ClearV => V1 - V0;
        public string Type = "";                   // chosen panel type for an empty opening, or a replacement for a filled one
        public Gr.PanelSize Panel;                 // the panel the rules give for Type (or the existing type)
        public string Label => $"{Frame.Label} {Face} {GrOpenings.Pos(this)}";
    }

    public static class GrOpenings
    {
        public static double ProfileWidth = 70;    // P150 seen on a face: the clear space starts this far in from the outer edge
        public static double MinOpening = 100;     // narrower gaps (e.g. beside a damper) are not openings
        static readonly string[] AxisNames = { "L", "W", "H" };

        // Face names: ends along the length, sides across the width, top and bottom.
        public static string FaceName(GrModel m, int axis, int side)
        {
            if (axis == 2) return side == 0 ? "Bottom" : "Top";
            if (axis == 0) return side == 0 ? "End 1" : "End 2";
            bool service = (side == 1) == (m.ServiceSide > 0);
            return service ? "Service side" : "Back side";
        }

        // Default panel per face: TP on the service side, RP on the back, FP everywhere else.
        public static string DefaultType(string face) => face == "Service side" ? "TP" : face == "Back side" ? "RP" : "FP";

        public static string Pos(GrOpening o) => $"{AxisNames[o.U]} {U.F(Math.Round(o.U0 - o.Frame.Pos[o.U]))}-{U.F(Math.Round(o.U1 - o.Frame.Pos[o.U]))}, {AxisNames[o.V]} {U.F(Math.Round(o.V0 - o.Frame.Pos[o.V]))}-{U.F(Math.Round(o.V1 - o.Frame.Pos[o.V]))}";

        // A component's box in layout coordinates (dividers follow size edits and moves, as drawn).
        public static (double[] Min, double[] Max)? LayoutBox(GrModel m, GrComponent c)
        {
            if (c.Geo == null) return null;
            var f = m.ById(c.Frame);
            if (f == null) return null;
            var mn = (double[])c.Geo.Min.Clone(); var mx = (double[])c.Geo.Max.Clone();
            if (c.Kind != "divider") ShiftWithSize(c.Geo, f.Size, -1, mn, mx);
            if (c.Kind == "divider")
            {
                var ext = Enumerable.Range(0, 3).Select(i => mx[i] - mn[i]).ToArray();
                int k = Array.IndexOf(ext, ext.Max());
                ShiftWithSize(c.Geo, f.Size, k, mn, mx);
                foreach (var r in c.Follow) if (DimIndex(r.Dim) == k) mx[k] += f.Size[k] - c.Geo.Size0[k];
                if (c.Position != null && c.Geo.Pos0 != null && c.Position != c.Geo.Pos0)
                {
                    m.Components.TryGetValue(c.PosPartner ?? "", out var partner);
                    int pk = partner != null && partner.Follow.Count > 0 ? DimIndex(partner.Follow[0].Dim) : -1;
                    int s = Enumerable.Range(0, 3).First(i => i != k && i != pk);
                    double delta = c.Position.Value - c.Geo.Pos0.Value;
                    mn[s] += delta; mx[s] += delta;
                }
            }
            return (Enumerable.Range(0, 3).Select(i => f.Pos[i] + mn[i]).ToArray(), Enumerable.Range(0, 3).Select(i => f.Pos[i] + mx[i]).ToArray());
        }

        static int DimIndex(string dim) => dim == "L" ? 0 : dim == "W" ? 1 : 2;

        // A part on a far face of its box (within 150 mm of it across axis i) moves with that face
        // when the box size changes; parts inside keep their place. skip = the part's own length
        // axis, which follows the size instead.
        public static void ShiftWithSize(Geo g, double[] size, int skip, double[] mn, double[] mx)
        {
            if (g.Size0 == null) return;
            for (int i = 0; i < 3; i++)
            {
                if (i == skip) continue;
                double delta = size[i] - g.Size0[i];
                if (delta == 0 || g.Size0[i] + Gr.CornerAdd - g.Max[i] > 150) continue;
                mn[i] += delta; mx[i] += delta;
            }
        }

        static string TypeOf(string baseName)
        {
            var m = Regex.Match(baseName ?? "", @"(?:^|\s)(FP|RP|TP)-\d");
            return m.Success ? m.Groups[1].Value : "";
        }

        // All openings of the model. Needs real positions (from Inventor or an InventorReader export).
        public static List<GrOpening> Find(GrModel m)
        {
            var list = new List<GrOpening>();
            if (m.RealMap == null) return list;
            var boxes = new Dictionary<string, (double[] Min, double[] Max)>();
            foreach (var c in m.Components.Values) { var b = LayoutBox(m, c); if (b != null) boxes[c.Name] = b.Value; }
            foreach (var f in m.Frames)
            {
                var o = f.Pos; var outer = f.Size.Select(x => x + Gr.CornerAdd).ToArray();
                for (int a = 0; a < 3; a++)
                    for (int side = 0; side < 2; side++)
                    {
                        if (TouchesOtherBox(m, f, a, side)) continue;
                        int u = a == 0 ? 1 : 0, v = a == 2 ? 1 : 2;
                        double plane = o[a] + (side == 0 ? 0 : outer[a]);
                        var rects = new List<double[]> { new[] { o[u] + ProfileWidth, o[u] + outer[u] - ProfileWidth, o[v] + ProfileWidth, o[v] + outer[v] - ProfileWidth } };
                        // dividers lying on this face: thin across the face, at the plane
                        var divs = m.Components.Values.Where(c => c.Kind == "divider" && c.Frame == f.Id && boxes.ContainsKey(c.Name))
                            .Select(c => boxes[c.Name]).Where(b => OnPlane(b, a, plane, 120))
                            .OrderByDescending(b => Math.Max(b.Max[u] - b.Min[u], b.Max[v] - b.Min[v])).ToList();
                        foreach (var d in divs)
                        {
                            bool alongV = d.Max[v] - d.Min[v] >= d.Max[u] - d.Min[u];
                            int la = alongV ? v : u, ca = alongV ? u : v;
                            int li = la == u ? 0 : 2, ci = ca == u ? 0 : 2;   // index of the axis' min in a rect
                            var next = new List<double[]>();
                            foreach (var r in rects)
                            {
                                bool crosses = d.Min[ca] > r[ci] - 1 && d.Max[ca] < r[ci + 1] + 1 && d.Min[la] <= r[li] + 60 && d.Max[la] >= r[li + 1] - 60;
                                if (!crosses) { next.Add(r); continue; }
                                var lo = (double[])r.Clone(); lo[ci + 1] = d.Min[ca];
                                var hi = (double[])r.Clone(); hi[ci] = d.Max[ca];
                                if (lo[ci + 1] - lo[ci] > 20) next.Add(lo);
                                if (hi[ci + 1] - hi[ci] > 20) next.Add(hi);
                            }
                            rects = next;
                        }
                        string face = FaceName(m, a, side);
                        foreach (var r in rects.Where(r => r[1] - r[0] >= MinOpening && r[3] - r[2] >= MinOpening).OrderBy(r => r[2]).ThenBy(r => r[0]))
                        {
                            var op = new GrOpening { Frame = f, Axis = a, Side = side, U = u, V = v, Plane = plane, U0 = r[0], U1 = r[1], V0 = r[2], V1 = r[3], Face = face };
                            op.Key = $"{f.Id}|{a}{side}|{Math.Round(r[0] - o[u])}|{Math.Round(r[2] - o[v])}";
                            Classify(m, op, boxes);
                            list.Add(op);
                        }
                    }
            }
            foreach (var op in list) { m.PanelChoice.TryGetValue(op.Key, out var t); op.Type = t ?? ""; Size(op); }
            return list;
        }

        static bool OnPlane((double[] Min, double[] Max) b, int a, double plane, double depth) =>
            b.Max[a] - b.Min[a] < depth && b.Max[a] > plane - depth && b.Min[a] < plane + depth;

        static bool TouchesOtherBox(GrModel m, GrFrame f, int a, int side)
        {
            double plane = f.Pos[a] + (side == 0 ? 0 : f.Size[a] + Gr.CornerAdd);
            foreach (var g in m.Frames.Where(g => g != f))
            {
                double gp = side == 0 ? g.Pos[a] + g.Size[a] + Gr.CornerAdd : g.Pos[a];
                if (Math.Abs(gp - plane) > 1) continue;
                bool overlap = Enumerable.Range(0, 3).Where(i => i != a).All(i => g.Pos[i] < f.Pos[i] + f.Size[i] + Gr.CornerAdd - 1 && g.Pos[i] + g.Size[i] + Gr.CornerAdd > f.Pos[i] + 1);
                if (overlap) return true;
            }
            return false;
        }

        static double Overlap(GrOpening op, (double[] Min, double[] Max) b)
        {
            double du = Math.Min(op.U1, b.Max[op.U]) - Math.Max(op.U0, b.Min[op.U]), dv = Math.Min(op.V1, b.Max[op.V]) - Math.Max(op.V0, b.Min[op.V]);
            return du <= 0 || dv <= 0 ? 0 : du * dv / (op.ClearU * op.ClearV);
        }

        static void Classify(GrModel m, GrOpening op, Dictionary<string, (double[] Min, double[] Max)> boxes)
        {
            foreach (var c in m.Components.Values.Where(c => (c.Kind == "panel" || c.Kind == "damper" || c.Kind == "stub") && boxes.ContainsKey(c.Name)))
            {
                var b = boxes[c.Name];
                if (!(b.Max[op.Axis] > op.Plane - 150 && b.Min[op.Axis] < op.Plane + 150)) continue;
                double ov = Overlap(op, b);
                if (c.Kind == "panel" && ov >= 0.5 && b.Max[op.Axis] - b.Min[op.Axis] < 150)
                {
                    op.State = "filled"; op.Existing = c.Name; op.ExistingType = TypeOf(c.Base); return;
                }
                if (c.Kind != "panel" && ov >= 0.1) { op.State = "blocked"; op.Existing = c.Name; }
            }
        }

        // The panel the rules give: the chosen type, or for a filled opening its existing type.
        public static void Size(GrOpening op)
        {
            string t = op.Type != "" && op.Type != "keep" ? op.Type : op.State == "filled" ? op.ExistingType : "";
            op.Panel = t == "" || op.State == "blocked" ? null : Gr.PanelFor(t, op.ClearU, op.ClearV);
        }

        public static void Choose(GrModel m, GrOpening op, string type)
        {
            type = type ?? "";
            if (type == "") m.PanelChoice.Remove(op.Key); else m.PanelChoice[op.Key] = type;
            op.Type = type; Size(op); m.Edited = true;
        }

        // The default type on every empty opening that has no choice yet.
        public static int FillDefaults(GrModel m, List<GrOpening> list)
        {
            int n = 0;
            foreach (var op in list.Where(o => o.State == "empty" && o.Type == "")) { Choose(m, op, DefaultType(op.Face)); n++; }
            return n;
        }

        // Does the existing panel have the size the rules give? ("" when it does or there is none)
        public static string ExistingCheck(GrModel m, GrOpening op)
        {
            if (op.State != "filled" || op.Panel == null || !m.Components.TryGetValue(op.Existing, out var c)) return "";
            var s = U.SizeRegex.Match(c.Base);
            if (!s.Success) return "";
            double w = U.P(s.Groups[1].Value), l = U.P(s.Groups[2].Value);
            return Math.Abs(w - op.Panel.W) < 0.6 && Math.Abs(l - op.Panel.L) < 0.6 ? "" : $"rule gives {U.F(op.Panel.W)}x{U.F(op.Panel.L)}";
        }

        // The panels to make: new ones for chosen empty openings, replacements where a filled one is
        // set to a type ("keep" or no choice leaves it).
        public static List<GrOpening> Planned(List<GrOpening> list) =>
            list.Where(o => o.Panel != null && o.Type != "" && o.Type != "keep" && (o.State == "empty" || o.State == "filled")).ToList();

        public static void ExportPanelList(GrModel m, List<GrOpening> list, string path)
        {
            var culture = System.Globalization.CultureInfo.CurrentCulture; string sep = culture.TextInfo.ListSeparator;
            string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(sep, new[] { "Box", "Face", "Opening (clear, mm)", "Position", "State", "Panel", "Master", "Note" }.Select(Q)));
            foreach (var o in Planned(list))
                sb.AppendLine(string.Join(sep, new[] { o.Frame.Label, o.Face, $"{U.F(o.ClearU)} x {U.F(o.ClearV)}", Pos(o), o.State == "filled" ? "replaces " + o.Existing : "new", o.Panel.Name, o.Panel.Inner, o.Panel.Problem ?? "" }.Select(Q)));
            System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
    }
}
