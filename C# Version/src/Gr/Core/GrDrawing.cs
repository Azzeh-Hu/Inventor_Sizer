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

        static void Frames(Draw d, ViewT t, List<BoxXYZ> boxes)
        {
            // hidden edges first and dashed, so the eye reads the box the right way round
            foreach (bool front in new[] { false, true })
                foreach (var b in boxes)
                    for (int i = 0; i < Edges.Length; i++)
                    {
                        var e = Edge(b, i);
                        bool seen = e.Faces.Any(f => Facing(t.R, f.Axis, f.Side));
                        if (seen != front) continue;
                        d.Poly3(seen ? "profile" : "profileHidden", t, new[] { e.A, e.B }, false);
                        d.Shapes[d.Shapes.Count - 1].Tag = $"edge:{b.F.No}:{i}";
                    }
            foreach (var b in boxes)
                foreach (var p in Corners(b)) { var s = Draw.Screen(t, p); d.Rect("corner", s[0] - 2.5, s[1] - 2.5, s[0] + 2.5, s[1] + 2.5); }
        }

        static void Labels(Draw d, ViewT t, List<BoxXYZ> boxes)
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
                d.Text("label", p[0], p[1], f.Label); d.Shapes[d.Shapes.Count - 1].Tag = "box:" + f.No;
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

        public static List<double[]> JointPoints(GrModel m)
        {
            var list = new List<double[]>();
            foreach (var c in m.Components.Values.Where(c => c.Kind == "joint" && c.Geo != null))
            {
                var f = m.ById(c.Frame);
                if (f == null) continue;
                var mn = (double[])c.Geo.Min.Clone(); var mx = (double[])c.Geo.Max.Clone();
                GrOpenings.ShiftWithSize(c.Geo, f.Size, -1, mn, mx);
                list.Add(Enumerable.Range(0, 3).Select(i => f.Pos[i] + (mn[i] + mx[i]) / 2).ToArray());
            }
            return list;
        }

        // Spacing between subsequent dividers: from the inner face of the frame profile (70 mm) to
        // each divider and on to the far profile, as a chain just past the dividers' ends.
        static void Chains(Draw d, ViewT t, List<DividerLine> lines)
        {
            const double prof = 70;
            int SpacingAxis(DividerLine l)
            {
                var f = l.Frame; var o = f.Pos; var size = f.Size;
                var others = Enumerable.Range(0, 3).Where(i => i != l.Axis).ToArray();
                bool AtFace(int i) => l.A[i] - o[i] < 120 || o[i] + size[i] + Gr.CornerAdd - l.A[i] < 120;
                if (AtFace(others[0]) && !AtFace(others[1])) return others[1];
                if (AtFace(others[1]) && !AtFace(others[0])) return others[0];
                return -1;
            }
            foreach (var g in lines.Select(l => (L: l, S: SpacingAxis(l))).Where(x => x.S >= 0).GroupBy(x => x.L.Frame.Id + "|" + x.S))
            {
                int s = g.First().S;
                var all = g.Select(x => x.L).ToList();
                int k = all.Select(l => l.Axis).OrderByDescending(x => x).First();
                var ls = all.Where(l => l.Axis == k).ToList(); var f = ls[0].Frame;
                var o = f.Pos; var size = f.Size;
                int third = Enumerable.Range(0, 3).First(i => i != k && i != s);
                var vals = new List<double> { o[s] + prof };
                foreach (var v in all.Select(l => Math.Round(l.A[s], 1)).OrderBy(v => v)) if (vals.All(x => Math.Abs(x - v) > 1)) vals.Add(v);
                vals.Sort();
                vals.Add(o[s] + size[s] + Gr.CornerAdd - prof);
                var firstFace = ls.OrderBy(l => l.A[third]).First();
                double atK = ls.Min(l => Math.Min(l.A[k], l.B[k])) - 80, atT = firstFace.A[third];
                for (int i = 1; i < vals.Count; i++)
                {
                    var p = new double[3]; var q = new double[3];
                    p[k] = q[k] = atK; p[third] = q[third] = atT; p[s] = vals[i - 1]; q[s] = vals[i];
                    var sa = Draw.Screen(t, ToDraw(p)); var sb = Draw.Screen(t, ToDraw(q));
                    if (Math.Sqrt(Math.Pow(sb[0] - sa[0], 2) + Math.Pow(sb[1] - sa[1], 2)) < 18) continue;   // seen end-on
                    d.DimLine(sa, sb, U.F(vals[i] - vals[i - 1]));
                }
            }
        }

        static void Dividers(Draw d, ViewT t, GrModel m)
        {
            var lines = DividerLines(m);
            foreach (var l in lines) { d.Poly3(l.Kind, t, new[] { ToDraw(l.A), ToDraw(l.B) }, false); d.Shapes[d.Shapes.Count - 1].Tag = "div:" + l.Name; }
            foreach (var p in JointPoints(m)) { var s = Draw.Screen(t, ToDraw(p)); d.Rect("joint", s[0] - 2.5, s[1] - 2.5, s[0] + 2.5, s[1] + 2.5); }
            if (lines.Count > 0) Chains(d, t, lines);
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
        static void Dimensions(Draw d, ViewT t, List<BoxXYZ> boxes)
        {
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
                foreach (var (a, b, lv) in placed)
                {
                    var pa = (double[])at.Clone(); pa[k] = a;
                    var pb = (double[])at.Clone(); pb[k] = b;
                    var sa = Draw.Screen(t, pa); var sb = Draw.Screen(t, pb);
                    if (Math.Sqrt(Math.Pow(sb[0] - sa[0], 2) + Math.Pow(sb[1] - sa[1], 2)) < 24) continue;
                    double o = unit * (lv + 1);
                    d.Poly3("ext", t, new[] { Draw.Offset(pa, outDir, unit * 0.15), Draw.Offset(pa, outDir, o + unit * 0.25) }, false);
                    d.Poly3("ext", t, new[] { Draw.Offset(pb, outDir, unit * 0.15), Draw.Offset(pb, outDir, o + unit * 0.25) }, false);
                    d.DimLine(Draw.Screen(t, Draw.Offset(pa, outDir, o)), Draw.Screen(t, Draw.Offset(pb, outDir, o)), U.F(b - a));
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
            Openings(d, t, m); Frames(d, t, boxes); Dividers(d, t, m); Labels(d, t, boxes); Dimensions(d, t, boxes);
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
            d.Text("caption", 10, 14, $"GR {seen.ToUpperInvariant()} - {m.Name}", 0, 0, "left");
            d.Text("sub", 10, 32, (m.RealMap != null ? $"Real part positions ({m.Source})" : $"Layout inferred from the constraints ({m.Source}) - dividers schematic") + "  ·  drag: turn, middle / Shift-drag: pan, wheel: zoom, double-click: fit", 0, 0, "left");
            Legend(d, 14, 50);
            double y = h - tabH - 6, leftW = Math.Floor((w - 30) * 0.4);
            d.Table("Boxes", FrameCols, frames, 10, y, leftW, rowH, 11);
            d.Table("Cut list and parts", PartCols, parts, 20 + leftW, y, w - 30 - leftW, rowH, 11);
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
            return d.Shapes;
        }
    }
}
