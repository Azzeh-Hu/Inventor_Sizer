// Themed controls: buttons, switches, inputs, cards, the mode switch and the glass menus.
// They are ordinary WinForms controls with their own painting, so the rest of the window code
// uses them like the built-in ones.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace InventorSizer
{
    public enum ButtonKind { Primary, Secondary, Ghost, GlassPrimary, Glass }

    // ------------------------------------------------------------------ button
    public class FlatButton : Button
    {
        public ButtonKind Kind;
        public string Glyph;          // drawn after the text, e.g. "▾"
        bool hover, down;

        public FlatButton(string text, ButtonKind kind = ButtonKind.Secondary, int width = 0, int height = 32)
        {
            Text = text; Kind = kind;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
            Font = kind == ButtonKind.Primary || kind == ButtonKind.GlassPrimary ? Theme.BodyBold : Theme.Body;
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 0, 8, 0);
            Size = new Size(width > 0 ? width : AutoWidth(), height);
        }

        public int AutoWidth() => TextRenderer.MeasureText(Text + (Glyph != null ? "  " + Glyph : ""), Font).Width + (Kind == ButtonKind.Ghost ? 18 : 30);
        public void FitWidth() => Width = AutoWidth();

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override bool ShowFocusCues => false;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.App);
            Theme.Smooth(g);
            Color fill, border, text;
            bool on = Enabled;
            switch (Kind)
            {
                case ButtonKind.Primary:
                    fill = !on ? Theme.LineStrong : down ? Color.Black : hover ? Theme.InkSoft : Theme.Ink;
                    border = fill; text = on ? Color.White : Theme.App; break;
                case ButtonKind.GlassPrimary:
                    fill = !on ? Theme.GlassLine : down ? Theme.Pressed : hover ? Theme.Hover : Theme.GlassText;
                    border = fill; text = Theme.Ink; break;
                case ButtonKind.Glass:
                    fill = down ? Theme.GlassLine : hover ? Theme.GlassRaised : Theme.Glass;
                    border = Theme.GlassLine; text = on ? Theme.GlassText : Theme.GlassMuted; break;
                case ButtonKind.Ghost:
                    var bg = Parent?.BackColor ?? Theme.App;
                    fill = down ? Theme.Mix(bg, Color.Black, 0.12) : hover ? Theme.Mix(bg, Color.Black, 0.06) : bg;
                    border = fill; text = on ? Theme.Ink : Theme.Faint; break;
                default:
                    fill = !on ? Theme.App : down ? Theme.Pressed : hover ? Theme.Hover : Theme.Paper;
                    border = on ? Theme.LineStrong : Theme.Line; text = on ? Theme.Ink : Theme.Faint; break;
            }
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Theme.RadiusSmall))
            using (var b = new SolidBrush(fill))
            using (var p = new Pen(border))
            { g.FillPath(b, path); g.DrawPath(p, path); }
            string label = Text + (Glyph != null ? "  " + Glyph : "");
            TextRenderer.DrawText(g, label, Font, new Rectangle(0, 0, Width, Height), text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // A button that opens a menu under itself.
    public class MenuButton : FlatButton
    {
        public readonly ContextMenuStrip Menu = Glass.Menu();
        public MenuButton(string text, ButtonKind kind = ButtonKind.Secondary) : base(text, kind)
        {
            Glyph = "▾"; FitWidth();
            Click += (s, e) => Menu.Show(this, new Point(0, Height + 4));
        }
        public ToolStripMenuItem Add(string text, EventHandler click)
        {
            var it = new ToolStripMenuItem(text) { Padding = new Padding(4, 6, 4, 6) };
            it.Click += click;
            Menu.Items.Add(it);
            return it;
        }
        public void Separator() => Menu.Items.Add(new ToolStripSeparator());
    }

    // ------------------------------------------------------------------ switch
    public class Toggle : CheckBox
    {
        public Toggle(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoSize = false; Font = Theme.Body; Cursor = Cursors.Hand; Margin = new Padding(0, 0, 12, 0);
            Size = new Size(TextRenderer.MeasureText(text, Font).Width + 46, 28);
        }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.App);
            Theme.Smooth(g);
            var track = new RectangleF(1, (Height - 18) / 2f, 32, 18);
            using (var path = Theme.Round(track, 9))
            using (var b = new SolidBrush(!Enabled ? Theme.Line : Checked ? Theme.Ink : Theme.LineStrong))
                g.FillPath(b, path);
            float kx = Checked ? track.Right - 16 : track.X + 2;
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, track.Y + 2, 14, 14);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(42, 0, Width - 42, Height), Enabled ? Theme.Ink : Theme.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // ------------------------------------------------------------------ combo box
    public class ThemedCombo : ComboBox
    {
        public ThemedCombo(int width)
        {
            DropDownStyle = ComboBoxStyle.DropDownList; DrawMode = DrawMode.OwnerDrawFixed; FlatStyle = FlatStyle.Flat;
            Font = Theme.Body; ItemHeight = 24; Width = width; BackColor = Theme.Paper; ForeColor = Theme.Ink;
            Margin = new Padding(0, 0, 8, 0); Cursor = Cursors.Hand;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0, sel = (e.State & DrawItemState.Selected) != 0 && !edit;
            using (var b = new SolidBrush(sel ? Theme.Selection : Theme.Paper)) e.Graphics.FillRectangle(b, e.Bounds);
            var r = new Rectangle(e.Bounds.X + (edit ? 4 : 10), e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, r, Enabled ? Theme.Ink : Theme.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        // The system draws the frame and the arrow; paint our own over them.
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0x000F || !IsHandleCreated) return;   // WM_PAINT
            try
            {
                using (var g = Graphics.FromHwnd(Handle))
                {
                    var bg = Parent?.BackColor ?? Theme.App;
                    using (var p = new Pen(bg, 3)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                    Theme.Smooth(g);
                    using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 6))
                    using (var p = new Pen(Focused || DroppedDown ? Theme.Ink : Theme.LineStrong)) g.DrawPath(p, path);
                    var arrow = new Rectangle(Width - 24, 2, 21, Height - 4);
                    using (var b = new SolidBrush(Theme.Paper)) g.FillRectangle(b, arrow);
                    float cx = arrow.X + arrow.Width / 2f - 1, cy = Height / 2f;
                    using (var p = new Pen(Enabled ? Theme.InkSoft : Theme.Faint, 1.5f))
                        g.DrawLines(p, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
                }
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ input field
    // A rounded box that hosts a borderless TextBox or NumericUpDown.
    public class Field : Panel
    {
        public readonly Control Inner;
        public Field(Control inner, int width, int height = 32)
        {
            Inner = inner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(width, height); Margin = new Padding(0, 0, 8, 0); BackColor = Theme.Paper;
            if (inner is TextBox tb) tb.BorderStyle = BorderStyle.None;
            if (inner is NumericUpDown nu) nu.BorderStyle = BorderStyle.None;
            inner.BackColor = Theme.Paper; inner.ForeColor = Theme.Ink;
            if (inner.Font == null || inner.Font.Equals(Control.DefaultFont)) inner.Font = Theme.Body;
            Controls.Add(inner);
            inner.GotFocus += (s, e) => Invalidate(); inner.LostFocus += (s, e) => Invalidate();
            inner.EnabledChanged += (s, e) => { inner.BackColor = BackColor = inner.Enabled ? Theme.Paper : Theme.App; Invalidate(); };
            Cursor = Cursors.IBeam;
            Click += (s, e) => inner.Focus();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Inner == null) return;
            int pad = Inner is NumericUpDown ? 6 : 10;
            Inner.SetBounds(pad, Math.Max(2, (Height - Inner.Height) / 2), Width - pad - (Inner is NumericUpDown ? 3 : pad), Inner.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.App);
            Theme.Smooth(g);
            using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 6))
            using (var b = new SolidBrush(BackColor))
            using (var p = new Pen(Inner != null && Inner.ContainsFocus ? Theme.Ink : Theme.LineStrong))
            { g.FillPath(b, path); g.DrawPath(p, path); }
        }
    }

    // ------------------------------------------------------------------ card
    // A rounded grey panel with a serif title and optional actions on the right of the title.
    public class Card : Panel
    {
        public string Title, Subtitle;
        public readonly FlowLayoutPanel Actions;
        public bool Collapsible, Collapsed;
        public int ExpandedHeight;
        public event Action CollapsedChanged;
        const int Head = 46;

        public Card(string title, Color? fill = null, string subtitle = null)
        {
            Title = title; Subtitle = subtitle;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = fill ?? Theme.Card;
            Padding = new Padding(14, title != null ? Head : 14, 14, 14);
            Actions = new FlowLayoutPanel { AutoSize = false, WrapContents = false, Size = new Size(1, 1), BackColor = BackColor, Anchor = AnchorStyles.Top | AnchorStyles.Right, Padding = new Padding(0), Margin = new Padding(0) };
            Controls.Add(Actions);
            MouseClick += (s, e) => { if (Collapsible && e.Y < Head) SetCollapsed(!Collapsed); };
        }

        public void SetCollapsed(bool c)
        {
            if (!Collapsible) return;
            if (!Collapsed) ExpandedHeight = Height;
            Collapsed = c;
            foreach (Control k in Controls) if (k != Actions) k.Visible = !c;
            Height = c ? Head + 2 : Math.Max(ExpandedHeight, Head + 60);
            Cursor = Cursors.Default;
            Invalidate();
            CollapsedChanged?.Invoke();
        }

        protected override void OnMouseMove(MouseEventArgs e) { Cursor = Collapsible && e.Y < Head ? Cursors.Hand : Cursors.Default; base.OnMouseMove(e); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Actions == null) return;
            // sized by hand: AutoSize flow panels are measured late (or not at all) on some runtimes
            var kids = Actions.Controls.Cast<Control>().ToList();
            Actions.Visible = kids.Count > 0;
            Actions.Size = new Size(Math.Max(1, kids.Sum(c => c.Width + c.Margin.Horizontal)), Math.Max(1, kids.Count == 0 ? 1 : kids.Max(c => c.Height + c.Margin.Vertical)));
            Actions.Location = new Point(Width - Actions.Width - 12 - (Collapsible ? 22 : 0), (Head - Actions.Height) / 2 + 1);
            Actions.BringToFront();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.App);
            Theme.Smooth(g);
            using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.Radius))
            using (var b = new SolidBrush(BackColor))
            using (var p = new Pen(Theme.Line))
            { g.FillPath(b, path); g.DrawPath(p, path); }
            if (Title == null) return;
            int x = 16;
            TextRenderer.DrawText(g, Title, Theme.CardTitle, new Point(x, 13), Theme.Ink, TextFormatFlags.NoPrefix);
            if (Subtitle != null)
            {
                int tw = TextRenderer.MeasureText(Title, Theme.CardTitle).Width;
                TextRenderer.DrawText(g, Subtitle, Theme.Small, new Rectangle(x + tw + 6, 12, Math.Max(0, Width - x - tw - Actions.Width - 40), 24), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            if (Collapsible)
            {
                float cx = Width - 22, cy = Head / 2f;
                using (var p = new Pen(Theme.InkSoft, 1.5f))
                    g.DrawLines(p, Collapsed
                        ? new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) }
                        : new[] { new PointF(cx - 4, cy + 2), new PointF(cx, cy - 2), new PointF(cx + 4, cy + 2) });
            }
        }

        // Soft shadows under every card in a container (the container paints them).
        public static void CastShadows(Control container)
        {
            container.Paint += (s, e) =>
            {
                Theme.Smooth(e.Graphics);
                foreach (Control c in container.Controls)
                    if (c is Card && c.Visible) Theme.Shadow(e.Graphics, c.Bounds, Theme.Radius, 6, 14);
            };
            container.ControlAdded += (s, e) => { e.Control.LocationChanged += (a, b) => container.Invalidate(); e.Control.SizeChanged += (a, b) => container.Invalidate(); e.Control.VisibleChanged += (a, b) => container.Invalidate(); };
        }
    }

    // ------------------------------------------------------------------ mode switch
    public class Segmented : Control
    {
        readonly string[] items;
        public int Selected { get; private set; }
        public event Action<int> Picked;
        int hover = -1;

        public Segmented(params string[] items)
        {
            this.items = items;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Font = Theme.Body; Cursor = Cursors.Hand;
            int w = items.Sum(t => TextRenderer.MeasureText(t, Theme.BodyBold).Width + 32);
            Size = new Size(w + 8, 36);
        }

        public void SetSelected(int i) { Selected = i; Invalidate(); }

        Rectangle Seg(int i)
        {
            int x = 4;
            for (int k = 0; k < i; k++) x += TextRenderer.MeasureText(items[k], Theme.BodyBold).Width + 32;
            return new Rectangle(x, 4, TextRenderer.MeasureText(items[i], Theme.BodyBold).Width + 32, Height - 8);
        }

        int Hit(Point p) { for (int i = 0; i < items.Length; i++) if (Seg(i).Contains(p)) return i; return -1; }
        protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Location); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { int h = Hit(e.Location); if (h >= 0 && h != Selected) Picked?.Invoke(h); base.OnMouseClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Paper);
            Theme.Smooth(g);
            using (var path = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), 10))
            using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, path);
            for (int i = 0; i < items.Length; i++)
            {
                var r = Seg(i);
                if (i == Selected)
                {
                    Theme.Shadow(g, r, 7, 3, 18);
                    using (var path = Theme.Round(r, 7))
                    using (var b = new SolidBrush(Theme.Paper)) g.FillPath(b, path);
                }
                TextRenderer.DrawText(g, items[i], i == Selected ? Theme.BodyBold : Theme.Body, r,
                    i == Selected ? Theme.Ink : i == hover ? Theme.InkSoft : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    // ------------------------------------------------------------------ small label
    public class Caption : Label
    {
        public Caption(string text, bool strong = false)
        {
            Text = text; AutoSize = true; Font = strong ? Theme.BodyBold : Theme.Small; ForeColor = strong ? Theme.Ink : Theme.Muted;
            Margin = new Padding(0, 9, 6, 0); BackColor = Color.Transparent;
        }
    }

    // ------------------------------------------------------------------ tables, menus
    public static class Glass
    {
        // A data grid without the spreadsheet chrome: no cell borders, a quiet header row,
        // white value cells on the grey card.
        public static void StyleGrid(DataGridView g, Color card)
        {
            g.BorderStyle = BorderStyle.None;
            g.BackgroundColor = card;
            g.EnableHeadersVisualStyles = false;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.GridColor = Theme.Line;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = 32;
            g.RowTemplate.Height = 30;
            g.AllowUserToResizeRows = false;
            g.Font = Theme.Body;
            g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.CardHead, ForeColor = Theme.InkSoft, Font = Theme.Ui(8.75f, FontStyle.Bold),
                SelectionBackColor = Theme.CardHead, SelectionForeColor = Theme.InkSoft, Padding = new Padding(6, 0, 0, 0), Alignment = DataGridViewContentAlignment.MiddleLeft, WrapMode = DataGridViewTriState.False
            };
            g.ScrollBars = ScrollBars.Vertical;
            // the order of the rows means something here: no sorting by clicking a header
            g.DataBindingComplete += (s, e) => { foreach (DataGridViewColumn c in g.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable; };
            g.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = card, ForeColor = Theme.Ink, SelectionBackColor = Theme.Selection, SelectionForeColor = Theme.Ink,
                Padding = new Padding(6, 0, 4, 0), Font = Theme.Body
            };
        }

        // Editable value columns read as white input cells.
        public static void InputColumn(DataGridView g, string column)
        {
            if (g.Columns[column] == null) return;
            g.Columns[column].DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Paper, Font = Theme.BodyBold, SelectionBackColor = Theme.Selection, SelectionForeColor = Theme.Ink };
        }

        public static ContextMenuStrip Menu()
        {
            var m = new ContextMenuStrip
            {
                Renderer = new GlassRenderer(), BackColor = Theme.Glass, ForeColor = Theme.GlassText, Font = Theme.Body,
                ShowImageMargin = false, ShowCheckMargin = false, Padding = new Padding(4), DropShadowEnabled = true
            };
            try { m.Opacity = Theme.GlassOpacity; } catch { }
            return m;
        }
    }

    class GlassRenderer : ToolStripProfessionalRenderer
    {
        public GlassRenderer() { RoundedEdges = false; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { using (var b = new SolidBrush(Theme.Glass)) e.Graphics.FillRectangle(b, e.AffectedBounds); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { using (var p = new Pen(Theme.GlassLine)) e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1); }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            Theme.Smooth(e.Graphics);
            using (var path = Theme.Round(new RectangleF(3, 1, e.Item.Width - 6, e.Item.Height - 2), 6))
            using (var b = new SolidBrush(Theme.GlassLine)) e.Graphics.FillPath(b, path);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.GlassText : Theme.GlassMuted;
            e.TextRectangle = new Rectangle(e.TextRectangle.X + 6, e.TextRectangle.Y, e.TextRectangle.Width, e.TextRectangle.Height);
            base.OnRenderItemText(e);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(Theme.GlassLine)) e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
        }
    }
}
