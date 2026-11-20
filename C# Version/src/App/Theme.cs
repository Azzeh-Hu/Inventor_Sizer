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

        // Glass: the material of toasts, dialogs, menus, pop-ups and the activity panel, after macOS
        // (light vibrancy): a light grey sheet, white wells and tables, hairlines, near-black text.
        public static readonly Color Glass = C("#F2F2F4");
        public static readonly Color GlassRaised = C("#FFFFFF");
        public static readonly Color GlassLine = C("#D8D8DC");
        public static readonly Color GlassText = C("#1D1D1F");
        public static readonly Color GlassMuted = C("#6E6E73");
        public const double GlassOpacity = 0.97;

        // macOS system colours: the blue of default buttons, selections and switches, and the rest.
        public static readonly Color MacBlue = C("#007AFF");
        public static readonly Color MacBlueHover = C("#1A88FF");
        public static readonly Color MacBlueDown = C("#0062CC");
        public static readonly Color MacSelection = C("#0063E1");   // a selected table row
        public static readonly Color MacStripe = C("#F4F5F7");      // every second table row
        public static readonly Color MacTrack = C("#E5E5EA");       // switch off, progress track
        public static readonly Color MacBorder = C("#C7C7CC");      // buttons and pop-up buttons
        public static readonly Color MacTertiary = C("#A1A1A6");    // disabled text
        public static readonly Color MacRed = C("#FF3B30");
        public static readonly Color MacGreen = C("#34C759");

        // The Inventor-like frame around the drawing: a dark title bar, the ribbon, the browser
        // on the left and the status bar. Blue is used sparingly (active tab, hover, selection).
        public static readonly Color TitleBar = C("#2D3540");
        public static readonly Color TitleRaised = C("#3E4856");
        public static readonly Color TitleText = C("#E8ECF1");
        public static readonly Color TitleMuted = C("#9AA5B1");
        public static readonly Color RibbonTabs = C("#ECEEF1");
        public static readonly Color RibbonBody = C("#FAFBFC");
        public static readonly Color RibbonLine = C("#D3D8DE");
        public static readonly Color RibbonCaption = C("#6B7480");
        public static readonly Color Accent = C("#0696D7");
        public static readonly Color AccentSoft = C("#E1F1FB");
        public static readonly Color AccentLine = C("#A9D6F0");
        public static readonly Color BrowserSel = C("#CFE7F7");

        // The pop-ups' tables: white input cells, the header on the table's own white.
        public static readonly Color PopInput = C("#FFFFFF");
        public static readonly Color PopHead = C("#FFFFFF");
        public const double PopOpacity = 0.98;

        public const int Radius = 10, RadiusSmall = 6, Gap = 14;

        static Color C(string hex) => ColorTranslator.FromHtml(hex);

        // ---------------------------------------------------------------- fonts
        // Claude's interface pairs a serif for headings with a clean sans for everything else.
        // The first installed family of each list is used; the last ones ship with Windows.
        static readonly string[] SerifFamilies = { "Tiempos Headline", "Tiempos Text", "Copernicus", "Source Serif 4", "Source Serif Pro", "Georgia", "DejaVu Serif" };
        static readonly string[] SansFamilies = { "SF Pro Text", "SF Pro Display", "SF Pro", "Inter", "Segoe UI Variable Text", "Segoe UI", "DejaVu Sans" };
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
        // Titles of pop-ups and dialogs: the sans in bold, as macOS sets them (not the serif).
        public static Font Heading(float size = 10.5f) => new Font(SansName, size, FontStyle.Bold);

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

        // A switch knob: white, with the hairline and soft shadow of a macOS switch.
        public static void Knob(Graphics g, float x, float y, float d)
        {
            using (var s = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(s, x, y + 0.8f, d, d);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, x, y, d, d);
            using (var p = new Pen(Color.FromArgb(30, 0, 0, 0), 0.8f)) g.DrawEllipse(p, x, y, d, d);
        }

        // A checkmark (menus, lists) with its left end at x, centred on y.
        public static void Tick(Graphics g, float x, float y, Color c)
        {
            using (var p = new Pen(c, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { new PointF(x, y), new PointF(x + 3.2f, y + 3.4f), new PointF(x + 9, y - 3.6f) });
        }

        public static Color Mix(Color a, Color b, double t) =>
            Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        // ---------------------------------------------------------------- native: pop-up windows
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

        public const int CS_DROPSHADOW = 0x00020000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;

        // Rounded corners for a borderless window: Windows 11 rounds it natively (with its own
        // shadow); on Windows 10 the window region is rounded instead.
        public static void RoundWindow(Form f, int radius = Radius) => RoundControl(f, radius);

        // The same for any top-level window, e.g. a menu (pref 3 = the small rounding of menus).
        public static void RoundControl(Control f, int radius = Radius, bool small = false)
        {
            bool native = false;
            try { int pref = small ? 3 : 2; native = DwmSetWindowAttribute(f.Handle, 33, ref pref, sizeof(int)) == 0 && Environment.OSVersion.Version.Build >= 22000; }
            catch { }
            if (native) return;
            try { f.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, f.Width + 1, f.Height + 1, radius * 2, radius * 2)); }
            catch { f.Region = null; }
        }
    }
}
