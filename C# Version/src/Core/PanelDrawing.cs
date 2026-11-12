// Panel drawings ("digital twin"): isometric and orthographic views with wall holes, hinge /
// handle holes, bill of materials and axis; the window view and the SVG drawing sheet.

using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorSizer.Core
{
    public class PanelValues
    {
        public double W, L, H, Z, D, C, T;
        public List<PlacedHole> HW = new List<PlacedHole>();

        public static PanelValues Of(Dictionary<string, double> v, double thickness, List<PlacedHole> hw)
        {
            double d = Standards.Get(v, "HoleDia");
            return new PanelValues
            {
                W = Standards.Get(v, "Width"), L = Standards.Get(v, "Length"), H = Standards.Get(v, "Height"), Z = Standards.Get(v, "HoleZ"),
                D = d > 0 ? d : 4.1, C = Standards.Get(v, "Clearance"), T = thickness, HW = hw ?? new List<PlacedHole>()
            };
        }

        public bool Valid => W > 0 && L > 0 && H > 0;
    }

    public static class PanelDrawing
    {
        static string HoleText(WallLayout w, double d)
        {
            if (w == null) return "";
            int n = (int)Math.Round(w.Holes);
            return n == 0 ? "no holes" : $"{n} {U.Times} {U.Dia}{U.F(d)}";
        }

        // Model point on a wall: along 'y' for the length walls (x fixed), 'x' for the width walls.
        static double[] WallPoint(string along, double fix, double pos, double z) => along == "y" ? new[] { fix, pos, z } : new[] { pos, fix, z };

        static string HoleKind(WallLayout w, bool hidden) => hidden ? "hiddenHole" : w.Errors.Count > 0 ? "holeBad" : "hole";

        static void IsoHoles(Draw d, ViewT t, WallLayout w, string along, double fix, double z, double r, bool hidden)
        {
            if (w == null) return;
            string kind = HoleKind(w, hidden);
            foreach (var pos in w.Positions)
            {
                var ring = new List<double[]>();
                for (int i = 0; i < 24; i++) { double a = i / 24.0 * 2 * Math.PI; ring.Add(WallPoint(along, fix, pos + Math.Cos(a) * r, z + Math.Sin(a) * r)); }
                d.Poly3(kind, t, ring, true);
                if (!hidden)
                {
                    d.Poly3("center", t, new[] { WallPoint(along, fix, pos - 1.8 * r, z), WallPoint(along, fix, pos + 1.8 * r, z) }, false);
                    d.Poly3("center", t, new[] { WallPoint(along, fix, pos, z - 1.8 * r), WallPoint(along, fix, pos, z + 1.8 * r) }, false);
                }
            }
        }

        // Which hardware holes a view shows, and whether directly (else dashed); null = not shown.
        static bool? Visibility(PlacedHole h, string view)
        {
            switch (view)
            {
                case "Isometric": return h.Wall == "face" ? h.Side == "top" : h.Wall == "length" ? h.Side == "xW" : h.Side == "yL";
                case "Top": return h.Wall == "face" ? h.Side == "top" : (bool?)null;
                case "Bottom": return h.Wall == "face" ? h.Side == "bottom" : (bool?)null;
                case "Front": return h.Wall == "length" ? h.Side == "xW" : (bool?)null;
                case "Back": return h.Wall == "length" ? h.Side == "x0" : (bool?)null;
                case "Right": return h.Wall == "width" ? h.Side == "yL" : (bool?)null;
                case "Left": return h.Wall == "width" ? h.Side == "y0" : (bool?)null;
            }
            return null;
        }

        static void HardwareHoles(Draw d, ViewT t, PanelValues v, string view, bool showHidden)
        {
            foreach (var h in v.HW)
            {
                var seen = Visibility(h, view);
                if (seen == null) continue;
                if (seen == false && view != "Isometric" && view != "Top" && view != "Bottom" && !showHidden) continue;
                string kind = h.Bad ? "hwBad" : seen == true ? "hw" : "hwHidden";
                double r = Math.Max(h.Dia / 2, 4.5 / t.S);
                var c = new[] { h.X, h.Y, h.Z };
                d.Ring3(kind, t, c, h.Axis, r);
                if (seen == true)
                {
                    double k = 1.6 * r;
                    var dirs = h.Axis == "x" ? new[] { new[] { 0, k, 0 }, new[] { 0, 0, k } } : h.Axis == "y" ? new[] { new[] { k, 0, 0 }, new[] { 0, 0, k } } : new[] { new[] { k, 0, 0 }, new[] { 0, k, 0 } };
                    foreach (var e in dirs) d.Poly3("center", t, new[] { new[] { c[0] - e[0], c[1] - e[1], c[2] - e[2] }, new[] { c[0] + e[0], c[1] + e[1], c[2] + e[2] } }, false);
                }
            }
        }

        public static void Iso(Draw d, PanelValues v, HoleLayout lay, double[] rect, bool showHidden)
        {
            double W = v.W, L = v.L, H = v.H, off = Math.Max(W, L) * 0.11;
            var pts = new List<double[]> {
                new[] { 0, 0, 0.0 }, new[] { W, 0, 0 }, new[] { 0, L, 0 }, new[] { W, L, 0 }, new[] { 0, 0, H }, new[] { W, 0, H }, new[] { 0, L, H }, new[] { W, L, H },
                new[] { W + off * 1.4, 0, 0 }, new[] { W + off * 1.4, L, 0 }, new[] { 0, L + off * 1.4, 0 }, new[] { W, L + off * 1.4, 0 }, new[] { -off * 0.8, L, H } };
            var t = Draw.Fit("Isometric", pts, rect, new double[] { 30, 34, 30, 30 });
            d.Poly3("face", t, new[] { new[] { W, 0, 0 }, new[] { W, L, 0 }, new[] { W, L, H }, new[] { W, 0, H } }, true);
            d.Poly3("face", t, new[] { new[] { 0, L, 0 }, new[] { W, L, 0 }, new[] { W, L, H }, new[] { 0, L, H } }, true);
            d.Poly3("face", t, new[] { new[] { 0, 0, H }, new[] { W, 0, H }, new[] { W, L, H }, new[] { 0, L, H } }, true);
            double i = Math.Max(v.T + v.C, 6 / t.S);
            d.Poly3("inner", t, new[] { new[] { i, i, H }, new[] { W - i, i, H }, new[] { W - i, L - i, H }, new[] { i, L - i, H } }, true);
            if (lay != null && lay.Split >= 2) d.Poly3("inner", t, new[] { new[] { W / 2, i, H }, new[] { W / 2, L - i, H } }, false);
            double r = Math.Max(v.D / 2, 4 / t.S);
            if (showHidden)
            {
                d.Poly3("hidden", t, new[] { new[] { 0, 0, 0.0 }, new[] { W, 0, 0 } }, false);
                d.Poly3("hidden", t, new[] { new[] { 0, 0, 0.0 }, new[] { 0, L, 0 } }, false);
                d.Poly3("hidden", t, new[] { new[] { 0, 0, 0.0 }, new[] { 0, 0, H } }, false);
                IsoHoles(d, t, lay?.Length, "y", 0, v.Z, r, true);
                IsoHoles(d, t, lay?.Width, "x", 0, v.Z, r, true);
            }
            IsoHoles(d, t, lay?.Length, "y", W, v.Z, r, false);
            IsoHoles(d, t, lay?.Width, "x", L, v.Z, r, false);
            HardwareHoles(d, t, v, "Isometric", showHidden);
            d.IsoDim(t, new[] { 0, L, 0 }, new[] { W, L, 0 }, new[] { 0, off, 0 }, U.F(W), HoleText(lay?.Width, v.D));
            d.IsoDim(t, new[] { W, 0, 0 }, new[] { W, L, 0 }, new[] { off, 0, 0 }, U.F(L), HoleText(lay?.Length, v.D));
            d.IsoDim(t, new[] { 0, L, 0 }, new[] { 0, L, H }, new[] { -off * 0.55, 0, 0 }, U.F(H), "");
        }

        public static void Ortho(Draw d, PanelValues v, HoleLayout lay, string view, double[] rect, bool showHidden)
        {
            double W = v.W, L = v.L, H = v.H, thick = v.T;
            var corners = new List<double[]> { new[] { 0, 0, 0.0 }, new[] { W, 0, 0 }, new[] { 0, L, 0 }, new[] { W, L, 0 }, new[] { 0, 0, H }, new[] { W, 0, H }, new[] { 0, L, H }, new[] { W, L, H } };
            var t = Draw.Fit(view, corners, rect, new double[] { 64, 34, 24, 62 });
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var c in corners) { var q = Draw.Screen(t, c); minX = Math.Min(minX, q[0]); maxX = Math.Max(maxX, q[0]); minY = Math.Min(minY, q[1]); maxY = Math.Max(maxY, q[1]); }
            d.Rect("face", minX, minY, maxX, maxY);
            double r = Math.Max(v.D / 2, 3.5 / t.S);
            bool plan = view == "Top" || view == "Bottom";
            if (!plan)
            {
                WallLayout wall = null; string along = "y"; double fix = 0;
                switch (view) { case "Front": wall = lay?.Length; along = "y"; fix = W; break; case "Back": wall = lay?.Length; along = "y"; fix = 0; break; case "Right": wall = lay?.Width; along = "x"; fix = L; break; case "Left": wall = lay?.Width; along = "x"; fix = 0; break; }
                if (showHidden)
                {
                    double tv = Math.Max(thick * t.S, 3);
                    d.Line("hidden", minX, maxY - tv, maxX, maxY - tv);
                    d.Line("hidden", minX + tv, minY, minX + tv, maxY);
                    d.Line("hidden", maxX - tv, minY, maxX - tv, maxY);
                }
                if (wall != null)
                {
                    string kind = HoleKind(wall, false);
                    foreach (var pos in wall.Positions)
                    {
                        var c = Draw.Screen(t, WallPoint(along, fix, pos, v.Z));
                        d.Circle(kind, c[0], c[1], r * t.S);
                        d.Line("center", c[0] - 1.8 * r * t.S, c[1], c[0] + 1.8 * r * t.S, c[1]);
                        d.Line("center", c[0], c[1] - 1.8 * r * t.S, c[0], c[1] + 1.8 * r * t.S);
                    }
                }
            }
            else
            {
                if (view == "Top")
                {
                    double i = Math.Max(thick + v.C, 5 / t.S);
                    d.Poly3("edge", t, new[] { new[] { i, i, H }, new[] { W - i, i, H }, new[] { W - i, L - i, H }, new[] { i, L - i, H } }, true);
                    if (lay != null && lay.Split >= 2) d.Poly3("edge", t, new[] { new[] { W / 2, i, H }, new[] { W / 2, L - i, H } }, false);
                }
                double tw = Math.Max(thick, 4 / t.S), ext = 8 / t.S;
                foreach (var set in new[] { (lay?.Length, "y", 0.0, 1), (lay?.Length, "y", W, -1), (lay?.Width, "x", 0.0, 1), (lay?.Width, "x", L, -1) })
                {
                    var wall = set.Item1; if (wall == null) continue;
                    string along = set.Item2; double edge = set.Item3; int dir = set.Item4;
                    foreach (var pos in wall.Positions)
                    {
                        double o = edge - dir * ext, inn = edge + dir * (tw + ext);
                        if (along == "y") d.Poly3("center", t, new[] { new[] { o, pos, H }, new[] { inn, pos, H } }, false);
                        else d.Poly3("center", t, new[] { new[] { pos, o, H }, new[] { pos, inn, H } }, false);
                        if (!showHidden) continue;
                        foreach (var side in new[] { -r, r })
                        {
                            string kind = HoleKind(wall, true);
                            if (along == "y") d.Poly3(kind, t, new[] { new[] { edge, pos + side, H }, new[] { edge + dir * tw, pos + side, H } }, false);
                            else d.Poly3(kind, t, new[] { new[] { pos + side, edge, H }, new[] { pos + side, edge + dir * tw, H } }, false);
                        }
                    }
                }
            }
            HardwareHoles(d, t, v, view, showHidden);
            double hSpan = view == "Left" || view == "Right" ? W : L, vSpan = plan ? W : H;
            string hSub = view == "Left" || view == "Right" ? HoleText(lay?.Width, v.D) : HoleText(lay?.Length, v.D);
            string vSub = plan ? HoleText(lay?.Width, v.D) : "";
            d.Line("ext", minX, maxY + 4, minX, maxY + 40); d.Line("ext", maxX, maxY + 4, maxX, maxY + 40);
            d.DimLine(new[] { minX, maxY + 34 }, new[] { maxX, maxY + 34 }, U.F(hSpan), hSub);
            d.Line("ext", minX - 4, minY, minX - 40, minY); d.Line("ext", minX - 4, maxY, minX - 40, maxY);
            d.DimLine(new[] { minX - 34, maxY }, new[] { minX - 34, minY }, U.F(vSpan), vSub);
        }

        static readonly (string, double)[] BomCols = { ("Pos", 0.07), ("Part number", 0.27), ("Description", 0.30), ("Qty", 0.08), ("Size (mm)", 0.28) };

        static List<string[]> BomRows(Bom b) => b.Rows.Select(r => new[] { r.Pos, r.Part, r.Desc, r.Qty, r.Size }).ToList();

        public static double BomHeight(Bom b, double rowH) => b == null ? 0 : Draw.TableHeight(b.Rows.Count, rowH) + (string.IsNullOrEmpty(b.Note) ? 0 : 20);

        public static List<string> HoleNotes(HoleLayout lay, PanelValues v)
        {
            var lines = new List<string> { $"Holes {U.Dia}{U.F(v.D)} mm, centre height {U.F(v.Z)} mm from the bottom" };
            if (lay != null)
                foreach (var w in new[] { lay.Length, lay.Width })
                {
                    int n = (int)Math.Round(w.Holes);
                    lines.Add(n == 0 ? $"{w.Wall} walls (2x): no holes" : $"{w.Wall} walls (2x): {n} hole(s) at {string.Join(" / ", w.Positions.Select(U.F))} mm from the wall end");
                }
            if (v.HW.Count > 0)
            {
                int face = v.HW.Count(h => h.Wall == "face");
                lines.Add($"Door hardware (orange): {v.HW.Count} hole(s), {face} in the face, {v.HW.Count - face} in the walls; dashed = far side");
                foreach (var g in v.HW.GroupBy(h => U.F(h.Dia))) lines.Add($"  {U.Dia}{g.Key} mm: {g.Count()}x");
            }
            lines.Add("All dimensions in mm. Drawing not to scale.");
            return lines;
        }

        public static List<Shape> Window(PanelValues v, HoleLayout lay, string view, double w, double h, bool showHidden, Bom bom)
        {
            var d = new Draw();
            if (!v.Valid || w < 160 || h < 160) { d.Text("empty", w / 2, h / 2, "Enter the dimensions"); return d.Shapes; }
            double rowH = 19, bomH = BomHeight(bom, rowH), viewH = Math.Max(120, h - bomH - 16);
            var rect = new[] { 0, 0, w, viewH };
            if (view == "Isometric") Iso(d, v, lay, rect, showHidden); else Ortho(d, v, lay, view, rect, showHidden);
            d.Text("caption", 10, 14, view.ToUpperInvariant(), 0, 0, "left");
            if (v.HW.Count > 0) d.Text("sub", 10, 32, "Door hardware holes: bold outline (dashed = far side)", 0, 0, "left");
            if (bom != null)
            {
                double bomW = Math.Min(620, w - 110);
                d.Table(bom.Title, BomCols, BomRows(bom), w - bomW - 10, h - bomH - 8, bomW, rowH, 11, bom.Note);
            }
            d.Axis(view, 48, h - 48);
            return d.Shapes;
        }

        public static List<Shape> Sheet(PanelValues v, HoleLayout lay, Bom bom, bool showHidden)
        {
            var d = new Draw();
            d.Rect("frame", 15, 15, 1385, 975);
            if (!v.Valid) { d.Text("empty", 700, 495, "Enter the dimensions"); return d.Shapes; }
            Iso(d, v, lay, new double[] { 30, 30, 640, 450 }, showHidden);
            d.Text("caption", 40, 46, "ISOMETRIC", 0, 0, "left");
            d.Axis("Isometric", 70, 450);
            Ortho(d, v, lay, "Front", new double[] { 690, 30, 680, 300 }, true);
            d.Text("caption", 700, 46, "FRONT", 0, 0, "left");
            Ortho(d, v, lay, "Top", new double[] { 690, 340, 680, 360 }, true);
            d.Text("caption", 700, 356, "TOP", 0, 0, "left");
            d.Line("table", 680, 30, 680, 960);
            double y = 530;
            d.Text("bomTitle", 40, y, "Notes", 0, 0, "left");
            foreach (var line in HoleNotes(lay, v)) { y += 24; d.Text("note", 40, y, line, 0, 0, "left"); }
            if (bom != null) d.Table(bom.Title, BomCols, BomRows(bom), 700, 960 - BomHeight(bom, 24), 670, 24, 12, bom.Note);
            return d.Shapes;
        }
    }
}
