// GR drawings: boxes as profile wireframes with corners and labels, dividers (P350 purple,
// P350 gefraest green, moved orange) and joints (gold) at their real positions, spacing chains
// between subsequent dividers, the size of every box, a legend, a box table and a cut list.
// Drawing axes as for panels: x = width, y = length (along the unit), z = height.
//
// The model is drawn through a camera (GrCamera) that the user turns, pans and zooms like in
// Inventor. The views of the View box are camera positions; "Front" looks at the service side
// (the TP side), so the unit faces the user. The model area is fitted between the header
// (caption, legend) and the tables, so nothing runs into the bill of materials, and it is
// clipped to that area when zoomed in.

using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorSizer.Core
{
    public class DividerLine { public double[] A, B; public int Axis; public string Name, Kind; public GrFrame Frame; public bool Real; }

    // Where the GR is seen from. Yaw turns around the height (0 = looking from the W max side),
    // pitch looks down (+) or up (-). Scale 0 = fit to the area on the next drawing.
    public class GrCamera
    {
        public double Yaw = 40, Pitch = 30, Scale, PanX, PanY;
        public string View = "Isometric";
        public double[] Area;   // the model area of the last drawing (px: x, y, w, h)

        public static double FrontYaw(int serviceSide) => serviceSide >= 0 ? 0 : 180;

        // A view of the View box; "Front" = the service side, Left / Right as seen from the front.
        public void Set(string view, int serviceSide)
        {
            double f = FrontYaw(serviceSide);
            View = view ?? "Isometric";
            switch (View)
            {
                case "Front": Yaw = f; Pitch = 0; break;
                case "Back": Yaw = f + 180; Pitch = 0; break;
                case "Right": Yaw = f + 90; Pitch = 0; break;
                case "Left": Yaw = f - 90; Pitch = 0; break;
                case "Top": Yaw = f; Pitch = 90; break;
                case "Bottom": Yaw = f; Pitch = -90; break;
                default: Yaw = f + 40; Pitch = 30; break;
            }
            Fit();
        }

        public void Fit() { Scale = 0; PanX = PanY = 0; }

        public void Orbit(double dxPx, double dyPx)
        {
            Yaw = (Yaw - dxPx * 0.4) % 360;
            Pitch = Math.Max(-90, Math.Min(90, Pitch + dyPx * 0.4));
            View = "";
        }

        // Zoom by factor around a screen point (px), keeping the model point under it in place.
        public void Zoom(double factor, double px, double py)
        {
            if (Scale <= 0 || Area == null) return;
            double ns = Math.Max(1e-5, Math.Min(Scale * factor, 50));
            double cx = Area[0] + Area[2] / 2 + PanX, cy = Area[1] + Area[3] / 2 + PanY;
            double k = ns / Scale;
            PanX += (px - cx) * (1 - k); PanY += (py - cy) * (1 - k);
            Scale = ns;
        }

        // Right, up and towards-the-viewer unit vectors in drawing axes (x = W, y = L, z = H).
        public double[] Basis()
        {
            double y = Yaw * Math.PI / 180, p = Pitch * Math.PI / 180;
            var d = new[] { Math.Cos(p) * Math.Cos(y), Math.Cos(p) * Math.Sin(y), Math.Sin(p) };
            double[] r = Math.Abs(Math.Cos(p)) > 1e-6 ? Norm(new[] { -d[1], d[0], 0 }) : new[] { -Math.Sin(y), Math.Cos(y), 0 };
            var u = new[] { d[1] * r[2] - d[2] * r[1], d[2] * r[0] - d[0] * r[2], d[0] * r[1] - d[1] * r[0] };
            return new[] { r[0], r[1], r[2], u[0], u[1], u[2], d[0], d[1], d[2] };
        }

        static double[] Norm(double[] v) { double n = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]); return n < 1e-12 ? new[] { 1.0, 0, 0 } : v.Select(x => x / n).ToArray(); }
    }

    public static class GrDrawing
    {
        // GR-only kinds: the boxes not being edited (grey), and the dimensions that can be clicked to
        // change them (blue). Added to the shared style table, which the panel drawings do not use.
        static GrDrawing()
        {
            void S(string k, Style s) { if (!Styles.All.ContainsKey(k)) Styles.All[k] = s; }
            S("profileDim", new Style { Stroke = "#d4d4d4", Width = 1.4 });
            S("profileHiddenDim", new Style { Stroke = "#e6e6e6", Width = 1, Dash = new float[] { 5, 4 } });
            S("dividerDim", new Style { Stroke = "#dadada", Width = 1.4 });
            S("cornerDim", new Style { Stroke = "#cfcfcf", Width = 1, Fill = "#e8e8e8" });
            S("jointDim", new Style { Stroke = "#cfcfcf", Width = 1, Fill = "#f2f2f2" });
            S("openDim", new Style { Fill = "#9a9a9a", Opacity = 0.06 });
            S("labelDim", new Style { TextColor = "#b4b4b4", FontSize = 12, Bold = true });
            S("labelFocus", new Style { TextColor = "#0574a8", FontSize = 14, Bold = true });
            S("dimEdit", new Style { Stroke = "#0696D7", Width = 1.1 });
            S("arrowEdit", new Style { Fill = "#0696D7" });
            S("labelEdit", new Style { TextColor = "#0574a8", FontSize = 13, Bold = true });
            S("subEdit", new Style { TextColor = "#0574a8", FontSize = 11 });
            S("cutEdit", new Style { TextColor = "#0574a8", FontSize = 12 });
            // new parts (green) and the P350 picked with a click (orange): their own dimensions
            S("dimNew", new Style { Stroke = "#2b8a3e", Width = 1.2 }); S("arrowNew", new Style { Fill = "#2b8a3e" });
            S("labelNew", new Style { TextColor = "#237032", FontSize = 13, Bold = true }); S("subNew", new Style { TextColor = "#237032", FontSize = 11 });
            S("dimSel", new Style { Stroke = "#e8590c", Width = 1.3 }); S("arrowSel", new Style { Fill = "#e8590c" });
            S("labelSel", new Style { TextColor = "#c4460a", FontSize = 13, Bold = true }); S("subSel", new Style { TextColor = "#c4460a", FontSize = 11 });
            S("dividerNew", new Style { Stroke = "#8ce99a", Width = 6 });
            S("dividerSel", new Style { Stroke = "#ffc078", Width = 7 });
        }

        // A dimension that can be clicked to change it: its line, arrows and label carry the tag, in
        // the palette's colour (Edit blue, New green, Sel orange). Without a tag: plain grey, or the
        // palette's colour for a new or picked part (so its chain reads as one).
        static void EditDim(Draw d, double[] a, double[] b, string label, string tag, string palette = "Edit")
        {
            int first = d.Shapes.Count;
            d.DimLine(a, b, label);
            if (tag == null && palette == "Edit") return;
            for (int i = first; i < d.Shapes.Count; i++)
            {
                var s = d.Shapes[i];
                s.Tag = tag;
                if (s.Kind == "dim" || s.Kind == "arrow" || s.Kind == "label" || s.Kind == "sub") s.Kind += palette;
            }
        }

        static bool Dimmed(GrModel m, GrFrame f) => m.Focus != 0 && f != null && f.Id != m.Focus;

        public class BoxXYZ { public GrFrame F; public double X0, X1, Y0, Y1, Z0, Z1; public double Min(int k) => k == 0 ? X0 : k == 1 ? Y0 : Z0; public double Max(int k) => k == 0 ? X1 : k == 1 ? Y1 : Z1; }

        public static List<BoxXYZ> Boxes(GrModel m)
        {
            double a = Gr.CornerAdd;
            return m.Frames.Select(f => new BoxXYZ { F = f, X0 = f.Y, X1 = f.Y + f.Wy + a, Y0 = f.X, Y1 = f.X + f.Lx + a, Z0 = f.Z, Z1 = f.Z + f.Hz + a }).ToList();
        }

        public static double[][] Corners(BoxXYZ b) => new[]
        {
            new[] { b.X0, b.Y0, b.Z0 }, new[] { b.X1, b.Y0, b.Z0 }, new[] { b.X1, b.Y1, b.Z0 }, new[] { b.X0, b.Y1, b.Z0 },
            new[] { b.X0, b.Y0, b.Z1 }, new[] { b.X1, b.Y0, b.Z1 }, new[] { b.X1, b.Y1, b.Z1 }, new[] { b.X0, b.Y1, b.Z1 },
        };

        public static readonly int[][] Edges = { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 0 }, new[] { 4, 5 }, new[] { 5, 6 }, new[] { 6, 7 }, new[] { 7, 4 }, new[] { 0, 4 }, new[] { 1, 5 }, new[] { 2, 6 }, new[] { 3, 7 } };

        // An edge of a box: its two ends (drawing axes), the drawing axis it runs along, and the two
        // faces it lies on (GR axis, side 0 = min / 1 = max).
        public static (double[] A, double[] B, int Axis, (int Axis, int Side)[] Faces) Edge(BoxXYZ b, int i)
        {
            var c = Corners(b); var a = c[Edges[i][0]]; var e = c[Edges[i][1]];
            int along = Enumerable.Range(0, 3).First(k => Math.Abs(a[k] - e[k]) > 1e-9);
            var faces = Enumerable.Range(0, 3).Where(k => k != along).Select(k => (GrAxis(k), Math.Abs(a[k] - b.Max(k)) < 1e-6 ? 1 : 0)).ToArray();
            return (a, e, along, faces);
        }

        public static int GrAxis(int drawAxis) => drawAxis == 0 ? 1 : drawAxis == 1 ? 0 : 2;   // x = W, y = L, z = H
        public static int DrawAxis(int grAxis) => GrAxis(grAxis);
        static double[] ToDraw(double[] p) => new[] { p[1], p[0], p[2] };   // GR (L, W, H) -> drawing (x = W, y = L, z = H)

        static double Dot(double[] r, int row, double[] p) => r[row * 3] * p[0] + r[row * 3 + 1] * p[1] + r[row * 3 + 2] * p[2];

        // Is a face (GR axis, side) turned to the viewer?
        static bool Facing(double[] R, int grAxis, int side) => R[6 + DrawAxis(grAxis)] * (side == 0 ? -1 : 1) > 1e-6;

        static void Frames(Draw d, ViewT t, List<BoxXYZ> boxes, GrModel m)
        {
            // hidden edges first and dashed, so the eye reads the box the right way round; the boxes
            // not being edited grey, under the one that is
            foreach (bool dim in new[] { true, false })
                foreach (bool front in new[] { false, true })
                    foreach (var b in boxes.Where(b => Dimmed(m, b.F) == dim))
                        for (int i = 0; i < Edges.Length; i++)
                        {
                            var e = Edge(b, i);
                            bool seen = e.Faces.Any(f => Facing(t.R, f.Axis, f.Side));
                            if (seen != front) continue;
                            d.Poly3((seen ? "profile" : "profileHidden") + (dim ? "Dim" : ""), t, new[] { e.A, e.B }, false);
                            d.Shapes[d.Shapes.Count - 1].Tag = $"edge:{b.F.No}:{i}";
                        }
            foreach (var b in boxes)
                foreach (var p in Corners(b)) { var s = Draw.Screen(t, p); d.Rect(Dimmed(m, b.F) ? "cornerDim" : "corner", s[0] - 2.5, s[1] - 2.5, s[0] + 2.5, s[1] + 2.5); }
        }

        static void Labels(Draw d, ViewT t, List<BoxXYZ> boxes, GrModel m)
        {
            var taken = new List<double[]>();
            foreach (var b in boxes)
            {
                var f = b.F;
                // above the box on the screen, clear of the opening numbers on its faces
                var sc = Corners(b).Select(c => Draw.Screen(t, c)).ToList();
                var p = new[] { (sc.Min(s => s[0]) + sc.Max(s => s[0])) / 2, sc.Min(s => s[1]) - 26 };
                for (int guard = 0; guard < 8 && taken.Any(q => Math.Abs(q[0] - p[0]) < 52 && Math.Abs(q[1] - p[1]) < 16); guard++) p[0] += 56;   // not on another box's name
                taken.Add(p);
                d.Text(Dimmed(m, f) ? "labelDim" : m.Focus == f.Id ? "labelFocus" : "label", p[0], p[1], f.Label); d.Shapes[d.Shapes.Count - 1].Tag = "box:" + f.No;
                if (Dimmed(m, f)) continue;
                var extra = new List<string>();
                if (f.Panels.Count > 0) extra.Add($"{f.Panels.Count} pan.");
                if (f.Dividers.Count > 0) extra.Add($"{f.Dividers.Count} div.");
                if (extra.Count > 0 && t.S * Math.Max(b.Y1 - b.Y0, b.X1 - b.X0) > 90) d.Text("sub", p[0], p[1] + 14, string.Join(" / ", extra));
            }
        }

        static int DimIndex(string dim) => dim == "L" ? 0 : dim == "W" ? 1 : 2;

        static string DividerKind(GrModel m, GrComponent c) =>
            (c.Position != null && c.Position != c.OrigPosition) || Gr.DividerShift(m, c).Delta != 0 ? "dividerMoved" : c.Base.Contains("gefr") ? "dividerMilled" : "divider";

        // Dividers from their real position (following edits), or schematically from their offset.
        public static List<DividerLine> DividerLines(GrModel m)
        {
            var list = new List<DividerLine>();
            double half = Gr.CornerAdd / 2;
            foreach (var c in m.Components.Values.Where(c => c.Kind == "divider"))
            {
                var f = m.ById(c.Frame);
                if (f == null) continue;
                var o = f.Pos; var size = f.Size;
                double[] p0, p1; int k;
                if (c.Geo != null)
                {
                    var mn = (double[])c.Geo.Min.Clone(); var mx = (double[])c.Geo.Max.Clone();
                    var ext = Enumerable.Range(0, 3).Select(i => mx[i] - mn[i]).ToArray();
                    k = Array.IndexOf(ext, ext.Max());
                    GrOpenings.ShiftWithSize(c.Geo, size, k, mn, mx);
                    foreach (var r in c.Follow) if (DimIndex(r.Dim) == k) mx[k] += size[k] - c.Geo.Size0[k];
                    var sh = Gr.DividerShift(m, c);
                    if (sh.Axis >= 0 && sh.Delta != 0) { mn[sh.Axis] += sh.Delta; mx[sh.Axis] += sh.Delta; }
                    p0 = Enumerable.Range(0, 3).Select(i => o[i] + (mn[i] + mx[i]) / 2).ToArray(); p1 = (double[])p0.Clone();
                    p0[k] = o[k] + mn[k]; p1[k] = o[k] + mx[k];
                }
                else if (c.Position != null)
                {
                    m.Components.TryGetValue(c.PosPartner ?? "", out var partner);
                    string pdim = partner != null && partner.Follow.Count > 0 ? partner.Follow[0].Dim : "";
                    var sz = new Dictionary<string, double> { ["L"] = f.Lx, ["W"] = f.Wy, ["H"] = f.Hz };
                    string dim; double? len = null;
                    if (c.Follow.Count > 0) dim = c.Follow[0].Dim;
                    else
                    {
                        dim = new[] { "H", "W", "L" }.FirstOrDefault(x => x != pdim && sz[x] >= c.Length);
                        if (dim == null) continue;
                        len = c.Length;
                    }
                    k = DimIndex(dim);
                    int s = DimIndex(new[] { "L", "H", "W" }.First(x => x != dim && x != pdim));
                    p0 = Enumerable.Range(0, 3).Select(i => o[i] + (size[i] + Gr.CornerAdd) / 2).ToArray(); p1 = (double[])p0.Clone();
                    p0[s] = o[s] + half + c.Position.Value; p1[s] = p0[s];
                    p0[k] = o[k] + half; p1[k] = len != null ? p0[k] + len.Value : o[k] + size[k] + half;
                }
                else continue;
                list.Add(new DividerLine { A = p0, B = p1, Axis = k, Name = c.Name, Kind = DividerKind(m, c), Frame = f, Real = c.Geo != null });
            }
            return list;
        }

        public static List<double[]> JointPoints(GrModel m) => JointPointsOf(m).Select(j => j.P).ToList();

        static List<(double[] P, GrFrame F)> JointPointsOf(GrModel m)
        {
            var list = new List<(double[], GrFrame)>();
            foreach (var c in m.Components.Values.Where(c => c.Kind == "joint" && c.Geo != null))
            {
                var f = m.ById(c.Frame);
                if (f == null) continue;
                var mn = (double[])c.Geo.Min.Clone(); var mx = (double[])c.Geo.Max.Clone();
                GrOpenings.ShiftWithSize(c.Geo, f.Size, -1, mn, mx);
                list.Add((Enumerable.Range(0, 3).Select(i => f.Pos[i] + (mn[i] + mx[i]) / 2).ToArray(), f));
            }
            return list;
        }

        // The tag of a gap between dividers that can be changed: moving `name` by +x along GR axis
        // `axis` changes the gap by sign * x; `max` = the room it has (the gap and the one it takes
        // from, so the gaps never add up to more than the space between the corners).
        public static string GapTag(int axis, int sign, double value, double max, string name) => $"dim:gap:{axis}:{sign}:{U.F(value)}:{U.F(max)}:{name}";

        // The spacing axis and the face a divider lies on (S = -1: inside the box, on no face).
        static (int S, int Face, int Side) Where(DividerLine l)
        {
            var f = l.Frame; var o = f.Pos; var size = f.Size;
            var others = Enumerable.Range(0, 3).Where(i => i != l.Axis).ToArray();
            int AtFace(int i) => l.A[i] - o[i] < 120 ? 0 : o[i] + size[i] + Gr.CornerAdd - l.A[i] < 120 ? 1 : -1;
            int a0 = AtFace(others[0]), a1 = AtFace(others[1]);
            if (a0 >= 0 && a1 < 0) return (others[1], others[0], a0);
            if (a1 >= 0 && a0 < 0) return (others[0], others[1], a1);
            return (-1, -1, 0);
        }

        static bool IsNew(GrModel m, string name) => m.Components.TryGetValue(name ?? "", out var c) && c.CloneOf != "";

        // Spacing between the dividers on one face of a box, one chain per face and direction (never the
        // dividers of two faces in one chain), from the inner face of the frame profile (70 mm) to each
        // divider and on to the far profile, just past the dividers' ends:
        //   blue   the parts in Inventor, on the faces turned to the viewer (the standard view)
        //   green  each new part (ring, single P350, P350 across a face): its own chain to the parts
        //          either side, further out, on any face
        //   orange the P350 picked with a click: its gaps on its face, whichever face that is
        // A gap can be clicked: it moves the divider at its far end (the last gap: the one at its near
        // end); in a green or orange chain the part the chain is for.
        static void Chains(Draw d, ViewT t, List<DividerLine> lines, GrModel m)
        {
            const double prof = 70;
            var placed = lines.Where(l => !Dimmed(m, l.Frame)).Select(l => (L: l, W: Where(l))).Where(x => x.W.S >= 0).ToList();
            foreach (var g in placed.GroupBy(x => (x.L.Frame.Id, x.W.Face, x.W.Side, x.W.S)))
            {
                var f = g.First().L.Frame; int s = g.Key.S, fa = g.Key.Face, side = g.Key.Side, k = 3 - s - fa;
                var o = f.Pos; var size = f.Size;
                var all = g.Select(x => x.L).ToList();
                double minK = all.Min(l => Math.Min(l.A[k], l.B[k])), atF = all[0].A[fa];
                List<(double V, string Name)> Values(IEnumerable<DividerLine> ls)
                {
                    var vals = new List<(double V, string Name)> { (o[s] + prof, null) };
                    foreach (var l in ls.OrderBy(l => l.A[s]))
                    {
                        double v = Math.Round(l.A[s], 1);
                        if (vals.All(x => Math.Abs(x.V - v) > 1)) vals.Add((v, l.Name));
                    }
                    vals = vals.OrderBy(x => x.V).ToList();
                    vals.Add((o[s] + size[s] + Gr.CornerAdd - prof, null));
                    return vals;
                }
                // one part with the parts either side of it (on the whole face: in Inventor and new)
                var full = Values(all);
                List<(double V, string Name)> Around(DividerLine l)
                {
                    double v = Math.Round(l.A[s], 1);
                    int i = full.FindIndex(x => Math.Abs(x.V - v) <= 1);
                    if (i <= 0 || i >= full.Count - 1) return null;
                    return new List<(double, string)> { full[i - 1], (v, l.Name), full[i + 1] };
                }
                void Chain(List<(double V, string Name)> vals, int level, string palette, string mover)
                {
                    if (vals == null) return;
                    double atK = minK - 110 * level + 30;   // level 1 as before (80 mm out), the others further out
                    for (int i = 1; i < vals.Count; i++)
                    {
                        var p = new double[3]; var q = new double[3];
                        p[k] = q[k] = atK; p[fa] = q[fa] = atF; p[s] = vals[i - 1].V; q[s] = vals[i].V;
                        var sa = Draw.Screen(t, ToDraw(p)); var sb = Draw.Screen(t, ToDraw(q));
                        if (Math.Sqrt(Math.Pow(sb[0] - sa[0], 2) + Math.Pow(sb[1] - sa[1], 2)) < 18) continue;   // seen end-on
                        double gap = Math.Round(vals[i].V - vals[i - 1].V, 1);
                        string tag = null;
                        if (mover != null) tag = GapTag(s, vals[i].Name == mover ? 1 : -1, gap, Math.Round(vals[vals.Count - 1].V - vals[0].V, 1), mover);
                        else if (vals[i].Name != null) tag = GapTag(s, 1, gap, Math.Round((i + 1 < vals.Count ? vals[i + 1].V : vals[i].V) - vals[i - 1].V, 1), vals[i].Name);
                        else if (vals[i - 1].Name != null) tag = GapTag(s, -1, gap, Math.Round(vals[i].V - (i >= 2 ? vals[i - 2].V : vals[i - 1].V), 1), vals[i - 1].Name);
                        EditDim(d, sa, sb, U.F(gap), tag, palette);
                    }
                }
                var existing = all.Where(l => !IsNew(m, l.Name)).ToList();
                if (existing.Count > 0 && Facing(t.R, fa, side)) Chain(Values(existing), 1, "Edit", null);
                int level = 2;
                foreach (var l in all.Where(l => IsNew(m, l.Name)).OrderBy(l => l.A[s])) Chain(Around(l), level++, "New", l.Name);
                var sel = all.FirstOrDefault(l => l.Name == m.SelectedDivider);
                if (sel != null) Chain(Around(sel), level, "Sel", sel.Name);
            }
        }

        static void Dividers(Draw d, ViewT t, GrModel m)
        {
            var lines = DividerLines(m);
            foreach (var l in lines.Where(l => Dimmed(m, l.Frame))) d.Poly3("dividerDim", t, new[] { ToDraw(l.A), ToDraw(l.B) }, false);
            foreach (var l in lines.Where(l => !Dimmed(m, l.Frame) && (l.Name == m.SelectedDivider || IsNew(m, l.Name))))
                d.Poly3(l.Name == m.SelectedDivider ? "dividerSel" : "dividerNew", t, new[] { ToDraw(l.A), ToDraw(l.B) }, false);   // under the line: a halo
            foreach (var l in lines.Where(l => !Dimmed(m, l.Frame))) { d.Poly3(l.Kind, t, new[] { ToDraw(l.A), ToDraw(l.B) }, false); d.Shapes[d.Shapes.Count - 1].Tag = "div:" + l.Name; }
            foreach (var j in JointPointsOf(m)) { var s = Draw.Screen(t, ToDraw(j.P)); d.Rect(Dimmed(m, j.F) ? "jointDim" : "joint", s[0] - 2.5, s[1] - 2.5, s[0] + 2.5, s[1] + 2.5); }
            if (lines.Count > 0) Chains(d, t, lines, m);
        }

        public static string OpeningKind(GrOpening o)
        {
            if (o.State == "blocked") return "openBlocked";
            if (o.HasType) return "open" + o.Type;
            return o.State == "filled" ? "openFilled" : "openEmpty";
        }

        // Openings as see-through areas on the box faces (far ones first), the selected one outlined.
        // The faces turned to the viewer carry their number and a tag, so a click finds them.
        static void Openings(Draw d, ViewT t, GrModel m)
        {
            var list = GrOpenings.Find(m);
            var centre = new Dictionary<string, double[]>();
            foreach (var o in list) { var c = new double[3]; c[o.Axis] = o.Plane; c[o.U] = (o.U0 + o.U1) / 2; c[o.V] = (o.V0 + o.V1) / 2; centre[o.Key] = ToDraw(c); }
            int first = d.Shapes.Count;   // a sheet draws several views into one page: only this view's shapes
            foreach (var o in list.OrderBy(o => Dot(t.R, 2, centre[o.Key])))
            {
                var pts = new[] { (o.U0, o.V0), (o.U1, o.V0), (o.U1, o.V1), (o.U0, o.V1) }.Select(p =>
                {
                    var q = new double[3]; q[o.Axis] = o.Plane; q[o.U] = p.Item1; q[o.V] = p.Item2; return ToDraw(q);
                }).ToList();
                if (Dimmed(m, o.Frame)) { d.Poly3("openDim", t, pts, true); continue; }
                d.Poly3(OpeningKind(o), t, pts, true);
                var shape = d.Shapes[d.Shapes.Count - 1];
                if (Facing(t.R, o.Axis, o.Side)) shape.Tag = "open:" + o.Key;
                if (o.Key == m.SelectedOpening) d.Poly3("openSelected", t, pts, true);
            }
            foreach (var o in list)
            {
                var poly = d.Shapes.Skip(first).FirstOrDefault(s => s.Tag == "open:" + o.Key);
                if (poly == null || Area(poly.Points) < 900) continue;   // turned away, or too small to label
                var p = Draw.Screen(t, centre[o.Key]);
                d.Text("openLabel", p[0], p[1], o.Id);
            }
        }

        static double Area(double[] p)
        {
            double a = 0; int n = p.Length / 2;
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; a += p[2 * i] * p[2 * j + 1] - p[2 * j] * p[2 * i + 1]; }
            return Math.Abs(a / 2);
        }

        // ------------------------------------------------------------------ dimensions

        // The size of every box along each axis, as chains along the outside of the unit (one
        // segment per box; boxes side by side follow each other, boxes of the same span share
        // one), and the overall size one step further out when it differs. An axis seen end-on
        // has no dimension. Lengths along the floor go below the unit, the height to its left.
        // A box size can be clicked (the segment of one box; while a box is edited, only its own sizes).
        static void Dimensions(Draw d, ViewT t, List<BoxXYZ> boxes, GrModel m)
        {
            if (m.Focus != 0) boxes = boxes.Where(b => b.F.Id == m.Focus).ToList();
            if (boxes.Count == 0) return;
            var R = t.R;
            var lo = Enumerable.Range(0, 3).Select(k => boxes.Min(b => b.Min(k))).ToArray();
            var hi = Enumerable.Range(0, 3).Select(k => boxes.Max(b => b.Max(k))).ToArray();
            double[] ScreenDir(double[] v) { var a = Draw.Screen(t, 0, 0, 0); var b = Draw.Screen(t, v); return new[] { b[0] - a[0], b[1] - a[1] }; }
            double Len(double[] v) => Math.Sqrt(v[0] * v[0] + v[1] * v[1]);
            for (int k = 0; k < 3; k++)
            {
                var along = new double[3]; along[k] = 1;
                if (Math.Sqrt(R[k] * R[k] + R[3 + k] * R[3 + k]) < 0.12) continue;   // seen (almost) end-on
                var others = Enumerable.Range(0, 3).Where(i => i != k).ToArray();
                // the edge of the unit the chain runs beside, and the direction it is moved out to
                var at = new double[3]; double[] outDir = null; double best = double.MinValue;
                if (k == 2)
                {
                    // height: the vertical edge most to the left on the screen, moved out further left
                    foreach (var sx in new[] { 0, 1 })
                        foreach (var sy in new[] { 0, 1 })
                        {
                            var p = new[] { sx == 0 ? lo[0] : hi[0], sy == 0 ? lo[1] : hi[1], lo[2] };
                            double u = -Draw.Screen(t, p)[0];
                            if (u > best) { best = u; at = p; }
                        }
                    double bestOut = double.MinValue;
                    foreach (var i in new[] { 0, 1 })
                    {
                        var v = new double[3]; v[i] = Math.Abs(at[i] - lo[i]) < 1e-6 ? -1 : 1;
                        double left = -ScreenDir(v)[0];
                        if (left > bestOut) { bestOut = left; outDir = v; }
                    }
                }
                else
                {
                    // along the floor: on the side turned to the viewer, at the bottom, moved down
                    int other = others.First(i => i != 2);
                    at[other] = R[6 + other] >= 0 ? hi[other] : lo[other]; at[2] = lo[2];
                    var vSide = new double[3]; vSide[other] = R[6 + other] >= 0 ? 1 : -1;
                    var vDown = new[] { 0.0, 0, -1 };
                    var sSide = ScreenDir(vSide); var sDown = ScreenDir(vDown);
                    outDir = sDown[1] >= sSide[1] - 1e-9 && Len(sDown) > 1e-6 ? vDown : vSide;
                }
                if (Len(ScreenDir(outDir)) < 1e-6) continue;
                double outLen = Len(ScreenDir(outDir));
                double unit = 30 / outLen;   // model length (mm) that moves the dimension 30 px out
                // the segments: each box's span along k, the same span drawn once, overlaps on further levels
                var spans = boxes.Select(b => (A: Math.Round(b.Min(k), 1), B: Math.Round(b.Max(k), 1))).Distinct().OrderBy(s => s.A).ThenBy(s => s.B).ToList();
                var levels = new List<double>();   // end of the last segment on each level
                var placed = new List<(double A, double B, int Level)>();
                foreach (var s in spans)
                {
                    int lv = levels.FindIndex(e => s.A >= e - 1);
                    if (lv < 0) { levels.Add(s.B); lv = levels.Count - 1; } else levels[lv] = s.B;
                    placed.Add((s.A, s.B, lv));
                }
                bool overall = spans.Count > 1 || Math.Abs(spans[0].A - lo[k]) > 1 || Math.Abs(spans[0].B - hi[k]) > 1;
                if (overall) placed.Add((lo[k], hi[k], levels.Count));
                int overallAt = overall ? placed.Count - 1 : -1;
                for (int pi = 0; pi < placed.Count; pi++)
                {
                    var (a, b, lv) = placed[pi];
                    var box = pi == overallAt ? null : boxes.FirstOrDefault(x => Math.Abs(Math.Round(x.Min(k), 1) - a) < 0.05 && Math.Abs(Math.Round(x.Max(k), 1) - b) < 0.05);
                    string tag = box == null ? null : $"dim:size:{box.F.No}:{"LWH"[GrAxis(k)]}";
                    var pa = (double[])at.Clone(); pa[k] = a;
                    var pb = (double[])at.Clone(); pb[k] = b;
                    var sa = Draw.Screen(t, pa); var sb = Draw.Screen(t, pb);
                    if (Math.Sqrt(Math.Pow(sb[0] - sa[0], 2) + Math.Pow(sb[1] - sa[1], 2)) < 24) continue;
                    double o = unit * (lv + 1);
                    d.Poly3("ext", t, new[] { Draw.Offset(pa, outDir, unit * 0.15), Draw.Offset(pa, outDir, o + unit * 0.25) }, false);
                    d.Poly3("ext", t, new[] { Draw.Offset(pb, outDir, unit * 0.15), Draw.Offset(pb, outDir, o + unit * 0.25) }, false);
                    EditDim(d, Draw.Screen(t, Draw.Offset(pa, outDir, o)), Draw.Screen(t, Draw.Offset(pb, outDir, o)), U.F(b - a), tag);
                }
            }
        }

        // ------------------------------------------------------------------ the model through a camera

        static void Legend(Draw d, double x, double y)
        {
            double x0 = x;
            foreach (var it in new[] { ("profile", "P150 frame"), ("divider", "P350"), ("dividerMilled", "P350 gefräst"), ("dividerMoved", "moved") })
            {
                d.Line(it.Item1, x, y, x + 22, y);
                d.Text("sub", x + 27, y, it.Item2, 0, 0, "left");
                x += 40 + 7 * it.Item2.Length;
            }
            x += 6;
            d.Rect("corner", x - 2.5, y - 2.5, x + 2.5, y + 2.5); d.Text("sub", x + 7, y, "corner", 0, 0, "left"); x += 56;
            d.Rect("joint", x - 2.5, y - 2.5, x + 2.5, y + 2.5); d.Text("sub", x + 7, y, "joint", 0, 0, "left");
            x = x0; y += 18;
            foreach (var it in new[] { ("openEmpty", "empty"), ("openFP", "FP"), ("openTP", "TP"), ("openRP", "RP"), ("openFilled", "has panel"), ("openBlocked", "blocked") })
            {
                d.Rect(it.Item1, x, y - 5, x + 14, y + 5); d.Text("sub", x + 18, y, it.Item2, 0, 0, "left");
                x += 26 + 7 * it.Item2.Length;
            }
            // dimensions: in Inventor, new parts, the P350 picked
            x += 14;
            foreach (var it in new[] { ("dimEdit", "dimension"), ("dimNew", "new part"), ("dimSel", "picked P350") })
            {
                d.Line(it.Item1, x, y, x + 22, y);
                d.Text("sub", x + 27, y, it.Item2, 0, 0, "left");
                x += 40 + 7 * it.Item2.Length;
            }
        }

        // The axis cross for a camera (X = W red, Y = L green, Z = H blue, as in Inventor).
        static void Axis(Draw d, double[] R, double cx, double cy, double len = 26)
        {
            foreach (var ax in new[] { ("X", 0), ("Y", 1), ("Z", 2) })
            {
                double dx = R[ax.Item2], dy = -R[3 + ax.Item2], n = Math.Sqrt(dx * dx + dy * dy);
                if (n < 0.15) { d.Circle("axisRing", cx, cy, 6); d.Text("axisLabel", cx - 12, cy + 11, ax.Item1); continue; }
                double ex = cx + dx * len, ey = cy + dy * len, ux = dx / n, uy = dy / n;
                d.Line("axis" + ax.Item1, cx, cy, ex, ey);
                d.Poly("axis" + ax.Item1, new[] { ex, ey, ex - ux * 7 - uy * 3, ey - uy * 7 + ux * 3, ex - ux * 7 + uy * 3, ey - uy * 7 - ux * 3 }, true);
                d.Text("axisLabel", ex + ux * 9, ey + uy * 9, ax.Item1);
            }
        }

        static ViewT Transform(GrModel m, GrCamera cam, double[] area, out double[] centre)
        {
            var boxes = Boxes(m);
            centre = boxes.Count == 0 ? new double[3] : Enumerable.Range(0, 3).Select(k => (boxes.Min(b => b.Min(k)) + boxes.Max(b => b.Max(k))) / 2).ToArray();
            var R = cam.Basis();
            double s = cam.Scale;
            double cu = Dot(R, 0, centre), cv = -Dot(R, 1, centre);
            return new ViewT { View = "Free", R = R, S = s, X = area[0] + area[2] / 2 + cam.PanX - cu * s, Y = area[1] + area[3] / 2 + cam.PanY - cv * s };
        }

        static void Model(Draw d, GrModel m, ViewT t)
        {
            var boxes = Boxes(m);
            Openings(d, t, m); Frames(d, t, boxes, m); Dividers(d, t, m); Labels(d, t, boxes, m); Dimensions(d, t, boxes, m);
        }

        // Screen box of shapes (text estimated from its font size).
        static double[] Bounds(IEnumerable<Shape> shapes)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var s in shapes)
            {
                if (s.IsText)
                {
                    double fs = Styles.All.TryGetValue(s.Kind, out var st) && st.FontSize > 0 ? st.FontSize : 12;
                    double w = 0.6 * fs * (s.Text ?? "").Length, h = fs * 1.3, a = s.Angle * Math.PI / 180;
                    double wx = Math.Abs(Math.Cos(a)) * w + Math.Abs(Math.Sin(a)) * h, wy = Math.Abs(Math.Sin(a)) * w + Math.Abs(Math.Cos(a)) * h;
                    double ox = s.Align == "left" ? wx / 2 : 0, cy = s.Y + s.Dy * Math.Cos(a);
                    x0 = Math.Min(x0, s.X + ox - wx / 2); x1 = Math.Max(x1, s.X + ox + wx / 2);
                    y0 = Math.Min(y0, cy - wy / 2); y1 = Math.Max(y1, cy + wy / 2);
                    continue;
                }
                for (int i = 0; i + 1 < s.Points.Length; i += 2) { x0 = Math.Min(x0, s.Points[i]); x1 = Math.Max(x1, s.Points[i]); y0 = Math.Min(y0, s.Points[i + 1]); y1 = Math.Max(y1, s.Points[i + 1]); }
            }
            return x0 > x1 ? null : new[] { x0, y0, x1, y1 };
        }

        // Fits the camera to the area: everything drawn (with labels and dimensions) inside it.
        // Text keeps its size while the model scales, so it is measured and corrected a few times.
        public static void Fit(GrModel m, GrCamera cam, double[] area)
        {
            var boxes = Boxes(m);
            if (boxes.Count == 0) { cam.Scale = 1; return; }
            var R = cam.Basis();
            var pts = boxes.SelectMany(Corners).ToList();
            double du = pts.Max(p => Dot(R, 0, p)) - pts.Min(p => Dot(R, 0, p)), dv = pts.Max(p => Dot(R, 1, p)) - pts.Min(p => Dot(R, 1, p));
            cam.Scale = Math.Max(1e-5, 0.8 * Math.Min(area[2] / Math.Max(du, 1), area[3] / Math.Max(dv, 1)));
            cam.PanX = cam.PanY = 0;
            for (int pass = 0; pass < 5; pass++)
            {
                var d = new Draw();
                Model(d, m, Transform(m, cam, area, out _));
                var b = Bounds(d.Shapes);
                if (b == null) return;
                double bw = b[2] - b[0], bh = b[3] - b[1];
                double f = Math.Min(area[2] / Math.Max(bw, 1), area[3] / Math.Max(bh, 1));
                // centre the drawing in the area, then scale around the area's centre
                cam.PanX += area[0] + area[2] / 2 - (b[0] + b[2]) / 2;
                cam.PanY += area[1] + area[3] / 2 - (b[1] + b[3]) / 2;
                if (f >= 1 && f < 1.04) return;   // all inside, and not much room left over
                double k = Math.Max(0.2, Math.Min(5, f < 1 ? f * 0.99 : 1 + (f - 1) * 0.92));
                cam.Scale *= k; cam.PanX *= k; cam.PanY *= k;
            }
        }

        // The model through the camera into a rectangle of the page (fitted first when the camera asks).
        static void Place(Draw d, GrModel m, GrCamera cam, double[] area)
        {
            cam.Area = area;
            if (cam.Scale <= 0) Fit(m, cam, area);
            d.Shapes.Add(new Shape { Kind = "clip", Points = new[] { area[0], area[1], area[0] + area[2], area[1], area[0] + area[2], area[1] + area[3], area[0], area[1] + area[3] }, Closed = true });
            Model(d, m, Transform(m, cam, area, out _));
            d.Shapes.Add(new Shape { Kind = "unclip", Points = new double[0] });
        }

        static readonly (string, double)[] FrameCols = { ("Box", 0.14), ("L x W x H (profiles)", 0.40), ("Attached", 0.46) };
        static readonly (string, double)[] PartCols = { ("Item", 0.24), ("Qty", 0.08), ("Lengths / sizes (mm)", 0.68) };

        // Rows that fit into a height; the rest as one "... more" line.
        static List<string[]> Cap(List<string[]> rows, double height, double rowH, string more)
        {
            int fit = (int)Math.Floor((height - 22) / rowH) - 1;
            if (rows.Count <= fit || fit < 2) return rows;
            var o = rows.Take(fit - 1).ToList();
            var line = Enumerable.Repeat("", rows[0].Length).ToArray();
            line[0] = $"... {rows.Count - fit + 1} more";
            if (line.Length > 2) line[2] = more;
            o.Add(line);
            return o;
        }

        public static List<Shape> Window(GrModel m, string view, double w, double h)
        {
            var cam = new GrCamera(); cam.Set(view, m?.ServiceSide ?? 1);
            return Window(m, cam, w, h);
        }

        public static List<Shape> Window(GrModel m, GrCamera cam, double w, double h)
        {
            var d = new Draw();
            if (m == null) { d.Text("empty", w / 2, h / 2, "GR view: choose a preset, load a GR export, or read a GR from Inventor"); return d.Shapes; }
            const double head = 86, rowH = 17;
            var tables = Gr.Tables(m);
            double tabMax = Math.Max(90, h * 0.34);
            var frames = Cap(tables.Frames, tabMax, rowH, "");
            var parts = Cap(tables.Parts, tabMax, rowH, "all of them: Files > Save cut list");
            double tabH = Math.Max(Draw.TableHeight(frames.Count, rowH), Draw.TableHeight(parts.Count, rowH));
            double top = head, bottom = Math.Max(top + 120, h - tabH - 40);
            Place(d, m, cam, new[] { 12, top, Math.Max(100, w - 24), bottom - top });
            string seen = string.IsNullOrEmpty(cam.View) ? "free view (drag to turn, wheel to zoom)" : cam.View;
            var focus = m.Focus != 0 ? m.ById(m.Focus) : null;
            d.Text("caption", 10, 14, focus != null ? $"EDITING {focus.Label.ToUpperInvariant()} - {m.Name}" : $"GR {seen.ToUpperInvariant()} - {m.Name}", 0, 0, "left");
            d.Text("sub", 10, 32, focus != null
                ? "Click a dimension to change it, a P350 to show its own (any face)  ·  right-click a profile or P350 to add a P350 across a face  ·  click empty space or Esc: done"
                : (m.RealMap != null ? $"Real part positions ({m.Source})" : $"Layout inferred from the constraints ({m.Source}) - dividers schematic") + "  ·  click a box to edit it, a P350 to show its dimensions, a dimension to change it  ·  drag: turn, Shift-drag: pan, wheel: zoom, double-click: fit", 0, 0, "left");
            Legend(d, 14, 50);
            double y = h - tabH - 6, leftW = Math.Floor((w - 30) * 0.4);
            d.Table("Boxes", FrameCols, frames, 10, y, leftW, rowH, 11);
            int first = d.Shapes.Count;
            d.Table("Cut list and parts (click a length to change it)", PartCols, parts, 20 + leftW, y, w - 30 - leftW, rowH, 11);
            // the P150 and P350 rows of the cut list can be clicked: blue, tagged with their kind
            var rowKind = new Dictionary<int, string> { [0] = "frameProfile", [1] = "divider" };
            double tableTop = y + 22;
            foreach (var s in d.Shapes.Skip(first).Where(s => s.IsText && s.Kind == "bom"))
            {
                int row = (int)Math.Round((s.Y - tableTop) / rowH - 1.5);
                if (!rowKind.TryGetValue(row, out var kind) || parts.Count <= row || parts[row][1] == "0") continue;
                s.Kind = "cutEdit"; s.Tag = "cut:" + kind;
            }
            Axis(d, cam.Basis(), w - 46, 48);
            return d.Shapes;
        }

        public static List<string> Notes(GrModel m)
        {
            string x = U.Times;
            var lines = new List<string>
            {
                $"{m.Name}: {m.Frames.Count} box(es), outer about {U.F(m.Length)} {x} {U.F(m.OuterWidth)} {x} {U.F(m.OuterHeight)} mm (L {x} W {x} H)",
                $"Box sizes are profile lengths (L {x} W {x} H); outer size = profile + {U.F(Gr.CornerAdd)} mm (2 corners).",
                m.RealMap != null ? "Positions from Inventor (range boxes)." : "Layout inferred from the constraints (no positions in the export); dividers drawn schematically."
            };
            lines.AddRange(m.Warnings);
            return lines;
        }

        public static List<Shape> Sheet(GrModel m)
        {
            var d = new Draw();
            d.Rect("frame", 15, 15, 1385, 975);
            if (m == null) { d.Text("empty", 700, 495, "No GR model loaded"); return d.Shapes; }
            int focus = m.Focus; m.Focus = 0;   // the sheet shows the whole GR, nothing greyed out
            try { SheetViews(d, m); } finally { m.Focus = focus; }
            return d.Shapes;
        }

        static void SheetViews(Draw d, GrModel m)
        {
            foreach (var (view, rect, caption) in new[] { ("Isometric", new double[] { 40, 60, 620, 440 }, "ISOMETRIC"), ("Front", new double[] { 700, 60, 660, 200 }, "FRONT (service side)"), ("Top", new double[] { 700, 300, 660, 200 }, "TOP") })
            {
                var cam = new GrCamera(); cam.Set(view, m.ServiceSide);
                Place(d, m, cam, rect);
                d.Text("caption", rect[0], rect[1] - 16, caption + (view == "Isometric" ? " - " + m.Name : ""), 0, 0, "left");
                if (view == "Isometric") Axis(d, cam.Basis(), 70, 480);
            }
            var tables = Gr.Tables(m);
            double y = 520;
            d.Table("Boxes", FrameCols, tables.Frames, 30, y, 520, 22, 12);
            d.Table("Cut list and parts", PartCols, tables.Parts, 570, y, 800, 22, 12);
            double ny = Math.Max(y + Draw.TableHeight(tables.Frames.Count, 22), y + Draw.TableHeight(tables.Parts.Count, 22)) + 24;
            foreach (var line in Notes(m)) { d.Text("note", 30, ny, line, 0, 0, "left"); ny += 20; }
            Legend(d, 30, ny + 6);
            // on paper the dimensions are plain ones (blue = can be clicked, in the window only)
            var plain = new Dictionary<string, string>
            {
                ["dimEdit"] = "dim", ["arrowEdit"] = "arrow", ["labelEdit"] = "label", ["subEdit"] = "sub",
                ["dimNew"] = "dim", ["arrowNew"] = "arrow", ["labelNew"] = "label", ["subNew"] = "sub",
                ["dimSel"] = "dim", ["arrowSel"] = "arrow", ["labelSel"] = "label", ["subSel"] = "sub",
            };
            foreach (var s in d.Shapes) if (plain.TryGetValue(s.Kind ?? "", out var k)) { s.Kind = k; s.Tag = null; }
        }
    }
}
