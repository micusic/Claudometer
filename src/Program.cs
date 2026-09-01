using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TokenMeter
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);

        [STAThread]
        private static void Main(string[] args)
        {
            if (Has(args, "--login")) { AttachConsole(-1); Report.Login(); return; }
            if (Has(args, "--api")) { AttachConsole(-1); Report.ApiStatus(); return; }
            if (Has(args, "--snapshot"))
            {
                try { SetProcessDPIAware(); } catch (Exception) { }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Report.Snapshot(Arg(args, "--snapshot")); return;
            }
            if (Has(args, "--snapdlg"))
            {
                try { SetProcessDPIAware(); } catch (Exception) { }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Report.SnapshotDialog(Arg(args, "--snapdlg"), Arg2(args, "--snapdlg")); return;
            }

            bool show = Has(args, "--show");
            bool created;
            using (var mutex = new Mutex(true, "Claudometer.SingleInstance.v1", out created))
            {
                if (!created)
                {
                    AppConfig probe = AppConfig.Load(); L.Use(probe.Language);
                    MessageBox.Show(L.S("instance.running"), "Claudometer",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try { SetProcessDPIAware(); } catch (Exception) { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp(show));
                GC.KeepAlive(mutex);
            }
        }

        private static bool Has(string[] a, string f)
        { if (a == null) return false; foreach (string x in a) if (string.Equals(x, f, StringComparison.OrdinalIgnoreCase)) return true; return false; }
        private static string Arg(string[] a, string f)
        { for (int i = 0; i < a.Length - 1; i++) if (string.Equals(a[i], f, StringComparison.OrdinalIgnoreCase)) return a[i + 1]; return null; }
        private static string Arg2(string[] a, string f)
        { for (int i = 0; i < a.Length - 2; i++) if (string.Equals(a[i], f, StringComparison.OrdinalIgnoreCase)) return a[i + 2]; return null; }
    }

    /// <summary>
    /// Tray owner. API-only: it holds a login token, polls the usage endpoint on a gentle cadence,
    /// stores each reading to local history, and paints the panel from that history. No transcript
    /// scanning, no calibration, no projection.
    /// </summary>
    internal class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon _tray = new NotifyIcon();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private readonly PanelForm _panel = new PanelForm();
        private ToolStripMenuItem _loginItem, _logoutItem;
        private readonly object _gate = new object();

        private AppConfig _cfg;
        private readonly History _history = new History();
        private TokenSet _token;
        private Snapshot _snap;
        private Icon _icon;
        private bool _showOnReady;

        private bool _polling;
        private DateTime _lastPollUtc = DateTime.MinValue;
        private int _backoffSec;
        private string _apiStatus = "";

        // Alerts, re-armed when the window resets or usage falls back below the warn line.
        private DateTime _alertWindowReset = DateTime.MinValue;
        private bool _sentWarn, _sentDanger, _sentOver;

        public TrayApp(bool showOnReady)
        {
            _showOnReady = showOnReady;
            _cfg = AppConfig.Load();
            L.Use(_cfg.Language);
            Tz.Use(_cfg.TimeZoneId);
            Theme.Apply(_cfg.ThemeMode);
            _token = OAuth.Load();
            _backoffSec = _cfg.PollSeconds;
            _history.Load();

            BuildTray();
            _panel.RefreshRequested += delegate { PollNow(); };
            _panel.SettingsRequested += delegate { OpenSettings(); };
            _panel.LoginRequested += delegate { DoLogin(); };
            var force = _panel.Handle;

            _timer.Interval = 15000;   // UI tick (countdown) + poll-if-due
            _timer.Tick += delegate { Tick(); };
            _timer.Start();

            Rebuild();
            if (_token != null) PollNow();
        }

        private void BuildTray()
        {
            RebuildMenu();
            _tray.Text = "Claudometer";
            _tray.Visible = true;
            SetIcon(0, IconRenderer.IdleGray, false);
            _tray.MouseClick += OnTrayClick;
        }

        private void RebuildMenu()
        {
            var old = _tray.ContextMenuStrip;
            var menu = new ContextMenuStrip();
            menu.Items.Add(L.S("menu.panel"), null, delegate { TogglePanel(); });
            menu.Items.Add(L.S("menu.refresh"), null, delegate { PollNow(); });
            menu.Items.Add(new ToolStripSeparator());
            _loginItem = new ToolStripMenuItem(L.S("menu.login"), null, delegate { DoLogin(); });
            _logoutItem = new ToolStripMenuItem(L.S("menu.logout"), null, delegate { DoLogout(); });
            menu.Items.Add(_loginItem);
            menu.Items.Add(_logoutItem);
            menu.Items.Add(L.S("menu.settings"), null, delegate { OpenSettings(); });
            menu.Items.Add(L.S("menu.datadir"), null, delegate { OpenFolder(AppConfig.Dir); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.S("menu.about"), null, delegate { About(); });
            menu.Items.Add(L.S("menu.quit"), null, delegate { Quit(); });
            _tray.ContextMenuStrip = menu;
            UpdateLoginMenu();
            if (old != null) old.Dispose();
        }

        private void UpdateLoginMenu()
        {
            bool inl = _token != null;
            if (_loginItem != null) _loginItem.Text = inl ? L.S("menu.relogin") : L.S("menu.login");
            if (_logoutItem != null) _logoutItem.Visible = inl;
        }

        private void OnTrayClick(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) TogglePanel(); }

        private void TogglePanel()
        {
            if (_panel.Visible) _panel.Hide();
            else { _panel.Update(_snap, _cfg); _panel.ShowNearTray(); }
        }

        // ---- polling -----------------------------------------------------------------

        private void Tick()
        {
            if (_token != null && (DateTime.UtcNow - _lastPollUtc).TotalSeconds >= _backoffSec) PollNow();
            else { Rebuild(); ApplySnapshot(); }   // keep the countdown live between polls
        }

        private void PollNow()
        {
            if (_token == null) { Rebuild(); ApplySnapshot(); return; }
            if (_polling) return;
            _polling = true;
            _lastPollUtc = DateTime.UtcNow;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { DoPoll(); } catch (Exception) { }
                try { _panel.BeginInvoke((MethodInvoker)delegate { _polling = false; Rebuild(); ApplySnapshot(); }); }
                catch (Exception) { _polling = false; }
            });
        }

        private void DoPoll()
        {
            if (_token.Expired)
            {
                string rerr;
                TokenSet fresh = OAuth.Refresh(_token.RefreshToken, out rerr);
                if (fresh != null) { _token = fresh; OAuth.Save(_token); }
                else { _apiStatus = L.S("status.tokenexpired"); return; }
            }

            UsageReading reading; string msg;
            UsageApi.Status st = UsageApi.Fetch(_token.AccessToken, out reading, out msg);
            if (st == UsageApi.Status.Unauthorized)
            {
                string rerr;
                TokenSet fresh = OAuth.Refresh(_token.RefreshToken, out rerr);
                if (fresh != null) { _token = fresh; OAuth.Save(_token); st = UsageApi.Fetch(_token.AccessToken, out reading, out msg); }
            }

            switch (st)
            {
                case UsageApi.Status.Ok:
                    _backoffSec = _cfg.PollSeconds;
                    _apiStatus = "";
                    lock (_gate) { _history.Add(UsageSample.From(reading)); _history.Save(); }
                    break;
                case UsageApi.Status.RateLimited:
                    _backoffSec = Math.Min(900, Math.Max(_cfg.PollSeconds, _backoffSec) * 2);
                    _apiStatus = L.F("status.ratelimited", _backoffSec / 60);
                    break;
                case UsageApi.Status.Unauthorized:
                    _apiStatus = L.S("status.unauth");
                    break;
                default:
                    _apiStatus = L.S("status.error");
                    break;
            }
        }

        private void Rebuild()
        {
            lock (_gate) _snap = Analytics.Build(_history, _token != null, _apiStatus, DateTime.UtcNow);
        }

        private void ApplySnapshot()
        {
            if (_snap == null) return;
            if (!_snap.LoggedIn) { SetIcon(0, IconRenderer.IdleGray, false); _tray.Text = L.S("tray.notloggedin"); }
            else if (!_snap.HasData) { SetIcon(0, IconRenderer.IdleGray, false); _tray.Text = L.S("tray.fetching"); }
            else
            {
                double frac = double.IsNaN(_snap.FivePct) ? 0 : _snap.FivePct / 100.0;
                Color c = IconRenderer.LevelColor(frac, _cfg.WarnPct, _cfg.DangerPct);
                SetIcon(frac, c, _snap.FivePct >= _cfg.DangerPct * 100);
                _tray.Text = Tooltip(_snap);
            }
            if (_panel.Visible) _panel.Update(_snap, _cfg);
            Notify(_snap);
            OnReadyShow();
        }

        private string Tooltip(Snapshot s)
        {
            string t = "5h " + (int)Math.Round(s.FivePct) + "%";
            if (s.FiveResetUtc > s.NowUtc) t += " ↻" + Fmt.Duration(s.ToReset);
            if (!double.IsNaN(s.SevenPct)) t += Environment.NewLine + "7d " + (int)Math.Round(s.SevenPct) + "%";
            return t.Length > 62 ? t.Substring(0, 62) : t;
        }

        private void SetIcon(double frac, Color c, bool alert)
        {
            Icon fresh = IconRenderer.Render(frac, c, alert);
            _tray.Icon = fresh;
            IconRenderer.Dispose(_icon);
            _icon = fresh;
        }

        private void Notify(Snapshot s)
        {
            if (!_cfg.Notify || !s.HasData || double.IsNaN(s.FivePct)) return;

            if (_alertWindowReset != s.FiveResetUtc)
            { _alertWindowReset = s.FiveResetUtc; _sentWarn = _sentDanger = _sentOver = false; }
            if (s.FivePct < _cfg.WarnPct * 100 * 0.85) { _sentWarn = _sentDanger = _sentOver = false; }

            if (!_sentOver && s.FivePct >= 100)
            {
                _sentOver = _sentDanger = _sentWarn = true;
                _tray.ShowBalloonTip(10000, L.S("balloon.full.title"), L.F("balloon.reset.body", Fmt.Duration(s.ToReset)), ToolTipIcon.Error);
            }
            else if (!_sentDanger && s.FivePct >= _cfg.DangerPct * 100)
            {
                _sentDanger = _sentWarn = true;
                _tray.ShowBalloonTip(10000, L.F("balloon.used.title", (int)Math.Round(s.FivePct)),
                    L.F("balloon.reset.body", Fmt.Duration(s.ToReset)), ToolTipIcon.Warning);
            }
            else if (!_sentWarn && s.FivePct >= _cfg.WarnPct * 100)
            {
                _sentWarn = true;
                _tray.ShowBalloonTip(8000, L.F("balloon.used.title", (int)Math.Round(s.FivePct)),
                    L.F("balloon.reset.body", Fmt.Duration(s.ToReset)), ToolTipIcon.Warning);
            }
        }

        // ---- login / menu ------------------------------------------------------------

        private void DoLogin()
        {
            _panel.SuppressAutoHide = true;
            try
            {
                using (var f = new LoginForm())
                {
                    if (f.ShowDialog() != DialogResult.OK || f.Result == null) return;
                    _token = f.Result;
                    _backoffSec = _cfg.PollSeconds;
                    _lastPollUtc = DateTime.MinValue;
                    UpdateLoginMenu();
                    _tray.ShowBalloonTip(6000, L.S("balloon.signedin.title"), L.S("balloon.signedin.body"), ToolTipIcon.Info);
                    PollNow();
                }
            }
            finally { _panel.SuppressAutoHide = false; }
        }

        private void DoLogout()
        {
            OAuth.Clear();
            _token = null;
            _apiStatus = "";
            UpdateLoginMenu();
            Rebuild(); ApplySnapshot();
            _tray.ShowBalloonTip(5000, L.S("balloon.signedout.title"), L.S("balloon.signedout.body"), ToolTipIcon.Info);
        }

        private void OnReadyShow()
        {
            if (!_showOnReady) return;
            _showOnReady = false;
            _panel.SuppressAutoHide = true;
            _panel.Update(_snap, _cfg);
            _panel.ShowNearTray();
        }

        private void OpenSettings()
        {
            _panel.SuppressAutoHide = true;
            try
            {
                using (var f = new SettingsForm(_cfg))
                {
                    if (f.ShowDialog() == DialogResult.OK)
                    {
                        L.Use(_cfg.Language);
                        Tz.Use(_cfg.TimeZoneId);
                        Theme.Apply(_cfg.ThemeMode);
                        _backoffSec = _cfg.PollSeconds;
                        RebuildMenu();
                        Rebuild(); ApplySnapshot();
                        if (_panel.Visible) _panel.Update(_snap, _cfg);
                    }
                }
            }
            finally { _panel.SuppressAutoHide = false; }
        }

        private static void OpenFolder(string path)
        {
            try { if (!Directory.Exists(path)) Directory.CreateDirectory(path); Process.Start("explorer.exe", "\"" + path + "\""); }
            catch (Exception) { }
        }

        private void About()
        {
            _panel.SuppressAutoHide = true;
            try
            {
                MessageBox.Show(L.F("about.body", _history.Samples.Count), L.S("about.title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally { _panel.SuppressAutoHide = false; }
        }

        private void Quit()
        {
            _timer.Stop();
            _tray.Visible = false;
            lock (_gate) _history.Save();
            IconRenderer.Dispose(_icon);
            _tray.Dispose();
            _panel.Dispose();
            ExitThread();
        }
    }
}
