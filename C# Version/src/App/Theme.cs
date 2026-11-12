// The look of the window: one monochrome palette (black, white and greys), the fonts, and the
// small native helpers that give pop-ups rounded corners and a shadow. Everything in App\ takes
// its colours and fonts from here, so the whole window can be retuned in one place.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InventorSizer
{
    public static class Theme
    {
        // ---------------------------------------------------------------- palette
        public static readonly Color Ink = C("#141414");          // primary text, primary buttons
        public static readonly Color InkSoft = C("#3A3A3A");      // secondary text
        public static readonly Color Muted = C("#7C7C7C");        // hints, locked values
        public static readonly Color Faint = C("#A8A8A8");        // placeholders, disabled
        public static readonly Color Line = C("#E2E2E2");         // hairlines, borders
        public static readonly Color LineStrong = C("#CFCFCF");   // input borders
        public static readonly Color App = C("#F6F6F6");          // window background
        public static readonly Color Paper = C("#FFFFFF");        // the drawing, inputs
        public static readonly Color Card = C("#EFEFEF");         // table cards ("grey pop-ups")
        public static readonly Color CardHead = C("#E6E6E6");     // table header rows
        public static readonly Color Hover = C("#E4E4E4");
        public static readonly Color Pressed = C("#D6D6D6");
        public static readonly Color Selection = C("#DCDCDC");

        // Glass: the translucent grey of toasts, dialogs, menus and the activity panel.
        public static readonly Color Glass = C("#2B2B2B");
        public static readonly Color GlassRaised = C("#383838");
        public static readonly Color GlassLine = C("#4A4A4A");
        public static readonly Color GlassText = C("#F2F2F2");
        public static readonly Color GlassMuted = C("#A6A6A6");
        public const double GlassOpacity = 0.93;

        public const int Radius = 12, RadiusSmall = 8, Gap = 14;

        static Color C(string hex) => ColorTranslator.FromHtml(hex);

        // ---------------------------------------------------------------- fonts
        // Claude's interface pairs a serif for headings with a clean sans for everything else.
        // The first installed family of each list is used; the last ones ship with Windows.
        static readonly string[] SerifFamilies = { "Tiempos Headline", "Tiempos Text", "Copernicus", "Source Serif 4", "Source Serif Pro", "Georgia", "DejaVu Serif" };
        static readonly string[] SansFamilies = { "Styrene B", "Styrene A", "Inter", "Segoe UI Variable Text", "Segoe UI", "DejaVu Sans" };
        static readonly string[] MonoFamilies = { "JetBrains Mono", "Cascadia Mono", "Consolas", "DejaVu Sans Mono" };

        public static readonly string SerifName = Pick(SerifFamilies, "Georgia");
        public static readonly string SansName = Pick(SansFamilies, "Segoe UI");
        public static readonly string MonoName = Pick(MonoFamilies, "Consolas");

        static string Pick(string[] wanted, string fallback)
        {
            try
            {
                using (var fonts = new InstalledFontCollection())
                {
                    var have = fonts.Families.Select(f => f.Name).ToList();
                    foreach (var w in wanted) if (have.Any(h => string.Equals(h, w, StringComparison.OrdinalIgnoreCase))) return w;
                }
            }
            catch { }
            return fallback;
        }

        public static Font Ui(float size = 9.5f, FontStyle style = FontStyle.Regular) => new Font(SansName, size, style);
        public static Font Serif(float size = 14f, FontStyle style = FontStyle.Regular) => new Font(SerifName, size, style);
        public static Font Mono(float size = 9f) => new Font(MonoName, size);

        public static readonly Font Body = Ui(9.5f);
        public static readonly Font BodyBold = Ui(9.5f, FontStyle.Bold);
        public static readonly Font Small = Ui(8.5f);
        public static readonly Font Title = Serif(15f);
        public static readonly Font CardTitle = Serif(12f);

        // ---------------------------------------------------------------- drawing helpers
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Max(0.1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static GraphicsPath Round(Rectangle r, float radius) => Round(new RectangleF(r.X, r.Y, r.Width, r.Height), radius);

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        // A soft shadow under a rounded rectangle: a few widening, fading outlines.
        public static void Shadow(Graphics g, Rectangle r, float radius, int depth = 6, int alpha = 16)
        {
            for (int i = depth; i >= 1; i--)
            {
                var rr = new RectangleF(r.X - i * 0.5f, r.Y - i * 0.25f + 2, r.Width + i, r.Height + i);
                using (var path = Round(rr, radius + i * 0.6f))
                using (var b = new SolidBrush(Color.FromArgb(Math.Max(1, alpha * (depth - i + 1) / depth / 2), 0, 0, 0)))
                    g.FillPath(b, path);
            }
        }

        public static Color Mix(Color a, Color b, double t) =>
            Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        // ---------------------------------------------------------------- native: pop-up windows
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

        public const int CS_DROPSHADOW = 0x00020000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;

        // Rounded corners for a borderless window: Windows 11 rounds it natively (with its own
        // shadow); on Windows 10 the window region is rounded instead.
        public static void RoundWindow(Form f, int radius = Radius)
        {
            bool native = false;
            try { int pref = 2; native = DwmSetWindowAttribute(f.Handle, 33, ref pref, sizeof(int)) == 0 && Environment.OSVersion.Version.Build >= 22000; }
            catch { }
            if (native) return;
            try { f.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, f.Width + 1, f.Height + 1, radius * 2, radius * 2)); }
            catch { f.Region = null; }
        }
    }
}
