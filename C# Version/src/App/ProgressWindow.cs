// The loading bar of a long step: a small glass window over the main window with what is being
// done, a bar, the time spent and about how long is left (Core\Progress). It runs on its own
// thread with its own message loop, because the main window is busy with Inventor during the
// step and does not redraw; so the bar keeps moving and the time keeps counting. The app's
// button in the Windows taskbar shows the same progress (seen while working in Inventor).
// Shown only for steps that take longer than a moment.

using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using InventorSizer.Core;

namespace InventorSizer
{
    public sealed class ProgressWindow : GlassForm
    {
        const int W = 440, H = 128, Pad = 20;
        const double ShowAfter = 0.8;   // seconds: shorter steps never show the window

        // Hooks the window to Progress; called once by the main window.
        public static void Attach(Form main)
        {
            Progress.TimingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventorSizer", "timings.json");
            Progress.Started += _ =>
            {
                Rectangle area; IntPtr hwnd;
                try { area = main.RectangleToScreen(main.ClientRectangle); hwnd = main.Handle; }
                catch { area = Screen.PrimaryScreen.WorkingArea; hwnd = IntPtr.Zero; }
                int run = Progress.Snapshot().Run;
                var t = new Thread(() =>
                {
                    try { Application.Run(new ProgressWindow(run, area, hwnd)); }
                    catch (Exception e) { Log.Dev($"progress window: {e.Message}"); }
                }) { IsBackground = true, Name = "Progress window" };
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
            };
        }

        readonly int run;
        readonly IntPtr owner;
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 100 };
        // own fonts: the shared ones in Theme are used by the main window's thread
        readonly Font titleFont = Theme.Serif(12.5f), bodyFont = Theme.Ui(9f), smallFont = Theme.Ui(8.5f);
        ProgressSnapshot snap = new ProgressSnapshot();
        double shown, left = -1;   // bar and time left as shown: smoothed, the bar never goes back
        bool visible;
        Taskbar taskbar;

        ProgressWindow(int run, Rectangle area, IntPtr owner) : base(true)
        {
            this.run = run; this.owner = owner;
            Font = bodyFont;
            Size = new Size(W, H);
            Location = new Point(area.Left + (area.Width - W) / 2, area.Top + (area.Height - H) / 2);
            try { Opacity = 0; } catch { }
            tick.Tick += (s, e) => Tick();
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); tick.Start(); Tick(); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            tick.Stop(); tick.Dispose();
            taskbar?.Clear(owner);
            titleFont.Dispose(); bodyFont.Dispose(); smallFont.Dispose();
            base.OnFormClosed(e);
        }

        void Tick()
        {
            snap = Progress.Snapshot();
            if (snap.Run != run || !Progress.Active) { Close(); return; }
            if (!visible)
            {
                if (snap.Elapsed < ShowAfter) return;
                visible = true;
                try { Opacity = Theme.GlassOpacity; } catch { }
                try { taskbar = new Taskbar(); } catch { taskbar = null; }
            }
            // the bar eases towards the estimate and never moves back
            double target = Math.Min(0.99, Math.Max(shown, snap.Fraction));
            shown += (target - shown) * 0.25;
            // the time left counts down smoothly; it only jumps when the estimate really changes
            double dt = tick.Interval / 1000.0;
            if (left < 0 || Math.Abs(snap.Remaining - left) > Math.Max(5, left * 0.2)) left = snap.Remaining;
            else left = Math.Max(0, Math.Min(left - dt, snap.Remaining + 2));
            taskbar?.Set(owner, shown);
            Invalidate();
        }

        static string Duration(double s)
        {
            int t = (int)Math.Round(s);
            return t < 60 ? $"{t} s" : $"{t / 60}:{t % 60:00} min";
        }

        // "about 2 min 30 s left", rounded so it does not flicker
        string LeftText()
        {
            if (snap.Overrun) return "taking longer than usual...";
            if (snap.Elapsed < 2 || left < 0) return "estimating...";
            double r = left;
            int r10 = (int)Math.Round(r / 10) * 10;
            string text = r < 10 ? "a few seconds left"
                : r < 60 ? $"about {(int)Math.Ceiling(r / 5) * 5} s left"
                : r10 % 60 == 0 ? $"about {r10 / 60} min left"
                : $"about {r10 / 60} min {r10 % 60} s left";
            return snap.Rough ? text + " (first run: rough)" : text;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            int w = Width - 2 * Pad;
            TextRenderer.DrawText(g, snap.Title, titleFont, new Rectangle(Pad, 16, w, 26), Theme.GlassText, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, snap.Label, bodyFont, new Rectangle(Pad, 46, w, 20), Theme.GlassMuted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            var track = new Rectangle(Pad, 76, w, 8);
            Theme.Smooth(g);
            using (var path = Theme.Round(track, 4))
            using (var b = new SolidBrush(Theme.GlassRaised)) g.FillPath(b, path);
            int fill = (int)Math.Round(track.Width * Math.Max(0, Math.Min(1, shown)));
            if (fill >= 8)
                using (var path = Theme.Round(new Rectangle(track.X, track.Y, fill, track.Height), 4))
                using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
            string spent = $"{Duration(snap.Elapsed)} elapsed   {Math.Round(shown * 100)} %";
            TextRenderer.DrawText(g, spent, smallFont, new Rectangle(Pad, 94, w, 18), Theme.GlassMuted, TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, LeftText(), smallFont, new Rectangle(Pad, 94, w, 18), Theme.GlassText, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
        }

        // ---- progress on the app's button in the Windows taskbar
        [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITaskbarList3
        {
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
            void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
            void SetProgressState(IntPtr hwnd, int flags);
        }

        [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
        class TaskbarList { }

        sealed class Taskbar
        {
            readonly ITaskbarList3 list;
            public Taskbar() { list = (ITaskbarList3)new TaskbarList(); list.HrInit(); }
            public void Set(IntPtr hwnd, double f)
            {
                if (hwnd == IntPtr.Zero) return;
                try { list.SetProgressState(hwnd, 2); list.SetProgressValue(hwnd, (ulong)(f * 1000), 1000); } catch { }
            }
            public void Clear(IntPtr hwnd)
            {
                if (hwnd == IntPtr.Zero) return;
                try { list.SetProgressState(hwnd, 0); } catch { }
            }
        }
    }
}
