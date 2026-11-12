// Paints drawing shapes (InventorSizer.Core.Shape) with GDI+ in the window's monochrome theme.
// The SVG export keeps the engineering colours from Core\Drawing.cs; only the window view is
// restyled here. On top of the drawing it paints a dot grid, grey glass cards behind the bill
// of materials tables, and the notes / issues pop-up.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using InventorSizer.Core;

namespace InventorSizer
{
    // The notes / issues pop-up in the drawing's top right corner.
    public class TwinOverlay
    {
        public List<string> Lines = new List<string>();
        public bool Error, Collapsed;
        public Rectangle Hit;   // set by the painter: click here to collapse / expand
    }

    public static class Painter
    {
        static readonly Dictionary<string, (Pen Pen, Brush Fill, Font Font, Brush Text)> Cache = new Dictionary<string, (Pen, Brush, Font, Brush)>();
        static readonly StringFormat Center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        static readonly StringFormat Left = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

        // Monochrome versions of the Core styles. Meaning that was carried by colour is carried
        // by weight, dash and fill here: problems are solid black, hardware is heavy, hidden is dashed.
        static Style L(string stroke, double w, float[] dash = null, string fill = null) => new Style { Stroke = stroke, Width = w, Dash = dash, Fill = fill };
        static Style T(string color, double size, bool bold = false) => new Style { TextColor = color, FontSize = size, Bold = bold };
        static readonly float[] Dash = { 6, 4 }, Small = { 3, 2 }, CenterDash = { 8, 3, 1.5f, 3 };
        static readonly Dictionary<string, Style> Mono = new Dictionary<string, Style>
        {
            ["face"] = L("#1c1c1c", 1.3, null, "#ffffff"), ["edge"] = L("#1c1c1c", 1.1), ["inner"] = L("#9a9a9a", 1, Dash), ["hidden"] = L("#a6a6a6", 1, Dash),
            ["hole"] = L("#1c1c1c", 1.1), ["holeBad"] = L("#000000", 2.2, null, "#141414"), ["hiddenHole"] = L("#b4b4b4", 1, Small), ["center"] = L("#c4c4c4", 0.8, CenterDash),
            ["dim"] = L("#6e6e6e", 1), ["ext"] = L("#d6d6d6", 1), ["arrow"] = new Style { Fill = "#6e6e6e" }, ["table"] = L("#d4d4d4", 1), ["frame"] = L("#d0d0d0", 1.2),
            ["axisX"] = L("#141414", 1.8, null, "#141414"), ["axisY"] = L("#5c5c5c", 1.8, null, "#5c5c5c"), ["axisZ"] = L("#9c9c9c", 1.8, null, "#9c9c9c"), ["axisRing"] = L("#bdbdbd", 1),
            ["hw"] = L("#2e2e2e", 2.0), ["hwHidden"] = L("#6a6a6a", 1.4, new float[] { 2.5f, 1.5f }), ["hwBad"] = L("#000000", 2.2, null, "#141414"),
            ["profile"] = L("#262626", 2.6), ["corner"] = L("#1c1c1c", 1, null, "#5a5a5a"), ["joint"] = L("#1c1c1c", 1, null, "#e2e2e2"),
            ["divider"] = L("#7a7a7a", 2), ["dividerMilled"] = L("#3a3a3a", 2, new float[] { 5, 3 }), ["dividerMoved"] = L("#000000", 3),
            ["label"] = T("#141414", 13, true), ["sub"] = T("#7a7a7a", 11), ["caption"] = T("#3a3a3a", 15), ["bom"] = T("#2a2a2a", 12),
            ["bomHead"] = T("#4a4a4a", 11.5, true), ["bomTitle"] = T("#141414", 14), ["axisLabel"] = T("#3a3a3a", 11, true), ["note"] = T("#6e6e6e", 11.5), ["empty"] = T("#9a9a9a", 16),
        };
        static readonly HashSet<string> SerifKinds = new HashSet<string> { "caption", "bomTitle", "empty" };

        static Color C(string hex) => ColorTranslator.FromHtml(hex);

