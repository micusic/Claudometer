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

            string verifier = OAuth.NewVerifier();
            string state = OAuth.NewState();
            W("");
            W("在浏览器打开下面的链接，登录并同意，然后把页面给出的 code 粘回这里：");
            W("");
            W(OAuth.BuildAuthorizeUrl(verifier, state));
            W("");
            Console.Out.Write("code: "); Console.Out.Flush();
            string pasted = Console.In.ReadLine();

            string err;
            TokenSet t = OAuth.Exchange(pasted, verifier, state, out err);
            if (t == null) { W("登录失败：" + err); return; }

            UsageReading reading; string msg;
            UsageApi.Status st = UsageApi.Fetch(t.AccessToken, out reading, out msg);
            if (st == UsageApi.Status.Unauthorized) { W("令牌被拒：" + msg); return; }

            OAuth.Save(t);
            W("");
            W("登录成功，令牌已加密保存到 " + OAuth.TokenPath);
            if (st == UsageApi.Status.Ok && reading != null) PrintReading(reading);
            else W("（用量接口暂时不可用：" + msg + "，令牌有效，托盘会稍后重试）");
            W("");
        }

        /// <summary>`--api` - print the current usage using the stored token, and record it to history.</summary>
        public static void ApiStatus()
        {
            AppConfig cfg = AppConfig.Load();
            Tz.Use(cfg.TimeZoneId);
            TokenSet t = OAuth.Load();
            if (t == null) { W("未登录。先运行 TokenMeter.exe --login。"); return; }
            if (t.Expired)
            {
                string rerr; TokenSet fresh = OAuth.Refresh(t.RefreshToken, out rerr);
                if (fresh != null) { t = fresh; OAuth.Save(t); }
                else { W("令牌过期且刷新失败：" + rerr); return; }
            }
            UsageReading r; string msg;
            UsageApi.Status st = UsageApi.Fetch(t.AccessToken, out r, out msg);
            if (st != UsageApi.Status.Ok) { W("用量接口：" + st + " " + msg); return; }
            PrintReading(r);
            var h = new History(); h.Load(); h.Add(UsageSample.From(r)); h.Save();
        }

        private static void PrintReading(UsageReading r)
        {
            W("");
            W("官方用量（api/oauth/usage）：");
            if (r.FiveHour.HasValue)
                W("  5 小时  " + r.FiveHour.Utilization.ToString("0") + "%   重置 " + Fmt.LocalDate(r.FiveHour.ResetUtc, "MM-dd HH:mm"));
            if (r.SevenDay.HasValue)
                W("  7 天    " + r.SevenDay.Utilization.ToString("0") + "%   重置 " + Fmt.LocalDate(r.SevenDay.ResetUtc, "ddd HH:mm"));
            if (r.SevenDayOpus.HasValue)
                W("  周·Opus   " + r.SevenDayOpus.Utilization.ToString("0") + "%");
            if (r.SevenDaySonnet.HasValue)
                W("  周·Sonnet " + r.SevenDaySonnet.Utilization.ToString("0") + "%");
        }

        /// <summary>`--snapshot out.png` renders the panel from local history (no live poll).</summary>
        public static void Snapshot(string path)
        {
            if (string.IsNullOrEmpty(path)) path = "panel.png";
            AppConfig cfg = AppConfig.Load();
            Tz.Use(cfg.TimeZoneId);
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
