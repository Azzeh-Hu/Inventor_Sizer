// Vector drawings: a drawing is a list of shapes in view coordinates (px). The window paints
// them with GDI+ (App/Painter.cs), the SVG export writes them as SVG. Styles are defined once
// here, so both look the same.
// Panel model axes: x = Width, y = Length, z = Height (mm).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace InventorSizer.Core
{
    public class Shape
    {
        public string Kind;
        public double[] Points;   // x0, y0, x1, y1, ... (null for text)
        public bool Closed;
        public double X, Y, Angle, Dy;
        public string Text, Align = "center";
        public bool IsText => Points == null;
    }

    public class Style
    {
        public string Stroke, Fill; public double Width = 1; public float[] Dash;
        public double FontSize; public bool Bold; public string TextColor;
    }

    public class ViewT { public string View; public double S, X, Y; }

    public static class Styles
    {
        static Style L(string stroke, double w, float[] dash = null, string fill = null) => new Style { Stroke = stroke, Width = w, Dash = dash, Fill = fill };
        static Style T(string color, double size, bool bold = false) => new Style { TextColor = color, FontSize = size, Bold = bold };
        static readonly float[] Dash = { 6, 4 }, Small = { 3, 2 }, Center = { 6, 2, 1, 2 };

        public static readonly Dictionary<string, Style> All = new Dictionary<string, Style>
        {
            ["face"] = L("#000000", 1.3, null, "#ffffff"), ["edge"] = L("#000000", 1), ["inner"] = L("#808080", 1, Dash), ["hidden"] = L("#808080", 1, Dash),
            ["hole"] = L("#000000", 1.1), ["holeBad"] = L("#8b0000", 1.5, null, "#ff0000"), ["hiddenHole"] = L("#808080", 1, Small), ["center"] = L("#808080", 0.8, Center),
            ["dim"] = L("#696969", 1), ["ext"] = L("#c0c0c0", 1), ["arrow"] = new Style { Fill = "#696969" }, ["table"] = L("#000000", 1), ["frame"] = L("#000000", 1.6),
            ["axisX"] = L("#ff0000", 1.6, null, "#ff0000"), ["axisY"] = L("#228b22", 1.6, null, "#228b22"), ["axisZ"] = L("#0000ff", 1.6, null, "#0000ff"), ["axisRing"] = L("#000000", 1),
            ["hw"] = L("#ff8c00", 1.4), ["hwHidden"] = L("#ff8c00", 1.3, new float[] { 2.5f, 1.5f }), ["hwBad"] = L("#8b0000", 1.5, null, "#ff0000"),
            ["profile"] = L("#4682b4", 2.2), ["corner"] = L("#000000", 1, null, "#696969"), ["joint"] = L("#000000", 1, null, "#ffd700"),
            ["divider"] = L("#9370db", 2), ["dividerMilled"] = L("#2e8b57", 2), ["dividerMoved"] = L("#ff8c00", 2.4),
            ["label"] = T("#000000", 13, true), ["sub"] = T("#696969", 11), ["caption"] = T("#696969", 14, true), ["bom"] = T("#000000", 12),
            ["bomHead"] = T("#000000", 12, true), ["bomTitle"] = T("#000000", 14, true), ["axisLabel"] = T("#000000", 11, true), ["note"] = T("#000000", 12), ["empty"] = T("#808080", 15),
        };
    }

    public class Draw
    {
        public readonly List<Shape> Shapes = new List<Shape>();
        public static readonly double Cos30 = Math.Cos(Math.PI / 6);
        public static readonly string[] Views = { "Isometric", "Front", "Back", "Left", "Right", "Top", "Bottom" };
        static readonly Dictionary<string, double[]> Viewer = new Dictionary<string, double[]>
        {
            ["Front"] = new double[] { 1, 0, 0 }, ["Back"] = new double[] { -1, 0, 0 }, ["Right"] = new double[] { 0, 1, 0 },
            ["Left"] = new double[] { 0, -1, 0 }, ["Top"] = new double[] { 0, 0, 1 }, ["Bottom"] = new double[] { 0, 0, -1 },
        };

        // Projection to unscaled view coordinates (u right, v down; right-handed views).
        public static double[] Proj(string view, double x, double y, double z)
        {
            switch (view)
            {
                case "Front": return new[] { y, -z };
                case "Back": return new[] { -y, -z };
                case "Right": return new[] { -x, -z };
                case "Left": return new[] { x, -z };
                case "Top": return new[] { y, x };
                case "Bottom": return new[] { y, -x };
                default: return new[] { (x - y) * Cos30, (x + y) * 0.5 - z };
            }
        }

        public static double[] Screen(ViewT t, double x, double y, double z) { var p = Proj(t.View, x, y, z); return new[] { p[0] * t.S + t.X, p[1] * t.S + t.Y }; }
        public static double[] Screen(ViewT t, double[] p) => Screen(t, p[0], p[1], p[2]);

        // Scale and offset that fit model points (plus margins in px) into a rectangle.
        public static ViewT Fit(string view, IEnumerable<double[]> pts, double[] rect, double[] margin)
        {
            double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            foreach (var c in pts)
            {
                var q = Proj(view, c[0], c[1], c[2]);
                minU = Math.Min(minU, q[0]); maxU = Math.Max(maxU, q[0]); minV = Math.Min(minV, q[1]); maxV = Math.Max(maxV, q[1]);
            }
            double w = rect[2] - margin[0] - margin[2], h = rect[3] - margin[1] - margin[3];
            double s = Math.Max(Math.Min(w / Math.Max(maxU - minU, 1e-6), h / Math.Max(maxV - minV, 1e-6)), 1e-6);
            return new ViewT
            {
                View = view, S = s,
                X = rect[0] + margin[0] + (w - (maxU - minU) * s) / 2 - minU * s,
                Y = rect[1] + margin[1] + (h - (maxV - minV) * s) / 2 - minV * s
            };
        }

        public void Poly(string kind, double[] flat, bool closed) => Shapes.Add(new Shape { Kind = kind, Points = flat, Closed = closed });
        public void Line(string kind, double x1, double y1, double x2, double y2) => Poly(kind, new[] { x1, y1, x2, y2 }, false);
        public void Rect(string kind, double x1, double y1, double x2, double y2) => Poly(kind, new[] { x1, y1, x2, y1, x2, y2, x1, y2 }, true);
        public void Text(string kind, double x, double y, string text, double angle = 0, double dy = 0, string align = "center") =>
            Shapes.Add(new Shape { Kind = kind, X = x, Y = y, Text = text, Angle = angle, Dy = dy, Align = align });

        public void Poly3(string kind, ViewT t, IEnumerable<double[]> pts, bool closed)
        {
            var flat = new List<double>();
            foreach (var p in pts) { var s = Screen(t, p); flat.Add(s[0]); flat.Add(s[1]); }
            Poly(kind, flat.ToArray(), closed);
        }

        public void Circle(string kind, double cx, double cy, double r)
        {
            var pts = new double[48];
            for (int i = 0; i < 24; i++) { double a = i / 24.0 * 2 * Math.PI; pts[2 * i] = cx + Math.Cos(a) * r; pts[2 * i + 1] = cy + Math.Sin(a) * r; }
            Poly(kind, pts, true);
        }

        // Circle of radius r around a model point, across the given axis (x, y or z).
        public void Ring3(string kind, ViewT t, double[] c, string axis, double r)
        {
            var pts = new List<double[]>();
            for (int i = 0; i < 24; i++)
            {
                double a = i / 24.0 * 2 * Math.PI, p = Math.Cos(a) * r, q = Math.Sin(a) * r;
                pts.Add(axis == "x" ? new[] { c[0], c[1] + p, c[2] + q } : axis == "y" ? new[] { c[0] + p, c[1], c[2] + q } : new[] { c[0] + p, c[1] + q, c[2] });
            }
            Poly3(kind, t, pts, true);
        }

        // Dimension line between screen points A and B, with arrows and a label (+ second line).
        public void DimLine(double[] a, double[] b, string label, string sub = "")
        {
            Line("dim", a[0], a[1], b[0], b[1]);
            double len = Math.Max(Math.Sqrt(Math.Pow(b[0] - a[0], 2) + Math.Pow(b[1] - a[1], 2)), 1);
            double ux = (b[0] - a[0]) / len, uy = (b[1] - a[1]) / len;
            foreach (var e in new[] { new[] { a[0], a[1], 1 }, new[] { b[0], b[1], -1 } })
            {
                double bx = e[0] + ux * 11 * e[2], by = e[1] + uy * 11 * e[2];
                Poly("arrow", new[] { e[0], e[1], bx - uy * 3.2, by + ux * 3.2, bx + uy * 3.2, by - ux * 3.2 }, true);
            }
            double angle = Math.Atan2(b[1] - a[1], b[0] - a[0]) * 180 / Math.PI;
            if (angle > 90) angle -= 180;
            if (angle < -90) angle += 180;
            double mx = (a[0] + b[0]) / 2, my = (a[1] + b[1]) / 2;
            Text("label", mx, my, label, angle, -9);
            if (!string.IsNullOrEmpty(sub)) Text("sub", mx, my, sub, angle, 9);
        }

        public static double[] Offset(double[] p, double[] o, double k) => new[] { p[0] + o[0] * k, p[1] + o[1] * k, p[2] + o[2] * k };

        public void IsoDim(ViewT t, double[] a, double[] b, double[] o, string label, string sub)
        {
            Poly3("ext", t, new[] { Offset(a, o, 0.12), Offset(a, o, 1.08) }, false);
            Poly3("ext", t, new[] { Offset(b, o, 0.12), Offset(b, o, 1.08) }, false);
            DimLine(Screen(t, Offset(a, o, 1)), Screen(t, Offset(b, o, 1)), label, sub);
        }

        public void Axis(string view, double cx, double cy, double len = 34)
        {
            var o = Proj(view, 0, 0, 0);
            foreach (var ax in new[] { ("X", 1.0, 0.0, 0.0), ("Y", 0.0, 1.0, 0.0), ("Z", 0.0, 0.0, 1.0) })
            {
                var p = Proj(view, ax.Item2, ax.Item3, ax.Item4);
                double dx = p[0] - o[0], dy = p[1] - o[1], n = Math.Sqrt(dx * dx + dy * dy);
                if (n < 1e-6)
                {   // pointing straight at / away from the viewer: dot = towards, cross = away
                    var v = Viewer[view];
                    bool toward = ax.Item2 * v[0] + ax.Item3 * v[1] + ax.Item4 * v[2] > 0;
                    Circle("axisRing", cx, cy, 7);
                    if (toward) Circle("axis" + ax.Item1, cx, cy, 2.5);
                    else { Line("axis" + ax.Item1, cx - 4.5, cy - 4.5, cx + 4.5, cy + 4.5); Line("axis" + ax.Item1, cx - 4.5, cy + 4.5, cx + 4.5, cy - 4.5); }
                    Text("axisLabel", cx - 13, cy + 12, ax.Item1);
                    continue;
                }
                double ux = dx / n, uy = dy / n, ex = cx + ux * len, ey = cy + uy * len;
                Line("axis" + ax.Item1, cx, cy, ex, ey);
                Poly("axis" + ax.Item1, new[] { ex, ey, ex - ux * 8 - uy * 3.5, ey - uy * 8 + ux * 3.5, ex - ux * 8 + uy * 3.5, ey - uy * 8 - ux * 3.5 }, true);
                Text("axisLabel", ex + ux * 10, ey + uy * 10, ax.Item1);
            }
        }

        // Text cut to roughly fit a width in px.
        public static string FitText(string text, double width, double fontPx)
        {
            int max = (int)Math.Floor(width / (0.55 * fontPx));
            if (text.Length <= max || max < 4) return text;
            return text.Substring(0, max - 1) + "…";
        }

        public static double TableHeight(int rows, double rowH) => 22 + rowH * (rows + 1);

        public void Table(string title, (string Name, double Frac)[] cols, List<string[]> rows, double x, double y, double w, double rowH, double fontPx, string note = null)
        {
            Text("bomTitle", x, y + 9, title, 0, 0, "left");
            double top = y + 22, bottom = top + rowH * (rows.Count + 1);
            Rect("table", x, top, x + w, bottom);
            for (int k = 1; k <= rows.Count; k++) Line("table", x, top + rowH * k, x + w, top + rowH * k);
            double cx = x;
            for (int c = 0; c < cols.Length; c++)
            {
                double cw = w * cols[c].Frac;
                if (c > 0) Line("table", cx, top, cx, bottom);
                Text("bomHead", cx + 5, top + rowH / 2, cols[c].Name, 0, 0, "left");
                for (int k = 0; k < rows.Count; k++) Text("bom", cx + 5, top + rowH * (k + 1.5), FitText(rows[k][c] ?? "", cw - 10, fontPx), 0, 0, "left");
                cx += cw;
            }
            if (!string.IsNullOrEmpty(note)) Text("note", x, bottom + 11, note, 0, 0, "left");
        }

        // ------------------------------------------------------------------ SVG

        public static string Svg(List<Shape> shapes, double w, double h, string title)
        {
            string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(w)}\" height=\"{N(h)}\" viewBox=\"0 0 {N(w)} {N(h)}\">");
            sb.AppendLine($"  <title>{Esc(title)}</title>");
            sb.AppendLine("  <rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>");
            foreach (var s in shapes)
            {
                Styles.All.TryGetValue(s.Kind, out var st);
                st = st ?? new Style { Stroke = "#000000" };
                if (s.IsText)
                {
                    string anchor = s.Align == "left" ? "start" : "middle";
                    sb.AppendLine($"  <text transform=\"translate({N(s.X)} {N(s.Y)}) rotate({N(s.Angle)})\" y=\"{N(s.Dy)}\" text-anchor=\"{anchor}\" dominant-baseline=\"middle\" " +
                                  $"font-family=\"Segoe UI, Arial, sans-serif\" font-size=\"{N(st.FontSize)}\"{(st.Bold ? " font-weight=\"bold\"" : "")} fill=\"{st.TextColor}\">{Esc(s.Text)}</text>");
                    continue;
                }
                var pts = new StringBuilder();
                for (int k = 0; k < s.Points.Length; k += 2) pts.Append(k > 0 ? " " : "").Append(N(s.Points[k])).Append(',').Append(N(s.Points[k + 1]));
                string stroke = st.Stroke == null ? "stroke=\"none\"" : $"stroke=\"{st.Stroke}\" stroke-width=\"{N(st.Width)}\"" +
                                (st.Dash != null ? $" stroke-dasharray=\"{string.Join(" ", st.Dash.Select(d => N(d)))}\"" : "");
                if (s.Closed) sb.AppendLine($"  <polygon points=\"{pts}\" fill=\"{st.Fill ?? "none"}\" {stroke} stroke-linejoin=\"round\"/>");
                else sb.AppendLine($"  <polyline points=\"{pts}\" fill=\"none\" {stroke} stroke-linecap=\"round\"/>");
            }
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        static string Esc(string s) => System.Security.SecurityElement.Escape(s ?? "");
    }
}