        static (Pen Pen, Brush Fill, Font Font, Brush Text) Get(string kind)
        {
            if (Cache.TryGetValue(kind, out var g)) return g;
            if (!Mono.TryGetValue(kind, out var st)) Styles.All.TryGetValue(kind, out st);
            st = st ?? new Style { Stroke = "#000000" };
            Pen pen = null; Brush fill = null, text = null; Font font = null;
            if (st.Stroke != null) { pen = new Pen(C(st.Stroke), (float)st.Width) { LineJoin = LineJoin.Round }; if (st.Dash != null) pen.DashPattern = Array.ConvertAll(st.Dash, d => Math.Max(0.1f, d / (float)Math.Max(st.Width, 1))); }
            if (st.Fill != null) fill = new SolidBrush(C(st.Fill));
            if (st.TextColor != null)
            {
                text = new SolidBrush(C(st.TextColor));
                font = new Font(SerifKinds.Contains(kind) ? Theme.SerifName : Theme.SansName, (float)(st.FontSize * 0.95), st.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            }
            g = (pen, fill, font, text);
            Cache[kind] = g;
            return g;
        }

        // "ISOMETRIC" -> "Isometric"; short codes like GR, FP stay as they are.
        static string Quiet(string s) =>
            string.Join(" ", s.Split(' ').Select(w => w.Length > 2 && w.All(char.IsUpper) ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w.ToLowerInvariant()) : w));

        public static void Paint(Graphics g, List<Shape> shapes) => Paint(g, shapes, Size.Empty, null);

        public static void Paint(Graphics g, List<Shape> shapes, Size size, TwinOverlay overlay)
        {
            Theme.Smooth(g);
            if (size.Width > 0) Dots(g, size);
            if (shapes == null) return;
            foreach (var t in Tables(shapes)) GlassTable(g, t);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            foreach (var s in shapes)
            {
                var st = Get(s.Kind);
                if (s.IsText)
                {
                    if (st.Font == null) continue;
                    var saved = g.Save();
                    g.TranslateTransform((float)s.X, (float)s.Y);
                    if (s.Angle != 0) g.RotateTransform((float)s.Angle);
                    string text = s.Kind == "caption" ? Quiet(s.Text) : s.Text;
                    g.DrawString(text, st.Font, st.Text, 0f, (float)s.Dy, s.Align == "left" ? Left : Center);
                    g.Restore(saved);
                    continue;
                }
                int n = s.Points.Length / 2;
                if (n < 2) continue;
                if (s.Kind == "table" && s.Closed) continue;   // the glass card is the table's outline
                var pts = new PointF[n];
                for (int k = 0; k < n; k++) pts[k] = new PointF((float)s.Points[2 * k], (float)s.Points[2 * k + 1]);
                if (s.Closed)
                {
                    if (st.Fill != null) g.FillPolygon(st.Fill, pts);
                    if (st.Pen != null) g.DrawPolygon(st.Pen, pts);
                }
                else if (st.Pen != null) g.DrawLines(st.Pen, pts);
            }
            if (overlay != null && size.Width > 0) Notes(g, size, overlay);
        }

        // ------------------------------------------------------------ dot grid
        static TextureBrush dots;
        static void Dots(Graphics g, Size size)
        {
            if (dots == null)
            {
                var bmp = new Bitmap(22, 22);
                using (var bg = Graphics.FromImage(bmp))
                {
                    bg.Clear(Theme.Paper);
                    using (var b = new SolidBrush(Color.FromArgb(0xE3, 0xE3, 0xE3))) bg.FillRectangle(b, 10, 10, 2, 2);
                }
                dots = new TextureBrush(bmp);
            }
            g.FillRectangle(dots, 0, 0, size.Width, size.Height);
        }

        // ------------------------------------------------------------ BOM tables on glass
        class TableBox { public RectangleF Card, Head; }

        static List<TableBox> Tables(List<Shape> shapes)
        {
            var o = new List<TableBox>();
            foreach (var r in shapes.Where(s => s.Kind == "table" && s.Closed && s.Points.Length == 8))
            {
                float x1 = (float)r.Points[0], y1 = (float)r.Points[1], x2 = (float)r.Points[4], y2 = (float)r.Points[5];
                // first horizontal line inside = the bottom of the header row
                var rows = shapes.Where(s => s.Kind == "table" && !s.Closed && s.Points.Length == 4 && Math.Abs(s.Points[1] - s.Points[3]) < 0.01
                                             && s.Points[1] > y1 + 1 && s.Points[1] < y2 + 0.5 && Math.Abs(s.Points[0] - x1) < 0.5).Select(s => (float)s.Points[1]).ToList();
                float headBottom = rows.Count > 0 ? rows.Min() : y1 + 19;
                bool note = shapes.Any(s => s.IsText && s.Kind == "note" && Math.Abs(s.X - x1) < 0.5 && s.Y > y2 && s.Y < y2 + 20);
                o.Add(new TableBox { Card = RectangleF.FromLTRB(x1 - 12, y1 - 32, x2 + 12, y2 + (note ? 24 : 12)), Head = RectangleF.FromLTRB(x1, y1, x2, headBottom) });
            }
            return o;
        }

