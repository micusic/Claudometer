using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TokenMeter
{
    /// <summary>
    /// Settings, stored as a flat JSON object in %APPDATA%\TokenMeter\config.json.
    ///
    /// The app is API-only: every number shown comes from Anthropic's usage endpoint after login.
    /// Nothing here is a budget or a calibration - those concepts are gone. This is just
    /// preferences (thresholds, poll cadence, timezone, theme).
    /// </summary>
    public class AppConfig
    {
        public double WarnPct = 0.70;     // fraction 0..1, compared against the API utilization
        public double DangerPct = 0.90;
        public int PollSeconds = 90;      // how often to poll the usage API (min 60 to be gentle)
        public bool Notify = true;
        public string TimeZoneId = Tz.DefaultId;
        public string ThemeMode = "light";   // "light" | "dark"

        public static string Dir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Claudometer");
            }
        }

        public static string ConfigPath { get { return Path.Combine(Dir, "config.json"); } }

        /// <summary>
        /// Carry a pre-rename install forward: if data still lives in the old %APPDATA%\TokenMeter
        /// folder and the new one doesn't exist yet, move it - so the token and history survive the
        /// rename and nobody has to log in again.
        /// </summary>
        public static void MigrateLegacy()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string legacy = Path.Combine(appData, "TokenMeter");
                if (Directory.Exists(legacy) && !Directory.Exists(Dir))
                    Directory.Move(legacy, Dir);
            }
            catch (Exception) { }
        }

        public static AppConfig Load()
        {
            MigrateLegacy();
            var c = new AppConfig();
            try
            {
                if (!File.Exists(ConfigPath)) return c;
                Dictionary<string, string> kv = ParseFlat(File.ReadAllText(ConfigPath, Encoding.UTF8));
                c.WarnPct = Num(kv, "warnPct", c.WarnPct);
                c.DangerPct = Num(kv, "dangerPct", c.DangerPct);
                c.PollSeconds = (int)Num(kv, "pollSeconds", c.PollSeconds);
                c.Notify = Bool(kv, "notify", c.Notify);
                string tz;
                if (kv.TryGetValue("timeZoneId", out tz) && !string.IsNullOrEmpty(tz)) c.TimeZoneId = Unescape(tz);
                string theme;
                if (kv.TryGetValue("theme", out theme) && !string.IsNullOrEmpty(theme)) c.ThemeMode = Unescape(theme);
            }
            catch (Exception) { /* a broken config falls back to defaults rather than blocking startup */ }
            c.Clamp();
            return c;
        }

        public void Clamp()
        {
            if (WarnPct < 0.05) WarnPct = 0.05;
            if (WarnPct > 0.98) WarnPct = 0.98;
            if (DangerPct <= WarnPct) DangerPct = Math.Min(0.99, WarnPct + 0.05);
            if (PollSeconds < 60) PollSeconds = 60;
            if (PollSeconds > 900) PollSeconds = 900;
        }

        public void Save()
        {
            Clamp();
            Directory.CreateDirectory(Dir);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"warnPct\": " + F(WarnPct) + ",");
            sb.AppendLine("  \"dangerPct\": " + F(DangerPct) + ",");
            sb.AppendLine("  \"pollSeconds\": " + PollSeconds + ",");
            sb.AppendLine("  \"notify\": " + (Notify ? "true" : "false") + ",");
            sb.AppendLine("  \"timeZoneId\": \"" + Escape(TimeZoneId ?? Tz.DefaultId) + "\",");
            sb.AppendLine("  \"theme\": \"" + Escape(ThemeMode ?? "light") + "\"");
            sb.AppendLine("}");
            string tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
            File.Move(tmp, ConfigPath);
        }

        private static string F(double d) { return d.ToString("0.####", CultureInfo.InvariantCulture); }

        private static double Num(Dictionary<string, string> kv, string key, double dflt)
        {
            string v;
            if (!kv.TryGetValue(key, out v)) return dflt;
            double d;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : dflt;
        }

        private static bool Bool(Dictionary<string, string> kv, string key, bool dflt)
        {
            string v;
            if (!kv.TryGetValue(key, out v)) return dflt;
            return v == "true";
        }

        private static Dictionary<string, string> ParseFlat(string json)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            while (i < json.Length)
            {
                int ks = json.IndexOf('"', i);
                if (ks < 0) break;
                int ke = ks + 1;
                while (ke < json.Length && json[ke] != '"') { if (json[ke] == '\\') ke++; ke++; }
                if (ke >= json.Length) break;
                string key = json.Substring(ks + 1, ke - ks - 1);

                int c = json.IndexOf(':', ke);
                if (c < 0) break;
                int v = c + 1;
                while (v < json.Length && char.IsWhiteSpace(json[v])) v++;
                if (v >= json.Length) break;

                if (json[v] == '"')
                {
                    int ve = v + 1;
                    while (ve < json.Length && json[ve] != '"') { if (json[ve] == '\\') ve++; ve++; }
                    d[key] = json.Substring(v + 1, Math.Max(0, ve - v - 1));
                    i = ve + 1;
                }
                else
                {
                    int ve = v;
                    while (ve < json.Length && json[ve] != ',' && json[ve] != '}' && json[ve] != '\n') ve++;
                    d[key] = json.Substring(v, ve - v).Trim();
                    i = ve;
                }
            }
            return d;
        }

        private static string Escape(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\""); }
        private static string Unescape(string s) { return s.Replace("\\\\", "\\").Replace("\\\"", "\""); }
    }
}
