// GR drawings: boxes as profile wireframes with corners and labels, dividers (P350 purple,
// P350 gefraest green, moved orange) and joints (gold) at their real positions, spacing chains
// between subsequent dividers, a legend, a box table and a cut list.
// Drawing axes as for panels: x = width, y = length (along the unit), z = height.

using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorSizer.Core
{
    public class DividerLine { public double[] A, B; public int Axis; public string Name, Kind; public GrFrame Frame; public bool Real; }

    public static class GrDrawing
    {
        class BoxXYZ { public GrFrame F; public double X0, X1, Y0, Y1, Z0, Z1; }

        static List<BoxXYZ> Boxes(GrModel m)
        {
            double a = Gr.CornerAdd;
            return m.Frames.Select(f => new BoxXYZ { F = f, X0 = f.Y, X1 = f.Y + f.Wy + a, Y0 = f.X, Y1 = f.X + f.Lx + a, Z0 = f.Z, Z1 = f.Z + f.Hz + a }).ToList();
        }

        static double[][] Corners(BoxXYZ b) => new[]
        {
            new[] { b.X0, b.Y0, b.Z0 }, new[] { b.X1, b.Y0, b.Z0 }, new[] { b.X1, b.Y1, b.Z0 }, new[] { b.X0, b.Y1, b.Z0 },
            new[] { b.X0, b.Y0, b.Z1 }, new[] { b.X1, b.Y0, b.Z1 }, new[] { b.X1, b.Y1, b.Z1 }, new[] { b.X0, b.Y1, b.Z1 },
        };

        static readonly int[][] Edges = { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 0 }, new[] { 4, 5 }, new[] { 5, 6 }, new[] { 6, 7 }, new[] { 7, 4 }, new[] { 0, 4 }, new[] { 1, 5 }, new[] { 2, 6 }, new[] { 3, 7 } };

        static List<double[]> AllPoints(List<BoxXYZ> boxes) => boxes.SelectMany(Corners).ToList();

        static void Frames(Draw d, ViewT t, List<BoxXYZ> boxes)
        {
            foreach (var b in boxes)
            {
                var c = Corners(b);
                foreach (var e in Edges) d.Poly3("profile", t, new[] { c[e[0]], c[e[1]] }, false);
                foreach (var p in c) { var s = Draw.Screen(t, p); d.Rect("corner", s[0] - 2.5, s[1] - 2.5, s[0] + 2.5, s[1] + 2.5); }
            }
        }

        static void Labels(Draw d, ViewT t, List<BoxXYZ> boxes, string view)
        {
            foreach (var b in boxes)
            {
                var f = b.F;
                double z = view == "Top" || view == "Bottom" ? b.Z1 : (b.Z0 + b.Z1) / 2, mx = (b.X0 + b.X1) / 2;
                var p = Draw.Screen(t, mx, (b.Y0 + b.Y1) / 2, z);
                var e0 = Draw.Screen(t, mx, b.Y0, z); var e1 = Draw.Screen(t, mx, b.Y1, z);
                double span = view == "Left" || view == "Right" ? 999 : Math.Sqrt(Math.Pow(e1[0] - e0[0], 2) + Math.Pow(e1[1] - e0[1], 2));
                d.Text("label", p[0], p[1], f.Label);
                if (span < 80) continue;
                var extra = new List<string>();
                if (f.Panels.Count > 0) extra.Add($"{f.Panels.Count} pan.");
                if (f.Dividers.Count > 0) extra.Add($"{f.Dividers.Count} div.");
                if (extra.Count > 0) d.Text("sub", p[0], p[1] + 14, string.Join(" / ", extra));
            }
        }

        static int DimIndex(string dim) => dim == "L" ? 0 : dim == "W" ? 1 : 2;
        static double[] ToDraw(double[] p) => new[] { p[1], p[0], p[2] };   // GR (L, W, H) -> drawing (x = W, y = L, z = H)

        static string DividerKind(GrComponent c) =>
            c.Position != null && c.Position != c.OrigPosition ? "dividerMoved" : c.Base.Contains("gefr") ? "dividerMilled" : "divider";

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
                    foreach (var r in c.Follow) if (DimIndex(r.Dim) == k) mx[k] += size[k] - c.Geo.Size0[k];
                    if (c.Position != null && c.Geo.Pos0 != null && c.Position != c.Geo.Pos0)
                    {
                        m.Components.TryGetValue(c.PosPartner ?? "", out var partner);
                        int pk = partner != null && partner.Follow.Count > 0 ? DimIndex(partner.Follow[0].Dim) : -1;
                        int s = Enumerable.Range(0, 3).First(i => i != k && i != pk);
                        double delta = c.Position.Value - c.Geo.Pos0.Value;
                        mn[s] += delta; mx[s] += delta;
                    }
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
                list.Add(new DividerLine { A = p0, B = p1, Axis = k, Name = c.Name, Kind = DividerKind(c), Frame = f, Real = c.Geo != null });
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
                list.Add(Enumerable.Range(0, 3).Select(i => f.Pos[i] + (c.Geo.Min[i] + c.Geo.Max[i]) / 2).ToArray());
            }
            return list;
        }

        // Spacing between subsequent dividers: from the inner face of the frame profile (70 mm) to
        // each divider and on to the far profile, as a chain just past the dividers' ends.
        static void Chains(Draw d, ViewT t, List<DividerLine> lines)
        {
            const double prof = 70;
            foreach (var g in lines.GroupBy(l => l.Frame.Id + "|" + l.Axis))
            {
                var ls = g.ToList(); int k = ls[0].Axis; var f = ls[0].Frame;
                var o = f.Pos; var size = f.Size;
                var others = Enumerable.Range(0, 3).Where(i => i != k).ToArray();
                var spread = others.Select(i => ls.Max(l => l.A[i]) - ls.Min(l => l.A[i])).ToArray();
                int s = spread[0] >= spread[1] ? others[0] : others[1];
                if (ls.Count == 1)
                {
                    var mid = others.Select(i => Math.Abs(ls[0].A[i] - (o[i] + (size[i] + Gr.CornerAdd) / 2))).ToArray();
                    s = mid[0] >= mid[1] ? others[0] : others[1];
                }
                int third = others.First(i => i != s);
                var vals = new List<double> { o[s] + prof };
                vals.AddRange(ls.Select(l => l.A[s]).Distinct().OrderBy(v => v));
                vals.Add(o[s] + size[s] + Gr.CornerAdd - prof);
                double atK = ls.Min(l => Math.Min(l.A[k], l.B[k])) - 80, atT = ls.Average(l => l.A[third]);
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
            foreach (var l in lines) d.Poly3(l.Kind, t, new[] { ToDraw(l.A), ToDraw(l.B) }, false);
            foreach (var p in JointPoints(m)) { var s = Draw.Screen(t, ToDraw(p)); d.Rect("joint", s[0] - 2.5, s[1] - 2.5, s[0] + 2.5, s[1] + 2.5); }
            if (lines.Count > 0) Chains(d, t, lines);
        }

        static void Legend(Draw d, double x, double y)
        {
            foreach (var it in new[] { ("profile", "P150 frame"), ("divider", "P350"), ("dividerMilled", "P350 gefräst"), ("dividerMoved", "moved") })
            {
                d.Line(it.Item1, x, y, x + 22, y);
                d.Text("sub", x + 27, y, it.Item2, 0, 0, "left");
                x += 40 + 7 * it.Item2.Length;
            }
            x += 6;
            d.Rect("corner", x - 2.5, y - 2.5, x + 2.5, y + 2.5); d.Text("sub", x + 7, y, "corner", 0, 0, "left"); x += 56;
            d.Rect("joint", x - 2.5, y - 2.5, x + 2.5, y + 2.5); d.Text("sub", x + 7, y, "joint", 0, 0, "left");
        }

        static void Iso(Draw d, GrModel m, double[] rect)
        {
            var boxes = Boxes(m);
            double L = m.Length, W = m.OuterWidth, H = m.OuterHeight, off = Math.Max(W, L) * 0.07;
            var pts = AllPoints(boxes); pts.Add(new[] { W + off * 1.4, 0, 0 }); pts.Add(new[] { 0, L + off * 1.4, 0 }); pts.Add(new[] { -off, L, H });
            var t = Draw.Fit("Isometric", pts, rect, new double[] { 30, 34, 30, 30 });
            Frames(d, t, boxes); Dividers(d, t, m); Labels(d, t, boxes, "Isometric");
            d.IsoDim(t, new[] { 0, L, 0 }, new[] { W, L, 0 }, new[] { 0, off, 0 }, U.F(W), "width");
            d.IsoDim(t, new[] { W, 0, 0 }, new[] { W, L, 0 }, new[] { off, 0, 0 }, U.F(L), "length");
            d.IsoDim(t, new[] { 0, L, 0 }, new[] { 0, L, H }, new[] { -off * 0.55, 0, 0 }, U.F(H), "");
        }

        static void Ortho(Draw d, GrModel m, string view, double[] rect)
        {
            var boxes = Boxes(m);
            var t = Draw.Fit(view, AllPoints(boxes), rect, new double[] { 64, 34, 24, 70 });
            Frames(d, t, boxes); Dividers(d, t, m); Labels(d, t, boxes, view);
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in AllPoints(boxes)) { var s = Draw.Screen(t, p); minX = Math.Min(minX, s[0]); maxX = Math.Max(maxX, s[0]); minY = Math.Min(minY, s[1]); maxY = Math.Max(maxY, s[1]); }
            if (view == "Front" || view == "Back" || view == "Top" || view == "Bottom")
            {
                foreach (var b in boxes.OrderBy(b => b.Y0))
                {
                    if (b.Z0 > 0 && view != "Top" && view != "Bottom") continue;
                    var a = Draw.Screen(t, b.X0, b.Y0, 0); var c = Draw.Screen(t, b.X0, b.Y1, 0);
                    if (a[0] > c[0]) { var tmp = a; a = c; c = tmp; }
                    d.DimLine(new[] { a[0], maxY + 22 }, new[] { c[0], maxY + 22 }, U.F(b.Y1 - b.Y0));
                }
                d.Line("ext", minX, maxY + 4, minX, maxY + 62); d.Line("ext", maxX, maxY + 4, maxX, maxY + 62);
                d.DimLine(new[] { minX, maxY + 56 }, new[] { maxX, maxY + 56 }, U.F(m.Length));
            }
            else
            {
                d.Line("ext", minX, maxY + 4, minX, maxY + 40); d.Line("ext", maxX, maxY + 4, maxX, maxY + 40);
                d.DimLine(new[] { minX, maxY + 34 }, new[] { maxX, maxY + 34 }, U.F(m.OuterWidth));
            }
            double vSpan = view == "Top" || view == "Bottom" ? m.OuterWidth : m.OuterHeight;
            d.Line("ext", minX - 4, minY, minX - 40, minY); d.Line("ext", minX - 4, maxY, minX - 40, maxY);
            d.DimLine(new[] { minX - 34, maxY }, new[] { minX - 34, minY }, U.F(vSpan));
        }

        static readonly (string, double)[] FrameCols = { ("Box", 0.14), ("L x W x H (profiles)", 0.40), ("Attached", 0.46) };
        static readonly (string, double)[] PartCols = { ("Item", 0.24), ("Qty", 0.08), ("Lengths / sizes (mm)", 0.68) };

        public static List<Shape> Window(GrModel m, string view, double w, double h)
        {
            var d = new Draw();
            if (m == null) { d.Text("empty", w / 2, h / 2, "GR view: choose a preset, load a GR export, or read a GR from Inventor"); return d.Shapes; }
            var tables = Gr.Tables(m);
            double rowH = 17, tabH = Math.Max(Draw.TableHeight(tables.Frames.Count, rowH), Draw.TableHeight(tables.Parts.Count, rowH));
            var rect = new[] { 0, 0, w, Math.Max(140, h - tabH - 16) };
            if (view == "Isometric") Iso(d, m, rect); else Ortho(d, m, view, rect);
            d.Text("caption", 10, 14, $"GR {view.ToUpperInvariant()} - {m.Name}", 0, 0, "left");
            d.Text("sub", 10, 32, m.RealMap != null ? $"Real part positions ({m.Source})" : $"Layout inferred from the constraints ({m.Source}) - dividers schematic", 0, 0, "left");
            Legend(d, 14, 50);
            double y = h - tabH - 6, leftW = Math.Floor((w - 30) * 0.4);
            d.Table("Boxes", FrameCols, tables.Frames, 10, y, leftW, rowH, 11);
            d.Table("Cut list and parts", PartCols, tables.Parts, 20 + leftW, y, w - 30 - leftW, rowH, 11);
            d.Axis(view, w - 60, 70);
            return d.Shapes;
        }

        public static List<string> Notes(GrModel m)
        {
            string x = U.Times;
            var lines = new List<string>
            {
                $"{m.Name}: {m.Frames.Count} box(es), outer about {U.F(m.Length)} {x} {U.F(m.OuterWidth)} {x} {U.F(m.OuterHeight)} mm (L {x} W {x} H)",
                $"Box sizes are profile lengths (L {x} W {x} H); outer size = profile + {U.F(Gr.CornerAdd)} mm (2 corners of 130).",
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
            Iso(d, m, new double[] { 30, 30, 640, 420 });
            d.Text("caption", 40, 46, $"ISOMETRIC - {m.Name}", 0, 0, "left");
            d.Axis("Isometric", 70, 420);
            Ortho(d, m, "Front", new double[] { 690, 30, 680, 230 }); d.Text("caption", 700, 46, "FRONT", 0, 0, "left");
            Ortho(d, m, "Top", new double[] { 690, 270, 680, 230 }); d.Text("caption", 700, 286, "TOP", 0, 0, "left");
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
