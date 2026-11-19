// A floating glass window for GR editing (boxes, dividers, openings): see-through, moved by its
// title bar, resized at its right and bottom edges, faded in and out, an optional row of buttons
// under the title. Built like the panel tool's parameter pop-up, kept apart from it.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace InventorSizer
{
    public class GrPopup : GlassForm
    {
        readonly Panel head;
        readonly Label title, sub;
        public readonly FlowLayoutPanel Actions;
        public readonly Panel Body;
        readonly Timer fade = new Timer { Interval = 15 };
        bool closing, placed;
        public event Action Dismissed;

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        public GrPopup(string titleText, Size size)
        {
            Size = size; MinimumSize = new Size(360, 180);
            Padding = new Padding(5, 1, 5, 5);
            head = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Glass, Cursor = Cursors.SizeAll };
            title = new Label { Text = titleText, Font = Theme.Serif(13f), ForeColor = Theme.GlassText, AutoSize = true, Location = new Point(14, 13), BackColor = Color.Transparent, Cursor = Cursors.SizeAll };
            sub = new Label { Text = "", Font = Theme.Small, ForeColor = Theme.GlassMuted, AutoSize = true, BackColor = Color.Transparent, Cursor = Cursors.SizeAll };
            var close = new FlatButton("✕", ButtonKind.Glass, 30, 28) { Anchor = AnchorStyles.Top | AnchorStyles.Right };
            head.Controls.AddRange(new Control[] { title, sub, close });
            head.Layout += (s, e) =>
            {
                close.Location = new Point(head.Width - close.Width - 10, 10);
                sub.Location = new Point(title.Left + TextRenderer.MeasureText(title.Text, title.Font).Width + 6, 18);
            };
            foreach (Control c in new Control[] { head, title, sub }) c.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) Drag(); };
            Actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Glass, WrapContents = false, Padding = new Padding(10, 2, 10, 6), Visible = false };
            Actions.ControlAdded += (s, e) => Actions.Visible = true;
            Body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(9, 0, 9, 9), BackColor = Theme.Glass };
            Controls.Add(Body); Controls.Add(Actions); Controls.Add(head);
            close.Click += (s, e) => Dismiss();
            fade.Tick += (s, e) => FadeStep();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && !(ActiveControl is DataGridView g && g.IsCurrentCellInEditMode)) Dismiss(); };
            FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Dismiss(); } };
        }

        public string Subtitle { get => sub.Text; set { sub.Text = value; head.PerformLayout(); } }

        void Drag()
        {
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 2, 0);   // WM_NCLBUTTONDOWN on the caption: Windows moves the window
        }

        public bool Open => Visible && !closing;

        // Shows the window; the first time at the given screen point, later where the user left it.
        public void Popup(Point at)
        {
            if (!placed) { Location = at; placed = true; }
            if (Open) { Activate(); return; }
            closing = false;
            if (!Visible) { try { Opacity = 0; } catch { } Show(); }
            fade.Start();
        }

        public void Toggle(Point at) { if (Open) Dismiss(); else Popup(at); }

        public void Dismiss()
        {
            if (!Visible || closing) return;
            foreach (Control c in Body.Controls) if (c is DataGridView g) g.EndEdit();
            closing = true;
            fade.Start();
        }

        void FadeStep()
        {
            try
            {
                double target = closing ? 0 : Theme.PopOpacity;
                double next = Opacity + (closing ? -0.12 : 0.1);
                Opacity = closing ? Math.Max(target, next) : Math.Min(target, next);
                if (Opacity != target) return;
            }
            catch { }
            fade.Stop();
            if (closing) { Hide(); closing = false; Dismissed?.Invoke(); }
        }

        // The right and bottom edges resize the window.
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0x84 || (int)m.Result != 1) return;   // WM_NCHITTEST on the client area
            long lp = m.LParam.ToInt64();
            var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            bool right = p.X >= Width - 6, bottom = p.Y >= Height - 6;
            if (right && bottom) m.Result = (IntPtr)17; else if (right) m.Result = (IntPtr)11; else if (bottom) m.Result = (IntPtr)15;
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { fade.Dispose(); base.OnFormClosed(e); }
    }

    // An on / off switch drawn for the dark glass (the panel tool's Toggle is drawn for light cards).
    public class GlassToggle : CheckBox
    {
        public GlassToggle(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoSize = false; Font = Theme.Body; Cursor = Cursors.Hand; Margin = new Padding(0, 2, 12, 0);
            Size = new Size(TextRenderer.MeasureText(text, Font).Width + 46, 28);
        }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Glass);
            Theme.Smooth(g);
            var track = new RectangleF(1, (Height - 18) / 2f, 32, 18);
            using (var path = Theme.Round(track, 9))
            using (var b = new SolidBrush(Checked ? Theme.Accent : Theme.GlassLine))
                g.FillPath(b, path);
            float kx = Checked ? track.Right - 16 : track.X + 2;
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, track.Y + 2, 14, 14);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(42, 0, Width - 42, Height), Theme.GlassText, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
