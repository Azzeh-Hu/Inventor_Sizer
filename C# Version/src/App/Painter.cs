// Paints drawing shapes (InventorSizer.Core.Shape) with GDI+, using the shared styles.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using InventorSizer.Core;

namespace InventorSizer
{
    public static class Painter
    {
        static readonly Dictionary<string, (Pen Pen, Brush Fill, Font Font, Brush Text)> Cache = new Dictionary<string, (Pen, Brush, Font, Brush)>();
        static readonly StringFormat Center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        static readonly StringFormat Left = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

        static Color C(string hex) => ColorTranslator.FromHtml(hex);

        static (Pen Pen, Brush Fill, Font Font, Brush Text) Get(string kind)
        {
            if (Cache.TryGetValue(kind, out var g)) return g;
            Styles.All.TryGetValue(kind, out var st);
            st = st ?? new Style { Stroke = "#000000" };
            Pen pen = null; Brush fill = null, text = null; Font font = null;
            if (st.Stroke != null) { pen = new Pen(C(st.Stroke), (float)st.Width); if (st.Dash != null) pen.DashPattern = Array.ConvertAll(st.Dash, d => Math.Max(0.1f, d / (float)Math.Max(st.Width, 1))); }
            if (st.Fill != null) fill = new SolidBrush(C(st.Fill));
            if (st.TextColor != null) { text = new SolidBrush(C(st.TextColor)); font = new Font("Segoe UI", (float)(st.FontSize * 0.92), st.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
            g = (pen, fill, font, text);
            Cache[kind] = g;
            return g;
        }

        public static void Paint(Graphics g, List<Shape> shapes)
        {
            if (shapes == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            foreach (var s in shapes)
            {
                var st = Get(s.Kind);
                if (s.IsText)
                {
                    if (st.Font == null) continue;
                    var saved = g.Save();
                    g.TranslateTransform((float)s.X, (float)s.Y);
                    if (s.Angle != 0) g.RotateTransform((float)s.Angle);
                    g.DrawString(s.Text, st.Font, st.Text, 0f, (float)s.Dy, s.Align == "left" ? Left : Center);
                    g.Restore(saved);
                    continue;
                }
                int n = s.Points.Length / 2;
                if (n < 2) continue;
                var pts = new PointF[n];
                for (int k = 0; k < n; k++) pts[k] = new PointF((float)s.Points[2 * k], (float)s.Points[2 * k + 1]);
                if (s.Closed)
                {
                    if (st.Fill != null) g.FillPolygon(st.Fill, pts);
                    if (st.Pen != null) g.DrawPolygon(st.Pen, pts);
                }
                else if (st.Pen != null) g.DrawLines(st.Pen, pts);
            }
        }
    }
}
