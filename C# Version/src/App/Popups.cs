// The grey translucent pop-ups: toasts for what just happened, a dialog for questions and
// errors (replaces MessageBox and the old Choose window), and the Activity panel that holds the
// log and the dev log.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace InventorSizer
{
    public enum ToastKind { Done, Info, Error }

    // ------------------------------------------------------------------ base window
    public class GlassForm : Form
    {
        protected readonly bool NoActivate;

        public GlassForm(bool noActivate = false)
        {
            NoActivate = noActivate;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Glass; ForeColor = Theme.GlassText; Font = Theme.Body;
            DoubleBuffered = true; KeyPreview = true;
            try { Opacity = Theme.GlassOpacity; } catch { }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= Theme.CS_DROPSHADOW;
                if (NoActivate) cp.ExStyle |= Theme.WS_EX_NOACTIVATE | Theme.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => NoActivate;
        protected override void OnShown(EventArgs e) { Theme.RoundWindow(this); base.OnShown(e); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (IsHandleCreated && Visible) Theme.RoundWindow(this); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Theme.Smooth(e.Graphics);
            using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.Radius))
            using (var p = new Pen(Theme.GlassLine)) e.Graphics.DrawPath(p, path);
        }

        // A small round badge: tick, exclamation mark or i.
        public static void Badge(Graphics g, RectangleF r, ToastKind kind)
        {
            Theme.Smooth(g);
            using (var b = new SolidBrush(kind == ToastKind.Error ? Theme.GlassText : Theme.GlassLine)) g.FillEllipse(b, r);
            var ink = kind == ToastKind.Error ? Theme.Ink : Theme.GlassText;
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (var p = new Pen(ink, 1.8f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
            {
                if (kind == ToastKind.Done) g.DrawLines(p, new[] { new PointF(cx - 4, cy), new PointF(cx - 1, cy + 3), new PointF(cx + 4.5f, cy - 3) });
                else if (kind == ToastKind.Error) { g.DrawLine(p, cx, cy - 4.5f, cx, cy + 1); g.DrawLine(p, cx, cy + 4, cx, cy + 4.2f); }
                else { g.DrawLine(p, cx, cy - 1, cx, cy + 4.5f); g.DrawLine(p, cx, cy - 4.5f, cx, cy - 4.3f); }
            }
        }
    }

    // ------------------------------------------------------------------ toasts
    public class Toast : GlassForm
    {
        readonly ToastKind kind;
        readonly string title, message;
        readonly Timer life = new Timer(), fade = new Timer { Interval = 16 };
        bool closing;
        public const int W = 360;

        Toast(ToastKind kind, string title, string message) : base(true)
        {
            this.kind = kind; this.title = title; this.message = message ?? "";
            int textW = W - 64;
            int h = 18 + TextRenderer.MeasureText(title, Theme.BodyBold, new Size(textW, 0), TextFormatFlags.WordBreak).Height;
            if (message != "") h += 4 + TextRenderer.MeasureText(Clip(this.message), Theme.Small, new Size(textW, 0), TextFormatFlags.WordBreak).Height;
            Size = new Size(W, Math.Max(56, h + 16));
            Cursor = Cursors.Hand;
            Click += (s, e) => Dismiss();
            life.Interval = kind == ToastKind.Error ? 7000 : 3800;
            life.Tick += (s, e) => Dismiss();
            fade.Tick += (s, e) => Step();
            try { Opacity = 0; } catch { }
        }

        static string Clip(string s) => s.Length > 240 ? s.Substring(0, 237) + "..." : s;

        void Step()
        {
            try
            {
                double target = closing ? 0 : Theme.GlassOpacity;
                double next = Opacity + (closing ? -0.09 : 0.12);
                Opacity = closing ? Math.Max(target, next) : Math.Min(target, next);
                if (Opacity == target) { fade.Stop(); if (closing) Close(); }
            }
            catch { fade.Stop(); if (closing) Close(); }
        }

        public void Dismiss() { if (closing) return; closing = true; life.Stop(); fade.Start(); }

        protected override void OnShown(EventArgs e) { base.OnShown(e); fade.Start(); life.Start(); }
        protected override void OnFormClosed(FormClosedEventArgs e) { life.Dispose(); fade.Dispose(); base.OnFormClosed(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Badge(g, new RectangleF(16, 16, 22, 22), kind);
            int x = 50, w = Width - x - 14;
            var th = TextRenderer.MeasureText(title, Theme.BodyBold, new Size(w, 0), TextFormatFlags.WordBreak).Height;
            TextRenderer.DrawText(g, title, Theme.BodyBold, new Rectangle(x, 16, w, th), Theme.GlassText, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            if (message != "")
                TextRenderer.DrawText(g, Clip(message), Theme.Small, new Rectangle(x, 16 + th + 3, w, Height - th - 20), Theme.GlassMuted, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        // ---- the stack of toasts at the bottom right of the window
        static readonly List<Toast> Open = new List<Toast>();
        public static Func<Rectangle> Anchor;   // screen rectangle the toasts sit in (set by the window)

        public static void Show(Form owner, ToastKind kind, string title, string message = null)
        {
            if (owner == null || owner.IsDisposed || !owner.Visible) return;
            var t = new Toast(kind, title, message) { Owner = owner };
            t.FormClosed += (s, e) => { Open.Remove(t); Layout(); };
            Open.Add(t);
            while (Open.Count(o => !o.closing) > 4) Open.First(o => !o.closing).Dismiss();
            Layout();
            t.Show(owner);
        }

        public static void Layout()
        {
            var r = Anchor?.Invoke() ?? Screen.PrimaryScreen.WorkingArea;
            int y = r.Bottom - 16;
            for (int i = Open.Count - 1; i >= 0; i--)
            {
                var t = Open[i];
                y -= t.Height;
                t.Location = new Point(r.Right - t.Width - 16, y);
                y -= 10;
            }
        }
    }

    // ------------------------------------------------------------------ dialog
    // A question with named buttons, or an error. The first button is the default (Enter), the
    // last answers Esc and closing. Returns the text of the clicked button.
    public class GlassDialog : GlassForm
    {
        public string Answer;

        GlassDialog(string title, string text, ToastKind kind, string[] buttons)
        {
            Answer = buttons[buttons.Length - 1];
            const int pad = 24, maxText = 480;
            var titleFont = Theme.Serif(14f);
            var head = new Label { Text = title, Font = titleFont, ForeColor = Theme.GlassText, AutoSize = true, Location = new Point(pad + 34, pad - 2), BackColor = Color.Transparent, UseMnemonic = false };
            var body = new Label { Text = text, Font = Theme.Body, ForeColor = Theme.GlassMuted, AutoSize = true, MaximumSize = new Size(maxText, 0), Location = new Point(pad, pad + 40), BackColor = Color.Transparent, UseMnemonic = false };
            var badge = new Panel { Location = new Point(pad, pad), Size = new Size(24, 24), BackColor = Theme.Glass };
            badge.Paint += (s, e) => Badge(e.Graphics, new RectangleF(1, 1, 21, 21), kind);
            Controls.AddRange(new Control[] { badge, head, body });

            // buttons right aligned, the default (first) one on the far right
            var btns = new List<FlatButton>();
            for (int i = 0; i < buttons.Length; i++)
            {
                string b = buttons[i];
                var btn = new FlatButton(b, i == 0 ? ButtonKind.GlassPrimary : ButtonKind.Glass, 0, 34);
                btn.Width = Math.Max(96, btn.Width);
                btn.Click += (s, e) => { Answer = b; Close(); };
                btns.Add(btn);
                if (i == 0) AcceptButton = btn;
                if (i == buttons.Length - 1) CancelButton = btn;
            }
            int rowW = btns.Sum(b => b.Width) + 8 * (btns.Count - 1);
            int width = Math.Max(Math.Max(head.Right, body.Right) + pad, Math.Max(400, rowW + pad * 2));
            int top = body.Bottom + pad, x = width - pad;
            foreach (var btn in btns) { x -= btn.Width; btn.Location = new Point(x, top); x -= 8; Controls.Add(btn); }
            Size = new Size(width, top + 34 + pad);
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { Answer = buttons[buttons.Length - 1]; Close(); } };
        }

        public static string Ask(Form owner, string title, string text, params string[] buttons) => Run(owner, title, text, ToastKind.Info, buttons);
        public static void Error(Form owner, string title, string text) => Run(owner, title, text, ToastKind.Error, new[] { "OK" });

        static string Run(Form owner, string title, string text, ToastKind kind, string[] buttons)
        {
            using (var d = new GlassDialog(title, text, kind, buttons))
            using (var dim = new Form { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, BackColor = Color.Black })
            {
                var host = owner != null && !owner.IsDisposed && owner.Visible ? owner : null;
                if (host != null)
                {
                    try { dim.Opacity = 0.22; } catch { }
                    dim.Bounds = host.RectangleToScreen(host.ClientRectangle);
                    dim.Show(host);
                    var r = dim.Bounds;
                    d.Location = new Point(r.X + (r.Width - d.Width) / 2, r.Y + (r.Height - d.Height) / 2 - 20);
                    d.ShowDialog(dim);
                }
                else { d.StartPosition = FormStartPosition.CenterScreen; d.ShowDialog(); }
                return d.Answer;
            }
        }
    }

    // ------------------------------------------------------------------ activity panel
    // The log and the dev log in a floating glass panel at the bottom right of the window.
    public class ActivityPanel : GlassForm
    {
        public readonly TextBox LogBox, DevBox;
        public readonly FlatButton SaveDev;
        readonly FlatButton tabLog, tabDev;

        public ActivityPanel()
        {
            Size = new Size(620, 340);
            var title = new Label { Text = "Activity", Font = Theme.Serif(13f), ForeColor = Theme.GlassText, AutoSize = true, Location = new Point(18, 14), BackColor = Color.Transparent };
            tabLog = new FlatButton("Log", ButtonKind.GlassPrimary, 0, 28);
            tabDev = new FlatButton("Dev log", ButtonKind.Glass, 0, 28);
            SaveDev = new FlatButton("Save dev log...", ButtonKind.Glass, 0, 28);
            var close = new FlatButton("✕", ButtonKind.Glass, 30, 28);
            tabLog.Location = new Point(112, 12); tabDev.Location = new Point(tabLog.Right + 6, 12);
            Controls.AddRange(new Control[] { title, tabLog, tabDev });
            SaveDev.Anchor = AnchorStyles.Top | AnchorStyles.Right; close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.Location = new Point(Width - close.Width - 14, 12);
            SaveDev.Location = new Point(close.Left - SaveDev.Width - 8, 12);
            Controls.AddRange(new Control[] { SaveDev, close });

            TextBox Box(float size, bool wrap) => new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = wrap ? ScrollBars.Vertical : ScrollBars.Both, WordWrap = wrap,
                BorderStyle = BorderStyle.None, BackColor = Theme.GlassRaised, ForeColor = Theme.GlassText, Font = Theme.Mono(size),
                Location = new Point(18, 54), Size = new Size(Width - 36, Height - 72), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            LogBox = Box(9, true); DevBox = Box(8, false); DevBox.Visible = false;
            var well = new Panel { Location = new Point(10, 46), Size = new Size(Width - 20, Height - 56), BackColor = Theme.GlassRaised, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(LogBox); Controls.Add(DevBox); Controls.Add(well);
            well.SendToBack();
            well.Paint += (s, e) => { };

            tabLog.Click += (s, e) => Show(false);
            tabDev.Click += (s, e) => Show(true);
            close.Click += (s, e) => Hide();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
            FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        }

        void Show(bool dev)
        {
            DevBox.Visible = dev; LogBox.Visible = !dev;
            tabDev.Kind = dev ? ButtonKind.GlassPrimary : ButtonKind.Glass; tabLog.Kind = dev ? ButtonKind.Glass : ButtonKind.GlassPrimary;
            tabDev.Font = dev ? Theme.BodyBold : Theme.Body; tabLog.Font = dev ? Theme.Body : Theme.BodyBold;
            tabDev.Invalidate(); tabLog.Invalidate();
        }
    }
}
