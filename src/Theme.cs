using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace TokenMeter
{
    /// <summary>
    /// The single source of colour for the whole UI. Every form and renderer reads from here,
    /// so switching light/dark is one call and the palette stays consistent.
    ///
    /// Two families of status colour on purpose: a darker *text* shade that stays legible as a
    /// number or a sentence, and a more saturated *fill* shade for bars and the tray tile. On a
    /// light background the same green-on-white has to work both as 22px type and as a 6px bar,
    /// and one value can't do both.
    /// </summary>
    public static class Theme
    {
        public static bool Dark { get; private set; }

        // Surfaces
        public static Color Bg;          // panel background
        public static Color Card;        // raised areas (segmented control, pills)
        public static Color Border;      // outer frame
        public static Color Divider;     // hairline between sections
        public static Color Track;       // empty progress track
        public static Color Hover;       // hovered control background

        // Text
        public static Color Text;        // primary
        public static Color Muted;       // secondary
        public static Color Faint;       // captions, axis labels

        // Accent
        public static Color Accent;      // selected segment, links
        public static Color OnAccent;    // text on the accent fill

        // Status - text shades
        public static Color OkText, WarnText, DangerText;
        // Status - fill shades (bars, icon)
        public static Color OkFill, WarnFill, DangerFill;

        // Charts
        public static Color Grid, Axis, Bar, LoadLine, BudgetLine;
        public static Color BurnLine, SafeLine, ResetLine, ZeroLine;
        public static Color BlockedFill;

        static Theme() { SetMode(false); }

        public static void Apply(string mode)
        {
            SetMode(!string.IsNullOrEmpty(mode) &&
                    mode.Equals("dark", StringComparison.OrdinalIgnoreCase));
        }

        public static void SetMode(bool dark)
        {
            Dark = dark;
            if (dark) Darken();
            else Lighten();
        }

        private static void Lighten()
        {
            Bg = C(0xFF, 0xFF, 0xFF);
            Card = C(0xF3, 0xF5, 0xF8);
            Border = C(0xDD, 0xE1, 0xE6);
            Divider = C(0xEE, 0xF0, 0xF3);
            Track = C(0xEA, 0xED, 0xF1);
            Hover = C(0xEE, 0xF1, 0xF5);

            Text = C(0x1A, 0x1E, 0x24);
            Muted = C(0x5C, 0x66, 0x72);
            Faint = C(0x93, 0x9C, 0xA8);

            Accent = C(0x3B, 0x6F, 0xE0);
            OnAccent = C(0xFF, 0xFF, 0xFF);

            OkText = C(0x12, 0x76, 0x3B);
            WarnText = C(0xAD, 0x63, 0x08);
            DangerText = C(0xC0, 0x36, 0x2B);
            OkFill = C(0x27, 0xAE, 0x60);
            WarnFill = C(0xF2, 0xA0, 0x28);
            DangerFill = C(0xE2, 0x4A, 0x3B);

            Grid = C(0xED, 0xF0, 0xF3);
            Axis = C(0x9A, 0xA2, 0xAD);
            Bar = C(0x6C, 0x96, 0xEE);
            LoadLine = C(0xE0, 0x9A, 0x24);
            BudgetLine = C(0xD8, 0x4C, 0x3E);

            BurnLine = C(0xE0, 0x60, 0x4F);
            SafeLine = C(0x27, 0xAE, 0x60);
            ResetLine = C(0xE0, 0x9A, 0x24);
            ZeroLine = C(0xDA, 0x49, 0x3B);
            BlockedFill = Color.FromArgb(38, 0xE2, 0x4A, 0x3B);
        }

        private static void Darken()
        {
            Bg = C(0x1E, 0x21, 0x26);
            Card = C(0x2C, 0x30, 0x37);
            Border = C(0x40, 0x45, 0x4E);
            Divider = C(0x2E, 0x32, 0x39);
            Track = C(0x2C, 0x30, 0x37);
            Hover = C(0x34, 0x39, 0x42);

            Text = C(0xE6, 0xE8, 0xEB);
            Muted = C(0x9A, 0xA0, 0xA8);
            Faint = C(0x6C, 0x74, 0x7E);

            Accent = C(0x5B, 0x8A, 0xF0);
            OnAccent = C(0xFF, 0xFF, 0xFF);

            OkText = C(0x4E, 0xC7, 0x82);
            WarnText = C(0xE7, 0xA9, 0x3A);
            DangerText = C(0xE8, 0x6E, 0x62);
            OkFill = C(0x2E, 0xA9, 0x5F);
            WarnFill = C(0xE0, 0x9A, 0x24);
            DangerFill = C(0xD8, 0x4C, 0x4C);

            Grid = C(0x38, 0x3C, 0x44);
            Axis = C(0x78, 0x7E, 0x88);
            Bar = C(0x40, 0x84, 0xC4);
            LoadLine = C(0xF0, 0xB0, 0x40);
            BudgetLine = C(0xC8, 0x48, 0x48);

            BurnLine = C(0xE2, 0x68, 0x58);
            SafeLine = C(0x60, 0xBA, 0x84);
            ResetLine = C(0xF0, 0xB0, 0x40);
            ZeroLine = C(0xC4, 0x46, 0x46);
            BlockedFill = Color.FromArgb(56, 0xC8, 0x4C, 0x4C);
        }

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        // ---- shared drawing helpers --------------------------------------------------

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color c)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundRect(r, radius))
            using (var b = new SolidBrush(c))
                g.FillPath(b, path);
            g.SmoothingMode = sm;
        }

        public static void StrokeRound(Graphics g, RectangleF r, float radius, Color c, float w)
        {
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundRect(r, radius))
            using (var p = new Pen(c, w))
                g.DrawPath(p, path);
            g.SmoothingMode = sm;
        }
    }
}
