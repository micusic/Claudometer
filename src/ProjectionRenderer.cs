using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace TokenMeter
{
    /// <summary>
    /// The burn-up chart: utilization across the fixed five-hour window, drawn only from real API
    /// readings. X is the whole window (start → reset); Y is percent used, 0 → 100. Two fixed
    /// references frame it - a horizontal 100% ceiling and a straight pace line from (start,0) to
    /// (reset,100). The actual line connects the stored API polls and stops at the latest one;
    /// there is deliberately no forward projection, because the app shows measured data only.
    /// </summary>
    public static class ProjectionRenderer
    {
        public static Color ActualColor(Snapshot s)
        {
            if (!s.HasData) return Theme.Muted;
            if (s.FivePct >= 100) return Theme.DangerFill;
            return LevelFill(s.FivePct);
        }

        public static Color PaceColor { get { return Theme.Axis; } }
        public static Color CeilingColor { get { return Theme.DangerFill; } }

        private static Color LevelFill(double pct)
        {
            if (pct >= 90) return Theme.DangerFill;
            if (pct >= 70) return Theme.WarnFill;
            return Theme.OkFill;
        }

        public static void Draw(Graphics g, Rectangle area, Snapshot s, Font font, float scale)
        {
            double winMin = Snapshot.WindowMinutes;
            double vmax = 100.0;

            int leftPad = (int)(40 * scale);
            int bottomPad = (int)(15 * scale);
            var plot = new Rectangle(area.X + leftPad, area.Y,
                                     Math.Max(10, area.Width - leftPad - (int)(4 * scale)),
                                     Math.Max(10, area.Height - bottomPad));
            Rectangle box = plot;
            Func<double, float> X = delegate(double min)
            {
                return box.X + (float)(Math.Max(0, Math.Min(winMin, min)) / winMin) * box.Width;
            };
            Func<double, float> Y = delegate(double pct)
            {
                return (float)(box.Bottom - (Math.Max(0, Math.Min(vmax, pct)) / vmax) * box.Height);
            };

            // y grid + % labels
            using (var pen = new Pen(Theme.Grid))
            using (var br = new SolidBrush(Theme.Axis))
                for (int k = 0; k <= 4; k++)
                {
                    double v = 25.0 * k;
                    float y = Y(v);
                    g.DrawLine(pen, plot.X, y, plot.Right, y);
                    string lab = ((int)v) + "%";
                    SizeF m = g.MeasureString(lab, font);
                    g.DrawString(lab, font, br, plot.X - 4 * scale - m.Width, y - m.Height / 2f);
                }

            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 100% ceiling
            float cy = Y(100);
            using (var p = new Pen(CeilingColor, 1.4f * scale)) { p.DashStyle = DashStyle.Dash; g.DrawLine(p, plot.X, cy, plot.Right, cy); }

            // pace line (0,0) -> (reset,100)
            using (var p = new Pen(PaceColor, 1.3f * scale)) g.DrawLine(p, X(0), Y(0), X(winMin), Y(100));

            if (!s.HasData || s.BurnPct.Count == 0)
            {
                g.SmoothingMode = SmoothingMode.Default;
                string msg = s.LoggedIn ? L.S("chart.waiting") : L.S("chart.loginfirst");
                SizeF mm = g.MeasureString(msg, font);
                using (var b = new SolidBrush(Theme.Faint))
                    g.DrawString(msg, font, b, plot.X + (plot.Width - mm.Width) / 2f, plot.Y + plot.Height / 2f - mm.Height);
                DrawXAxis(g, s, plot, font, scale, X, winMin);
                return;
            }

            Color actual = ActualColor(s);

            // actual: the real API readings, connected in order. No synthetic points - the line is
            // exactly what was observed, so a gap when the app wasn't polling is a gap, not a guess.
            var pts = new List<PointF>();
            for (int i = 0; i < s.BurnPct.Count; i++) pts.Add(new PointF(X(s.BurnMin[i]), Y(s.BurnPct[i])));
            if (pts.Count >= 2)
                using (var p = new Pen(actual, 2.6f * scale) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(p, pts.ToArray());
            PointF tip = pts[pts.Count - 1];

            // forecast: dashed extrapolation of the recent slope from now to the reset (an estimate)
            if (s.HasForecast)
                using (var p = new Pen(actual, 2.2f * scale) { DashStyle = DashStyle.Dash })
                    g.DrawLine(p, tip.X, tip.Y, X(s.ForecastEndMin), Y(s.ForecastEndPct));

            using (var b = new SolidBrush(actual)) g.FillEllipse(b, tip.X - 3.5f * scale, tip.Y - 3.5f * scale, 7f * scale, 7f * scale);

            // 'now' divider at the latest sample
            using (var p = new Pen(Theme.Divider, 1f * scale)) { p.DashStyle = DashStyle.Dash; g.DrawLine(p, tip.X, plot.Y, tip.X, plot.Bottom); }
            {
                string lab = L.S("chart.now");
                SizeF m = g.MeasureString(lab, font);
                float lx = Math.Max(plot.X, Math.Min(tip.X - m.Width / 2f, plot.Right - m.Width));
                using (var b = new SolidBrush(Theme.Muted)) g.DrawString(lab, font, b, lx, plot.Y - 1 * scale);
            }

            g.SmoothingMode = SmoothingMode.Default;
            DrawXAxis(g, s, plot, font, scale, X, winMin);
        }

        private static void DrawXAxis(Graphics g, Snapshot s, Rectangle plot, Font font, float scale,
                                      Func<double, float> X, double winMin)
        {
            DateTime start = s.FiveResetUtc > DateTime.MinValue ? s.FiveResetUtc - Analytics.Window
                                                               : s.NowUtc - Analytics.Window;
            using (var br = new SolidBrush(Theme.Axis))
                for (double t = 0; t <= winMin + 0.5; t += 60)
                {
                    string lab = Fmt.LocalTime(start.AddMinutes(t));
                    SizeF m = g.MeasureString(lab, font);
                    float x = X(t) - m.Width / 2f;
                    if (x < plot.X) x = plot.X;
                    if (x + m.Width > plot.Right) x = plot.Right - m.Width;
                    g.DrawString(lab, font, br, x, plot.Bottom + 2 * scale);
                }
        }
    }
}
