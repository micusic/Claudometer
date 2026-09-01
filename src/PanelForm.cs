using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace TokenMeter
{
    /// <summary>
    /// The pop-up panel. Owner-drawn in one pass. API-only: everything shown is a number the
    /// usage endpoint returned. Without a login there is nothing to show, so the panel says so.
    /// </summary>
    public class PanelForm : Form
    {
        private const string FontName = "Microsoft YaHei UI";
        private const int BaseWidth = 428;
        private const int BaseHeight = 486;
        private const int Pad = 18;

        internal readonly float _s;
        private readonly Font _f9, _f8, _f7, _f11b, _f26b, _f9b;

        private Snapshot _snap;
        private AppConfig _cfg;
        private string _hot;

        private readonly Dictionary<string, Rectangle> _zones = new Dictionary<string, Rectangle>();

        public event EventHandler RefreshRequested;
        public event EventHandler SettingsRequested;
        public event EventHandler LoginRequested;

        public bool SuppressAutoHide { get; set; }

        public PanelForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            KeyPreview = true;

            _s = Dpi() / 96f;
            ClientSize = new Size(S(BaseWidth), S(BaseHeight));

            _f9 = new Font(FontName, 9f);
            _f8 = new Font(FontName, 8f);
            _f7 = new Font(FontName, 7.5f);
            _f9b = new Font(FontName, 9f, FontStyle.Bold);
            _f11b = new Font(FontName, 11f, FontStyle.Bold);
            _f26b = new Font(FontName, 26f, FontStyle.Bold);
        }

        private static float Dpi()
        {
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX; }
            catch (Exception) { return 96f; }
        }

        private int S(int v) { return (int)Math.Round(v * _s); }
        private int Rx { get { return ClientSize.Width - S(Pad); } }
        private int Lx { get { return S(Pad); } }

        public void Update(Snapshot snap, AppConfig cfg)
        {
            _snap = snap; _cfg = cfg;
            if (IsHandleCreated) { BackColor = Theme.Bg; Invalidate(); }
        }

        public void ShowNearTray()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Left = Math.Max(wa.Left + 8, wa.Right - Width - 12);
            Top = Math.Max(wa.Top + 8, wa.Bottom - Height - 12);
            Show();
            TopMost = false; TopMost = true;
            Activate(); BringToFront();
        }

        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); if (!SuppressAutoHide) Hide(); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Hide();
            else if (e.KeyCode == Keys.F5) Raise(RefreshRequested);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string hit = HitTest(e.Location);
            if (hit != _hot) { _hot = hit; Cursor = hit != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot != null) { _hot = null; Cursor = Cursors.Default; Invalidate(); }
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            string hit = HitTest(e.Location);
            if (hit == null) return;
            switch (hit)
            {
                case "close": Hide(); break;
                case "refresh": Raise(RefreshRequested); break;
                case "settings": Raise(SettingsRequested); break;
                case "login": Raise(LoginRequested); break;
            }
        }

        private void Raise(EventHandler h) { if (h != null) h(this, EventArgs.Empty); }
        private string HitTest(Point p) { foreach (var kv in _zones) if (kv.Value.Contains(p)) return kv.Key; return null; }

        private Color LevelText(double pct100)
        {
            if (pct100 >= _cfg.DangerPct * 100) return Theme.DangerText;
            if (pct100 >= _cfg.WarnPct * 100) return Theme.WarnText;
            return Theme.OkText;
        }
        private Color LevelFill(double pct100)
        {
            if (pct100 >= _cfg.DangerPct * 100) return Theme.DangerFill;
            if (pct100 >= _cfg.WarnPct * 100) return Theme.WarnFill;
            return Theme.OkFill;
        }
        private static string P(double pct100)
        {
            return double.IsNaN(pct100) ? "—" : ((int)Math.Round(pct100)) + "%";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Bg);
            _zones.Clear();
            Theme.StrokeRound(g, new RectangleF(0.5f, 0.5f, ClientSize.Width - 1, ClientSize.Height - 1), S(10), Theme.Border, 1f);

            int y = S(16);
            y = PaintHeader(g, y);

            if (_snap == null || _cfg == null) { Str(g, "启动中…", _f9, Theme.Muted, Lx, y); return; }
            if (!_snap.LoggedIn) { PaintLoginNeeded(g, y); return; }
            if (!_snap.HasData) { PaintWaiting(g, y); return; }

            y = PaintFive(g, y);
            y = PaintChart(g, y);
            y = PaintSeven(g, y);
            PaintModels(g, y);
            PaintFooter(g);
        }

        private int PaintHeader(Graphics g, int y)
        {
            using (var b = new SolidBrush(Theme.Accent))
            {
                var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(b, Lx, y + S(4), S(9), S(9));
                g.SmoothingMode = sm;
            }
            Str(g, "Claudometer", _f11b, Theme.Text, Lx + S(15), y - S(1));

            int bx = Rx - S(28);
            bx = IconButton(g, "close", "✕", bx, y - S(3));
            bx = IconButton(g, "settings", "⚙", bx - S(6), y - S(3));
            IconButton(g, "refresh", "↻", bx - S(6), y - S(3));

            y += S(30);
            Line(g, y);
            return y + S(14);
        }

        private int IconButton(Graphics g, string key, string glyph, int x, int y)
        {
            var r = new Rectangle(x, y, S(28), S(26));
            _zones[key] = r;
            if (_hot == key) Theme.FillRound(g, r, S(6), Theme.Hover);
            var fmt = new StringFormat(); fmt.Alignment = StringAlignment.Center; fmt.LineAlignment = StringAlignment.Center;
            using (var br = new SolidBrush(_hot == key ? Theme.Text : Theme.Muted)) g.DrawString(glyph, _f9b, br, r, fmt);
            return x - S(28);
        }

        private void PaintLoginNeeded(Graphics g, int y)
        {
            int cx = ClientSize.Width / 2;
            y += S(48);
            CenterStr(g, "需要登录 Claude", _f11b, Theme.Text, cx, y); y += S(28);
            CenterStr(g, "本工具只显示官方用量接口的数据。", _f8, Theme.Muted, cx, y); y += S(18);
            CenterStr(g, "登录在你自己的浏览器完成，不经手密码。", _f8, Theme.Muted, cx, y); y += S(30);

            string label = "登录 Claude";
            SizeF m = g.MeasureString(label, _f9b);
            int bw = (int)m.Width + S(40), bh = S(34);
            var r = new Rectangle(cx - bw / 2, y, bw, bh);
            _zones["login"] = r;
            Theme.FillRound(g, r, S(7), _hot == "login" ? Theme.Accent : Theme.Accent);
            var fmt = new StringFormat(); fmt.Alignment = StringAlignment.Center; fmt.LineAlignment = StringAlignment.Center;
            using (var br = new SolidBrush(Theme.OnAccent)) g.DrawString(label, _f9b, br, r, fmt);
        }

        private void PaintWaiting(Graphics g, int y)
        {
            int cx = ClientSize.Width / 2;
            y += S(56);
            string msg = string.IsNullOrEmpty(_snap.ApiStatus) ? "正在获取官方用量…" : _snap.ApiStatus;
            CenterStr(g, msg, _f9, Theme.Muted, cx, y);
        }

        private int PaintFive(Graphics g, int y)
        {
            Snapshot s = _snap;
            Color txt = LevelText(s.FivePct), fill = LevelFill(s.FivePct);

            DateTime start = s.FiveResetUtc - Analytics.Window;
            string head = "5 小时窗口";
            if (s.FiveResetUtc > DateTime.MinValue)
                head += "  " + Fmt.LocalTime(start) + " → " + Fmt.LocalTime(s.FiveResetUtc);
            Str(g, head, _f9b, Theme.Muted, Lx, y + S(12));

            string pct = P(s.FivePct);
            SizeF pm = g.MeasureString(pct, _f26b);
            Str(g, pct, _f26b, txt, Rx - pm.Width, y - S(4));

            y += S(38);
            RoundBar(g, new Rectangle(Lx, y, ClientSize.Width - 2 * Lx, S(9)), s.FivePct / 100.0, fill);
            y += S(19);

            // source pill
            PaintPill(g, y);
            string reset = s.FiveResetUtc > s.NowUtc
                ? Fmt.Duration(s.ToReset) + "后重置"
                : "已重置";
            Str(g, reset, _f8, Theme.Muted, Lx, y + S(1));
            y += S(24);
            return y;
        }

        private void PaintPill(Graphics g, int y)
        {
            Snapshot s = _snap;
            int age = (int)(s.NowUtc - s.ApiLiveUtc).TotalSeconds;
            bool fresh = age < 8 * 60;
            string text; Color dot, fg;
            if (fresh) { text = "官方 · " + (age < 75 ? "刚刚" : Fmt.Duration(TimeSpan.FromSeconds(age)) + "前"); dot = Theme.OkFill; fg = Theme.OkText; }
            else { text = "官方 · " + Fmt.Duration(TimeSpan.FromSeconds(age)) + "前（刷新中）"; dot = Theme.WarnFill; fg = Theme.WarnText; }

            SizeF tm = g.MeasureString(text, _f7);
            int w = S(18) + (int)Math.Ceiling(tm.Width) + S(10), h = S(17);
            var pill = new Rectangle(Rx - w, y - S(1), w, h);
            Theme.FillRound(g, pill, h / 2f, Theme.Card);
            var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, pill.X + S(8), pill.Y + h / 2 - S(3), S(6), S(6));
            g.SmoothingMode = sm;
            Str(g, text, _f7, fg, pill.X + S(18), pill.Y + (h - tm.Height) / 2f);
        }

        private int PaintChart(Graphics g, int y)
        {
            Str(g, "本窗口燃起图", _f9b, Theme.Text, Lx, y);
            y += S(22);
            var area = new Rectangle(Lx, y, ClientSize.Width - 2 * Lx, S(132));
            ProjectionRenderer.Draw(g, area, _snap, _f7, _s);
            y += area.Height + S(6);

            // legend
            float x = Lx;
            x = Swatch(g, x, y, ProjectionRenderer.ActualColor(_snap), "实际", false);
            x = Swatch(g, x + S(10), y, ProjectionRenderer.PaceColor, "匀速", false);
            Swatch(g, x + S(10), y, ProjectionRenderer.CeilingColor, "上限 100%", true);
            SizeF nm = g.MeasureString(_snap.SampleCount + " 个采样点", _f7);
            Str(g, _snap.SampleCount + " 个采样点", _f7, Theme.Faint, Rx - nm.Width, y);

            y += S(18);
            Line(g, y);
            return y + S(14);
        }

        private int PaintSeven(Graphics g, int y)
        {
            Snapshot s = _snap;
            Color txt = LevelText(s.SevenPct), fill = LevelFill(s.SevenPct);
            string head = "7 天窗口";
            if (s.SevenResetUtc > DateTime.MinValue) head += "  重置 " + Fmt.LocalDate(s.SevenResetUtc, "ddd HH:mm");
            Str(g, head, _f9b, Theme.Muted, Lx, y);
            string pct = P(s.SevenPct);
            SizeF pm = g.MeasureString(pct, _f9b);
            Str(g, pct, _f9b, txt, Rx - pm.Width, y);
            y += S(20);
            RoundBar(g, new Rectangle(Lx, y, ClientSize.Width - 2 * Lx, S(7)), s.SevenPct / 100.0, fill);
            y += S(13);
            if (s.SevenResetUtc > s.NowUtc)
                Str(g, Fmt.Duration(s.ToWeekReset) + "后重置", _f8, Theme.Muted, Lx, y);
            y += S(20);
            Line(g, y);
            return y + S(14);
        }

        private void PaintModels(Graphics g, int y)
        {
            Snapshot s = _snap;
            bool hasOpus = !double.IsNaN(s.OpusPct), hasSonnet = !double.IsNaN(s.SonnetPct);
            if (!hasOpus && !hasSonnet)
            {
                Str(g, "7 天分模型：本次读数未返回", _f7, Theme.Faint, Lx, y);
                return;
            }
            Str(g, "7 天 · 分模型", _f9b, Theme.Text, Lx, y);
            y += S(22);
            if (hasOpus) y = ModelRow(g, "Opus", s.OpusPct, y);
            if (hasSonnet) ModelRow(g, "Sonnet", s.SonnetPct, y);
        }

        private int ModelRow(Graphics g, string name, double pct100, int y)
        {
            Str(g, name, _f8, Theme.Text, Lx, y);
            string amt = P(pct100);
            SizeF am = g.MeasureString(amt, _f8);
            Str(g, amt, _f8, Theme.Muted, Rx - am.Width, y);
            var track = new Rectangle(Lx + S(70), y + S(6), ClientSize.Width - Lx - S(70) - Lx - (int)am.Width - S(10), S(5));
            if (track.Width > S(10)) RoundBar(g, track, pct100 / 100.0, LevelFill(pct100));
            return y + S(20);
        }

        private void PaintFooter(Graphics g)
        {
            Snapshot s = _snap;
            int y = ClientSize.Height - S(24);
            Line(g, y - S(8));
            Str(g, "官方接口 · " + Fmt.LocalTime(s.ApiLiveUtc) + " 读取", _f7, Theme.Faint, Lx, y);
            string right = s.SampleCount + " 条本地历史";
            SizeF m = g.MeasureString(right, _f7);
            Str(g, right, _f7, Theme.Faint, Rx - m.Width, y);
        }

        // ---- primitives --------------------------------------------------------------

        private void Str(Graphics g, string s, Font f, Color c, float x, float y)
        { using (var b = new SolidBrush(c)) g.DrawString(s, f, b, x, y); }

        private void CenterStr(Graphics g, string s, Font f, Color c, int cx, float y)
        { SizeF m = g.MeasureString(s, f); Str(g, s, f, c, cx - m.Width / 2f, y); }

        private void Line(Graphics g, int y)
        { using (var p = new Pen(Theme.Divider)) g.DrawLine(p, Lx, y, Rx, y); }

        private float Swatch(Graphics g, float x, int y, Color c, string label, bool dashed)
        {
            var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(c, 2.4f * _s)) { if (dashed) p.DashStyle = DashStyle.Dash; g.DrawLine(p, x, y + S(7), x + S(13), y + S(7)); }
            g.SmoothingMode = sm;
            Str(g, label, _f7, Theme.Muted, x + S(15), y);
            return x + S(15) + g.MeasureString(label, _f7).Width;
        }

        private void RoundBar(Graphics g, Rectangle r, double frac, Color fill)
        {
            float rad = r.Height / 2f;
            Theme.FillRound(g, r, rad, Theme.Track);
            if (frac <= 0 || double.IsNaN(frac)) return;
            double f = Math.Min(1.0, frac);
            int w = (int)Math.Round(r.Width * f);
            if (w < r.Height) w = r.Height;
            Theme.FillRound(g, new Rectangle(r.X, r.Y, w, r.Height), rad, fill);
            if (frac > 1.0)
                using (var hb = new HatchBrush(HatchStyle.WideUpwardDiagonal, Theme.WarnFill, fill))
                using (var path = Theme.RoundRect(r, rad))
                { var sm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias; g.FillPath(hb, path); g.SmoothingMode = sm; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _f7.Dispose(); _f8.Dispose(); _f9.Dispose(); _f9b.Dispose(); _f11b.Dispose(); _f26b.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
