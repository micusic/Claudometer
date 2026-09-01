using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TokenMeter
{
    /// <summary>
    /// Draws the tray icon: a filled tile carrying the percent of the five-hour budget used.
    ///
    /// A tray icon is 16px at 100% DPI. A ring gauge is unreadable at that size, so the
    /// number carries the detail and the fill colour carries the urgency - readable from
    /// across the room without hovering.
    /// </summary>
    public static class IconRenderer
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static readonly Color Ok = Color.FromArgb(29, 122, 60);
        public static readonly Color Warn = Color.FromArgb(191, 106, 12);
        public static readonly Color Danger = Color.FromArgb(183, 38, 38);
        public static readonly Color IdleGray = Color.FromArgb(88, 92, 98);

        public static Color LevelColor(double pct, double warnPct, double dangerPct)
        {
            if (pct >= dangerPct) return Danger;
            if (pct >= warnPct) return Warn;
            return Ok;
        }

        /// <summary>
        /// Builds a tray icon. The caller owns the result and must pass it to
        /// <see cref="Dispose"/> - the underlying HICON is not freed by Icon.Dispose alone.
        /// </summary>
        public static Icon Render(double pct, Color fill, bool alert)
        {
            Size sz = SystemInformation.SmallIconSize;
            int w = Math.Max(16, sz.Width);
            int h = Math.Max(16, sz.Height);

            using (var bmp = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    var rect = new Rectangle(0, 0, w - 1, h - 1);
                    int radius = Math.Max(2, w / 5);
                    using (GraphicsPath path = Rounded(rect, radius))
                    using (var b = new SolidBrush(fill))
                        g.FillPath(b, path);

                    if (alert)
                    {
                        // A bright rim marks "projected to run out before this window resets".
                        using (GraphicsPath path = Rounded(rect, radius))
                        using (var p = new Pen(Color.FromArgb(255, 214, 102), Math.Max(1f, w / 12f)))
                            g.DrawPath(p, path);
                    }

                    string text = Label(pct);
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    DrawFitted(g, text, new RectangleF(0, 0, w, h), Color.White);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        private static string Label(double pct)
        {
            int p = (int)Math.Round(pct * 100.0);
            if (p >= 100) return "!!";
            if (p < 0) p = 0;
            return p.ToString();
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Picks the largest bold size that fits, so 1-, 2-digit and "!!" all fill the tile.</summary>
        private static void DrawFitted(Graphics g, string text, RectangleF box, Color color)
        {
            float target = box.Width * 0.86f;
            for (float size = box.Height; size >= 5f; size -= 0.5f)
            {
                using (var f = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    SizeF m = g.MeasureString(text, f, PointF.Empty, StringFormat.GenericTypographic);
                    if (m.Width > target || m.Height > box.Height * 0.98f) continue;
                    using (var br = new SolidBrush(color))
                    {
                        var fmt = StringFormat.GenericTypographic;
                        g.DrawString(text, f, br,
                            new PointF(box.X + (box.Width - m.Width) / 2f,
                                       box.Y + (box.Height - m.Height) / 2f), fmt);
                    }
                    return;
                }
            }
        }

        /// <summary>Frees both the managed Icon and the HICON behind it.</summary>
        public static void Dispose(Icon icon)
        {
            if (icon == null) return;
            IntPtr h = icon.Handle;
            try { icon.Dispose(); } catch (Exception) { }
            try { DestroyIcon(h); } catch (Exception) { }
        }

        /// <summary>A larger version of the same mark, for window title bars and Alt+Tab.</summary>
        public static Icon RenderLarge(double pct, Color fill)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    using (GraphicsPath path = Rounded(new Rectangle(0, 0, 31, 31), 7))
                    using (var b = new SolidBrush(fill))
                        g.FillPath(b, path);
                    DrawFitted(g, Label(pct), new RectangleF(0, 0, 32, 32), Color.White);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
