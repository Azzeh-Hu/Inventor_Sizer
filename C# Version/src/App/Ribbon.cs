// The Inventor-like frame: the ribbon at the top (title bar, tabs, groups of big and small
// buttons), the type browser on the left (like Inventor's model browser) and the vector icons
// both use. Plain WinForms controls with their own painting, like the rest of App\.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using InventorSizer.Core;

namespace InventorSizer
{
    // ------------------------------------------------------------------ ribbon
    public class Ribbon : Panel
    {
        public const int TitleH = 40, TabH = 30, BodyH = 104, ContentH = 76;
        public readonly Panel TitleBar;
        public readonly FlowLayoutPanel TitleLeft, TitleRight;
        readonly RibbonStrip strip;
        readonly Panel body;
        readonly List<RibbonTab> tabs = new List<RibbonTab>();
        public RibbonTab Active { get; private set; }

        public Ribbon()
        {
            Dock = DockStyle.Top; Height = TitleH + TabH + BodyH + 1; BackColor = Theme.RibbonBody;
            TitleBar = new Panel { Dock = DockStyle.Top, Height = TitleH, BackColor = Theme.TitleBar, Padding = new Padding(12, 0, 12, 0) };
            TitleLeft = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.TitleBar, Dock = DockStyle.Left, Padding = new Padding(0), Margin = new Padding(0) };
            TitleRight = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.TitleBar, Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0), Margin = new Padding(0) };
            TitleBar.Controls.Add(TitleLeft); TitleBar.Controls.Add(TitleRight);
            strip = new RibbonStrip(this) { Dock = DockStyle.Top, Height = TabH };
            body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.RibbonBody, Padding = new Padding(6, 4, 6, 1) };
            body.Paint += (s, e) => { using (var p = new Pen(Theme.RibbonLine)) e.Graphics.DrawLine(p, 0, body.Height - 1, body.Width, body.Height - 1); };
            Controls.Add(body); Controls.Add(strip); Controls.Add(TitleBar);
        }

        internal IEnumerable<RibbonTab> ShownTabs => tabs.Where(t => t.Shown);

        public RibbonTab AddTab(string name)
        {
            var t = new RibbonTab(name);
            t.Groups.Visible = false;
            body.Controls.Add(t.Groups);
            tabs.Add(t);
            if (Active == null) Select(t);
            strip.Invalidate();
            return t;
        }

        public void Select(RibbonTab t)
        {
            Active = t;
            foreach (var k in tabs) k.Groups.Visible = k == t;
            strip.Invalidate();
        }

        public void ShowTab(RibbonTab t, bool on)
        {
            t.Shown = on;
            if (!on && Active == t) { var first = ShownTabs.FirstOrDefault(); if (first != null) Select(first); }
            strip.Invalidate();
        }

        // A column of small controls (rows of the group), centred in the group's height.
        public static FlowLayoutPanel Stack(params Control[] cs)
        {
            foreach (var c in cs) Fix(c);
            int w = cs.Max(c => c.Width + c.Margin.Horizontal), h = cs.Sum(c => c.Height + c.Margin.Vertical);
            var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.RibbonBody, Size = new Size(w, h), Padding = new Padding(0), Margin = new Padding(4, Math.Max(0, (ContentH - h) / 2), 4, 0) };
            p.Controls.AddRange(cs);
            return p;
        }

        // A row of small controls side by side.
        public static FlowLayoutPanel Line(params Control[] cs)
        {
            foreach (var c in cs) Fix(c);
            int w = cs.Sum(c => c.Width + c.Margin.Horizontal), h = cs.Max(c => c.Height + c.Margin.Vertical);
            var p = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Theme.RibbonBody, Size = new Size(w, h), Padding = new Padding(0), Margin = new Padding(0, 0, 0, 2) };
            p.Controls.AddRange(cs);
            return p;
        }

        // Labels are sized by hand: AutoSize flow content is measured late on some runtimes.
        static void Fix(Control c)
        {
            if (c is Label l && l.AutoSize) { var sz = l.PreferredSize; l.AutoSize = false; l.Size = sz; }
        }
    }

    public class RibbonTab
    {
        public readonly string Name;
        public bool Shown = true;
        public readonly FlowLayoutPanel Groups;
        internal Rectangle Hit;

        public RibbonTab(string name)
        {
            Name = name;
            Groups = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Theme.RibbonBody, Padding = new Padding(0), Margin = new Padding(0) };
        }

        public RibbonGroup AddGroup(string caption, params Control[] content)
        {
            var g = new RibbonGroup(caption);
            Groups.Controls.Add(g);
            if (content.Length > 0) g.Add(content);
            return g;
        }
    }

    // The tab names under the title bar; the active tab joins the ribbon body.
    class RibbonStrip : Control
    {
        readonly Ribbon ribbon;
        RibbonTab hover;

        public RibbonStrip(Ribbon r)
        {
            ribbon = r;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.Body; Cursor = Cursors.Hand;
        }

        RibbonTab Hit(Point p) => ribbon.ShownTabs.FirstOrDefault(t => t.Hit.Contains(p));
        protected override void OnMouseMove(MouseEventArgs e) { var h = Hit(e.Location); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = null; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { var h = Hit(e.Location); if (h != null) ribbon.Select(h); base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.RibbonTabs);
            int x = 10, bottom = Height - 1;
            RibbonTab active = null;
            foreach (var t in ribbon.ShownTabs)
            {
                bool on = t == ribbon.Active;
                var font = on ? Theme.BodyBold : Theme.Body;
                int w = TextRenderer.MeasureText(t.Name, font).Width + 26;
                t.Hit = new Rectangle(x, 4, w, Height - 4);
                if (on)
                {
                    active = t;
                    Theme.Smooth(g);
                    var r = new RectangleF(x, 4, w, Height);
                    using (var path = TopRound(r, 5))
                    using (var b = new SolidBrush(Theme.RibbonBody))
                    using (var p = new Pen(Theme.RibbonLine))
                    { g.FillPath(b, path); g.DrawPath(p, path); }
                    using (var p = new Pen(Theme.Accent, 2)) g.DrawLine(p, x + 6, 5, x + w - 6, 5);
                }
                TextRenderer.DrawText(g, t.Name, font, t.Hit, on ? Theme.Ink : t == hover ? Theme.Ink : Theme.InkSoft, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                x += w + 2;
            }
            g.SmoothingMode = SmoothingMode.None;
            using (var p = new Pen(Theme.RibbonLine))
            {
                if (active == null) g.DrawLine(p, 0, bottom, Width, bottom);
                else { g.DrawLine(p, 0, bottom, active.Hit.X, bottom); g.DrawLine(p, active.Hit.Right, bottom, Width, bottom); }
            }
        }

        static GraphicsPath TopRound(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddLine(r.X, r.Bottom, r.X, r.Y + rad);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddLine(r.Right, r.Y + rad, r.Right, r.Bottom);
            return p;
        }
    }

    // One group of a tab: its controls, the caption under them and a separator on the right.
    public class RibbonGroup : Panel
    {
        public readonly string Caption;
        public readonly FlowLayoutPanel Content;
        const int CapH = 20;

        public RibbonGroup(string caption)
        {
            Caption = caption;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.RibbonBody; Margin = new Padding(0); Padding = new Padding(6, 2, 7, CapH);
            Height = Ribbon.BodyH - 6;
            Content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Theme.RibbonBody, Padding = new Padding(0), Margin = new Padding(0) };
            Controls.Add(Content);
        }

        public RibbonGroup Add(params Control[] cs) { Content.Controls.AddRange(cs); Fit(); return this; }

        public void Fit()
        {
            int w = Content.Controls.Cast<Control>().Sum(c => c.Width + c.Margin.Horizontal);
            int cap = TextRenderer.MeasureText(Caption, Theme.Small).Width + 12;
            Width = Math.Max(w, cap) + Padding.Horizontal;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            TextRenderer.DrawText(g, Caption, Theme.Small, new Rectangle(0, Height - CapH, Width - 1, CapH - 2), Theme.RibbonCaption, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            using (var p = new Pen(Theme.RibbonLine)) g.DrawLine(p, Width - 1, 6, Width - 1, Height - 4);
        }
    }

    // ------------------------------------------------------------------ ribbon button
    // Large: a 32 px icon over one or two lines of text. Small: a 16 px icon left of the text.
    // With menu items it opens a glass menu under itself (like the old MenuButton).
    public class RibbonButton : Button
    {
        public string Icon;
        public readonly bool Large;
        public ContextMenuStrip Menu;
        bool hover, down;

        public RibbonButton(string text, string icon, bool large = true)
        {
            Text = text; Icon = icon; Large = large;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
            Font = large ? Theme.Ui(8.75f) : Theme.Body; Cursor = Cursors.Hand; Margin = new Padding(1, 0, 1, 0);
            Measure();
        }

        string Label => Text + (Menu != null ? " ▾" : "");

        void Measure()
        {
            if (Large)
            {
                int w = Label.Split('\n').Max(l => TextRenderer.MeasureText(l, Font).Width);
                Size = new Size(Math.Max(58, w + 14), Ribbon.ContentH);
            }
            else Size = new Size(TextRenderer.MeasureText(Label, Font).Width + (Icon != null ? 34 : 18), 26);
        }

        public ToolStripMenuItem Add(string text, EventHandler click)
        {
            if (Menu == null) { Menu = Glass.Menu(); Measure(); }
            var it = new ToolStripMenuItem(text) { Padding = new Padding(4, 6, 4, 6) };
            it.Click += click;
            Menu.Items.Add(it);
            return it;
        }

        public void Separator() => Menu?.Items.Add(new ToolStripSeparator());

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Menu != null) Menu.Show(this, new Point(0, Height + 2));
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Measure(); Invalidate(); }
        protected override bool ShowFocusCues => false;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.RibbonBody);
            Theme.Smooth(g);
            if (Enabled && (hover || down))
                using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 4))
                using (var b = new SolidBrush(down ? Theme.AccentLine : Theme.AccentSoft))
                using (var p = new Pen(Theme.AccentLine))
                { g.FillPath(b, path); g.DrawPath(p, path); }
            var ink = Enabled ? Theme.Ink : Theme.Faint;
            if (Large)
            {
                if (Icon != null) Icons.Draw(g, Icon, new RectangleF((Width - 32) / 2f, 6, 32, 32), Enabled);
                TextRenderer.DrawText(g, Label, Font, new Rectangle(2, 42, Width - 4, Height - 44), ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
            else
            {
                int x = 8;
                if (Icon != null) { Icons.Draw(g, Icon, new RectangleF(6, (Height - 16) / 2f, 16, 16), Enabled); x = 28; }
                TextRenderer.DrawText(g, Label, Font, new Rectangle(x, 0, Width - x - 4, Height), ink, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // ------------------------------------------------------------------ icons
    // Small vector icons drawn in a 32 x 32 box, scaled to the rectangle given.
    public static class Icons
    {
        static Color C(string hex) => ColorTranslator.FromHtml(hex);
        static readonly Color Folder = C("#E8B547"), FolderDark = C("#B9862A"), Blue = C("#3D7CC9"), BlueLight = C("#8DB6E6"), BlueDark = C("#24548F"),
            Green = C("#2F9E64"), Paper = C("#FFFFFF"), Line = C("#4A4F57"), Orange = C("#F08C1E");

        public static void Draw(Graphics g, string name, RectangleF r, bool enabled = true)
        {
            var saved = g.Save();
            Theme.Smooth(g);
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 32f, r.Height / 32f);
            Color Tone(Color c) => enabled ? c : Theme.Mix(c, Color.White, 0.6);
            using (var line = new Pen(Tone(Line), 1.6f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                switch (name)
                {
                    case "open": case "folder": FolderIcon(g, line, Tone); if (name == "open") Cube(g, Tone, 17, 19, 7); break;
                    case "master": Cube(g, Tone, 16, 17, 12); break;
                    case "subNone": CubeOutline(g, Tone, 16, 17, 12); break;
                    case "type": FolderIcon(g, line, Tone); break;
                    case "fx":
                        using (var path = Theme.Round(new RectangleF(3, 5, 26, 22), 4))
                        using (var b = new SolidBrush(Tone(Paper))) { g.FillPath(b, path); g.DrawPath(line, path); }
                        using (var f = new Font(Theme.SerifName, 13f, FontStyle.Italic | FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var b = new SolidBrush(Tone(BlueDark))) g.DrawString("fx", f, b, new RectangleF(3, 5, 26, 22), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                        break;
                    case "apply":
                        Cube(g, Tone, 14, 16, 11);
                        Badge(g, Tone(Green), 23, 23);
                        using (var p = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                            g.DrawLines(p, new[] { new PointF(19.5f, 23), new PointF(22, 25.5f), new PointF(26.5f, 20.5f) });
                        break;
                    case "read":
                        Cube(g, Tone, 16, 19, 10);
                        using (var p = new Pen(Tone(Green), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        { g.DrawLine(p, 16, 2, 16, 11); g.DrawLines(p, new[] { new PointF(12, 7.5f), new PointF(16, 11.5f), new PointF(20, 7.5f) }); }
                        break;
                    case "svg": case "files":
                        var page = new[] { new PointF(7, 3), new PointF(20, 3), new PointF(26, 9), new PointF(26, 29), new PointF(7, 29) };
                        using (var b = new SolidBrush(Tone(Paper))) g.FillPolygon(b, page);
                        g.DrawPolygon(line, page);
                        g.DrawLines(line, new[] { new PointF(20, 3), new PointF(20, 9), new PointF(26, 9) });
                        if (name == "svg")
                        {
                            using (var b = new SolidBrush(Tone(Orange))) g.FillRectangle(b, 4, 16, 20, 9);
                            using (var f = new Font(Theme.SansName, 7f, FontStyle.Bold, GraphicsUnit.Pixel))
                            using (var b = new SolidBrush(Color.White)) g.DrawString("SVG", f, b, new RectangleF(4, 16, 20, 9), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                        }
                        else for (int i = 0; i < 4; i++) g.DrawLine(line, 11, 13 + i * 4, 22, 13 + i * 4);
                        break;
                    case "gr":
                        using (var p = new Pen(Tone(BlueDark), 2.6f)) { g.DrawRectangle(p, 4, 7, 24, 19); g.DrawLine(p, 16, 7, 16, 26); }
                        using (var b = new SolidBrush(Tone(BlueLight))) { g.FillRectangle(b, 2, 5, 5, 5); g.FillRectangle(b, 25, 5, 5, 5); g.FillRectangle(b, 2, 23, 5, 5); g.FillRectangle(b, 25, 23, 5, 5); }
                        break;
                    case "activity":
                        for (int i = 0; i < 4; i++) g.DrawLine(line, 6, 8 + i * 6, 26 - (i % 2) * 7, 8 + i * 6);
                        break;
                }
            }
            g.Restore(saved);
        }

        static void FolderIcon(Graphics g, Pen line, Func<Color, Color> tone)
        {
            var back = new[] { new PointF(3, 8), new PointF(12, 8), new PointF(15, 11), new PointF(29, 11), new PointF(29, 27), new PointF(3, 27) };
            using (var b = new SolidBrush(tone(FolderDark))) g.FillPolygon(b, back);
            var front = new[] { new PointF(3, 14), new PointF(29, 14), new PointF(29, 27), new PointF(3, 27) };
            using (var b = new SolidBrush(tone(Folder))) g.FillPolygon(b, front);
            using (var p = new Pen(tone(FolderDark), 1.2f) { LineJoin = LineJoin.Round }) g.DrawPolygon(p, back);
        }

        // An isometric box, centre (cx, cy), half size s: the panel the app sizes.
        static void Cube(Graphics g, Func<Color, Color> tone, float cx, float cy, float s)
        {
            var top = new[] { new PointF(cx, cy - s), new PointF(cx + s * 0.87f, cy - s * 0.5f), new PointF(cx, cy), new PointF(cx - s * 0.87f, cy - s * 0.5f) };
            var left = new[] { new PointF(cx - s * 0.87f, cy - s * 0.5f), new PointF(cx, cy), new PointF(cx, cy + s), new PointF(cx - s * 0.87f, cy + s * 0.5f) };
            var right = new[] { new PointF(cx, cy), new PointF(cx + s * 0.87f, cy - s * 0.5f), new PointF(cx + s * 0.87f, cy + s * 0.5f), new PointF(cx, cy + s) };
            using (var b = new SolidBrush(tone(BlueLight))) g.FillPolygon(b, top);
            using (var b = new SolidBrush(tone(Blue))) g.FillPolygon(b, left);
            using (var b = new SolidBrush(tone(BlueDark))) g.FillPolygon(b, right);
            using (var p = new Pen(tone(BlueDark), 1f) { LineJoin = LineJoin.Round }) { g.DrawPolygon(p, top); g.DrawPolygon(p, left); g.DrawPolygon(p, right); }
        }

        static void CubeOutline(Graphics g, Func<Color, Color> tone, float cx, float cy, float s)
        {
            var outline = new[] { new PointF(cx, cy - s), new PointF(cx + s * 0.87f, cy - s * 0.5f), new PointF(cx + s * 0.87f, cy + s * 0.5f), new PointF(cx, cy + s), new PointF(cx - s * 0.87f, cy + s * 0.5f), new PointF(cx - s * 0.87f, cy - s * 0.5f) };
            using (var p = new Pen(tone(Theme.Faint), 1.6f) { DashPattern = new[] { 2f, 1.5f }, LineJoin = LineJoin.Round })
            {
                g.DrawPolygon(p, outline);
                g.DrawLines(p, new[] { new PointF(cx - s * 0.87f, cy - s * 0.5f), new PointF(cx, cy), new PointF(cx + s * 0.87f, cy - s * 0.5f) });
                g.DrawLine(p, cx, cy, cx, cy + s);
            }
        }

        static void Badge(Graphics g, Color fill, float cx, float cy)
        {
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, cx - 7, cy - 7, 14, 14);
            using (var p = new Pen(Color.White, 1.5f)) g.DrawEllipse(p, cx - 7, cy - 7, 14, 14);
        }

        public static Bitmap Bitmap(string name, int size = 16)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Transparent); Draw(g, name, new RectangleF(0, 0, size, size)); }
            return bmp;
        }
    }

    // ------------------------------------------------------------------ type browser
    // Like Inventor's model browser: type (FP / TP / RP, from standards\*.json) -> sub type
    // (1T / 2T), laid out like the masters folder. A type node means "Auto (width)": the sub
    // type then follows the split width; a sub type node forces that master.
    public class TypeBrowser : Panel
    {
        public readonly TreeView Tree;
        public event Action<Standard, string> Picked;   // sub type null = Auto (width)
        public event Action<Standard, string> Opened;   // double click on a sub type
        bool quiet;

        sealed class SubRef { public Standard Std; public string Sub; }

        public TypeBrowser()
        {
            BackColor = Theme.Paper;
            var head = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Theme.RibbonTabs };
            head.Paint += (s, e) =>
            {
                var g = e.Graphics;
                TextRenderer.DrawText(g, "Types", Theme.BodyBold, new Point(12, 9), Theme.Ink, TextFormatFlags.NoPrefix);
                int w = TextRenderer.MeasureText("Types", Theme.BodyBold).Width;
                TextRenderer.DrawText(g, "masters \\ type \\ sub type", Theme.Small, new Point(12 + w + 6, 10), Theme.Muted, TextFormatFlags.NoPrefix);
                using (var p = new Pen(Theme.RibbonLine)) g.DrawLine(p, 0, head.Height - 1, head.Width, head.Height - 1);
            };
            var icons = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
            foreach (var k in new[] { "type", "master", "subNone" }) icons.Images.Add(k, Icons.Bitmap(k));
            Tree = new TreeView
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = Theme.Body, ItemHeight = 26, Indent = 20,
                ShowLines = true, ShowPlusMinus = true, ShowRootLines = true, HideSelection = false, ShowNodeToolTips = true,
                DrawMode = TreeViewDrawMode.OwnerDrawText, ImageList = icons, BackColor = Theme.Paper, ForeColor = Theme.Ink, LineColor = Theme.LineStrong
            };
            var well = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 6, 0, 0), BackColor = Theme.Paper };
            well.Controls.Add(Tree);
            var hint = new Label
            {
                Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Paper, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(10, 6, 8, 6),
                Text = "Type = sub type by width (Auto). 1T / 2T = use that master; double-click to open it in Inventor."
            };
            Controls.Add(well); Controls.Add(hint); Controls.Add(head);
            Paint += (s, e) => { using (var p = new Pen(Theme.RibbonLine)) e.Graphics.DrawLine(p, Width - 1, 0, Width - 1, Height); };
            Padding = new Padding(0, 0, 1, 0);

            Tree.DrawNode += DrawNode;
            Tree.AfterSelect += (s, e) =>
            {
                if (quiet || e.Node == null) return;
                if (e.Node.Tag is SubRef r) Picked?.Invoke(r.Std, r.Sub);
                else if (e.Node.Tag is Standard st) Picked?.Invoke(st, null);
            };
            Tree.NodeMouseDoubleClick += (s, e) => { if (e.Node?.Tag is SubRef r) Opened?.Invoke(r.Std, r.Sub); };
        }

        public void Build(List<Standard> standards)
        {
            quiet = true;
            Tree.BeginUpdate();
            try
            {
                Tree.Nodes.Clear();
                foreach (var s in standards)
                {
                    var n = new TreeNode(s.Name) { Tag = s, ImageKey = "type", SelectedImageKey = "type", ToolTipText = $"{s.Name}  [{s.Prefix}...]  -  {s.File}" };
                    foreach (var sub in Standards.SubTypes) n.Nodes.Add(new TreeNode(sub) { Tag = new SubRef { Std = s, Sub = sub } });
                    Tree.Nodes.Add(n);
                }
                Tree.ExpandAll();
            }
            finally { Tree.EndUpdate(); quiet = false; }
        }

        // Master status on every sub type; the sub type in use is bold.
        public void Refresh(Standard current, string effectiveSub)
        {
            foreach (TreeNode tn in Tree.Nodes)
                foreach (TreeNode sn in tn.Nodes)
                {
                    var r = (SubRef)sn.Tag;
                    bool has = r.Std.HasMaster(r.Sub), inUse = r.Std == current && r.Sub == effectiveSub;
                    string text = $"{r.Sub}  ·  {(has ? "master" : "no master")}";
                    string key = has ? "master" : "subNone";
                    string tip = has ? r.Std.MasterFor(r.Sub) : $"No master in masters\\{r.Std.Id}\\{r.Sub}";
                    if (sn.Text != text) sn.Text = text;
                    if (sn.ImageKey != key) { sn.ImageKey = key; sn.SelectedImageKey = key; }
                    if (sn.ToolTipText != tip) sn.ToolTipText = tip;
                    var font = inUse ? Theme.BodyBold : null;
                    if (sn.NodeFont != font) sn.NodeFont = font;
                }
        }

        public void Select(Standard s, string sub)
        {
            foreach (TreeNode tn in Tree.Nodes)
            {
                if (!ReferenceEquals(tn.Tag, s)) continue;
                var n = sub == null ? tn : tn.Nodes.Cast<TreeNode>().FirstOrDefault(k => ((SubRef)k.Tag).Sub == sub) ?? tn;
                if (Tree.SelectedNode != n) { quiet = true; try { Tree.SelectedNode = n; } finally { quiet = false; } }
                return;
            }
        }

        void DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            if (e.Bounds.IsEmpty || e.Bounds.Width == 0) return;
            var g = e.Graphics;
            bool sel = (e.State & TreeNodeStates.Selected) != 0;
            var font = e.Node.NodeFont ?? Tree.Font;
            var r = new Rectangle(e.Bounds.X, e.Bounds.Y + 1, e.Bounds.Width + 4, e.Bounds.Height - 2);
            using (var b = new SolidBrush(sel ? Theme.BrowserSel : Tree.BackColor)) g.FillRectangle(b, r);
            const TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            string text = e.Node.Text;
            int dot = text.IndexOf("  ·  ", StringComparison.Ordinal);
            var tr = new Rectangle(r.X + 2, r.Y, r.Width + 40, r.Height);
            if (dot < 0) { TextRenderer.DrawText(g, text, font, tr, Theme.Ink, f); return; }
            string head = text.Substring(0, dot), tail = text.Substring(dot);
            TextRenderer.DrawText(g, head, font, tr, Theme.Ink, f);
            int w = TextRenderer.MeasureText(g, head, font, tr.Size, f).Width;
            TextRenderer.DrawText(g, tail, Theme.Small, new Rectangle(tr.X + w, tr.Y, tr.Width - w, tr.Height), Theme.Muted, f);
        }
    }
}