        static void GlassTable(Graphics g, TableBox t)
        {
            var r = Rectangle.Round(t.Card);
            Theme.Shadow(g, r, Theme.Radius, 6, 12);
            using (var path = Theme.Round(t.Card, Theme.Radius))
            using (var b = new SolidBrush(Color.FromArgb(222, 0xEE, 0xEE, 0xEE)))
            using (var p = new Pen(Color.FromArgb(160, 0xD8, 0xD8, 0xD8)))
            { g.FillPath(b, path); g.DrawPath(p, path); }
            using (var path = Theme.Round(t.Head, 5))
            using (var b = new SolidBrush(Color.FromArgb(200, 0xE0, 0xE0, 0xE0))) g.FillPath(b, path);
        }

        // ------------------------------------------------------------ notes / issues pop-up
        static readonly Font NoteFont = Theme.Ui(8.75f), NoteHead = Theme.Ui(9f, FontStyle.Bold);

        static void Notes(Graphics g, Size size, TwinOverlay o)
        {
            o.Hit = Rectangle.Empty;
            if (o.Lines.Count == 0) return;
            string head = o.Error ? (o.Lines.Count == 1 ? "1 issue" : $"{o.Lines.Count} issues") : "Notes";
            int headW = TextRenderer.MeasureText(head, NoteHead).Width;
            Rectangle r;
            int textW = 0;
            if (o.Collapsed)
                r = new Rectangle(size.Width - headW - 76, 12, headW + 64, 34);
            else
            {
                int w = Math.Min(360, Math.Max(240, size.Width * 2 / 5));
                textW = w - 32;
                int h = 44 + o.Lines.Sum(l => TextRenderer.MeasureText(l.Trim(), NoteFont, new Size(textW, 0), TextFormatFlags.WordBreak).Height + 4) + 8;
                r = new Rectangle(size.Width - w - 12, 12, w, Math.Min(h, size.Height - 24));
            }
            o.Hit = new Rectangle(r.X, r.Y, r.Width, 34);
            Theme.Shadow(g, r, Theme.Radius, 7, 22);
            using (var path = Theme.Round(r, Theme.Radius))
            using (var b = new SolidBrush(Color.FromArgb(232, Theme.Glass)))
            using (var p = new Pen(Color.FromArgb(200, Theme.GlassLine)))
            { g.FillPath(b, path); g.DrawPath(p, path); }
            GlassForm.Badge(g, new RectangleF(r.X + 12, r.Y + 8, 18, 18), o.Error ? ToastKind.Error : ToastKind.Info);
            TextRenderer.DrawText(g, head, NoteHead, new Point(r.X + 38, r.Y + 9), Theme.GlassText, TextFormatFlags.NoPrefix);
            // chevron: up = collapse, down = expand
            float cx = r.Right - 18, cy = r.Y + 17;
            using (var p = new Pen(Theme.GlassMuted, 1.5f))
                g.DrawLines(p, o.Collapsed
                    ? new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) }
                    : new[] { new PointF(cx - 4, cy + 2), new PointF(cx, cy - 2), new PointF(cx + 4, cy + 2) });
            if (o.Collapsed) return;
            var saved = g.Save();
            g.SetClip(new Rectangle(r.X, r.Y + 34, r.Width, r.Height - 40));
            int y = r.Y + 40;
            foreach (var line in o.Lines)
            {
                string t = line.Trim();
                bool indent = line.StartsWith(" ");
                int x = r.X + 16 + (indent ? 10 : 0), lw = textW - (indent ? 10 : 0);
                int h = TextRenderer.MeasureText(t, NoteFont, new Size(lw, 0), TextFormatFlags.WordBreak).Height;
                TextRenderer.DrawText(g, t, NoteFont, new Rectangle(x, y, lw, h), indent ? Theme.GlassMuted : Theme.GlassText, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                y += h + 4;
            }
            g.Restore(saved);
        }
    }
}
