using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace TokenMeter
{
    /// <summary>Terminal entry points: login, a text usage readout, and PNG renders for layout review.</summary>
    public static class Report
    {
        /// <summary>`--login` - the OAuth flow from a terminal.</summary>
        public static void Login()
        {
            AppConfig cfg = AppConfig.Load();
            Tz.Use(cfg.TimeZoneId);

            L.Use(cfg.Language);
            string verifier = OAuth.NewVerifier();
            string state = OAuth.NewState();
            W("");
            W("Open this link in your browser, sign in and consent, then paste the code back here:");
            W("");
            W(OAuth.BuildAuthorizeUrl(verifier, state));
            W("");
            Console.Out.Write("code: "); Console.Out.Flush();
            string pasted = Console.In.ReadLine();

            string err;
            TokenSet t = OAuth.Exchange(pasted, verifier, state, out err);
            if (t == null) { W("Sign-in failed: " + err); return; }

            UsageReading reading; string msg;
            UsageApi.Status st = UsageApi.Fetch(t.AccessToken, out reading, out msg);
            if (st == UsageApi.Status.Unauthorized) { W("Token rejected: " + msg); return; }

            OAuth.Save(t);
            W("");
            W("Signed in. Token stored encrypted at " + OAuth.TokenPath);
            if (st == UsageApi.Status.Ok && reading != null) PrintReading(reading);
            else W("(usage API unavailable for now: " + msg + "; the token is valid, the tray will retry)");
            W("");
        }

        /// <summary>`--update-check` - print the latest release and whether it's newer (no apply).</summary>
        public static void UpdateCheck()
        {
            W("current : v" + Updater.Version);
            Updater.Info latest = Updater.Latest();
            if (latest == null) { W("latest  : (could not reach GitHub Releases)"); return; }
            W("latest  : v" + latest.Version + "  (" + latest.Size + " bytes)");
            W("asset   : " + latest.Url);
            W("newer?  : " + (Updater.IsNewer(latest.Version, Updater.Version) ? "YES — would update" : "no — up to date"));
        }

        /// <summary>`--api` - print the current usage using the stored token, and record it to history.</summary>
        public static void ApiStatus()
        {
            AppConfig cfg = AppConfig.Load();
            Tz.Use(cfg.TimeZoneId);
            L.Use(cfg.Language);
            TokenSet t = OAuth.Load();
            if (t == null) { W("Not signed in. Run Claudometer.exe --login first."); return; }
            if (t.Expired)
            {
                string rerr; TokenSet fresh = OAuth.Refresh(t.RefreshToken, out rerr);
                if (fresh != null) { t = fresh; OAuth.Save(t); }
                else { W("Token expired and refresh failed: " + rerr); return; }
            }
            UsageReading r; string msg;
            UsageApi.Status st = UsageApi.Fetch(t.AccessToken, out r, out msg);
            if (st != UsageApi.Status.Ok) { W("Usage API: " + st + " " + msg); return; }
            PrintReading(r);
            var h = new History(); h.Load(); h.Add(UsageSample.From(r)); h.Save();
        }

        private static void PrintReading(UsageReading r)
        {
            W("");
            W("Usage (api/oauth/usage):");
            if (r.FiveHour.HasValue)
                W("  5-hour  " + r.FiveHour.Utilization.ToString("0") + "%   resets " + Fmt.LocalDate(r.FiveHour.ResetUtc, "MM-dd HH:mm"));
            if (r.SevenDay.HasValue)
                W("  7-day   " + r.SevenDay.Utilization.ToString("0") + "%   resets " + Fmt.LocalDate(r.SevenDay.ResetUtc, "ddd HH:mm"));
            if (r.SevenDayOpus.HasValue)
                W("  wk Opus   " + r.SevenDayOpus.Utilization.ToString("0") + "%");
            if (r.SevenDaySonnet.HasValue)
                W("  wk Sonnet " + r.SevenDaySonnet.Utilization.ToString("0") + "%");
        }

        /// <summary>`--snapshot out.png` renders the panel from local history (no live poll).</summary>
        public static void Snapshot(string path)
        {
            if (string.IsNullOrEmpty(path)) path = "panel.png";
            AppConfig cfg = AppConfig.Load();
            Tz.Use(cfg.TimeZoneId);
            L.Use(cfg.Language);
            Theme.Apply(cfg.ThemeMode);
            var hist = new History(); hist.Load();
            bool loggedIn = OAuth.Load() != null;
            Snapshot snap = Analytics.Build(hist, loggedIn, "", DateTime.UtcNow);

            using (var f = new PanelForm())
            {
                f.Update(snap, cfg);
                IntPtr force = f.Handle; GC.KeepAlive(force);
                using (var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.ClientSize.Width, f.ClientSize.Height));
                    bmp.Save(path, ImageFormat.Png);
                }
                AttachAndSay("wrote " + path + "  (" + f.ClientSize.Width + "x" + f.ClientSize.Height + ")");
            }
        }

        /// <summary>`--snapdlg settings|login out.png` renders a dialog for layout review.</summary>
        public static void SnapshotDialog(string which, string path)
        {
            if (string.IsNullOrEmpty(path)) path = "dialog.png";
            AppConfig cfg = AppConfig.Load();
            Tz.Use(cfg.TimeZoneId);
            L.Use(cfg.Language);
            Theme.Apply(cfg.ThemeMode);

            Form f = (which ?? "").ToLowerInvariant() == "login" ? (Form)new LoginForm() : new SettingsForm(cfg);
            using (f)
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-4000, -4000);
                f.ShowInTaskbar = false;
                f.Show();
                for (int i = 0; i < 8; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(30); }
                using (var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.ClientSize.Width, f.ClientSize.Height));
                    bmp.Save(path, ImageFormat.Png);
                }
                f.Hide();
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int pid);
        private static void AttachAndSay(string s) { AttachConsole(-1); W(s); }
        private static void W(string s) { try { Console.WriteLine(s); } catch (Exception) { } }
    }
}
