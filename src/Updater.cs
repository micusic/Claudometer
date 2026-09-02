using System;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace TokenMeter
{
    /// <summary>
    /// Self-update from GitHub Releases. The app is a single self-contained exe, so an update is
    /// just: fetch the latest release, compare versions, download the new exe, swap it in, restart.
    /// The download is written by our own FileStream (no Mark-of-the-Web), so the swapped exe runs
    /// without a fresh SmartScreen prompt.
    /// </summary>
    public static class Updater
    {
        public const string Version = "1.2.0";
        private const string Repo = "micusic/Claudometer";
        private const string AssetName = "Claudometer.exe";
        private const string UA = "Claudometer-updater";

        static Updater()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch (Exception) { }
        }

        public class Info { public string Version; public string Url; public long Size; }

        /// <summary>The latest release's info (version + exe asset), regardless of comparison; null on failure.</summary>
        public static Info Latest()
        {
            try
            {
                string json = GetString("https://api.github.com/repos/" + Repo + "/releases/latest");
                if (json == null) return null;
                string tag = JsonPeek.Str(json, 0, "tag_name");
                if (string.IsNullOrEmpty(tag)) return null;
                long size;
                string url = FindAssetUrl(json, out size);
                if (url == null) return null;
                return new Info { Version = tag.TrimStart('v', 'V'), Url = url, Size = size };
            }
            catch (Exception) { return null; }
        }

        /// <summary>Returns release info if a newer version is available, else null.</summary>
        public static Info Check()
        {
            Info info = Latest();
            if (info == null || !IsNewer(info.Version, Version)) return null;
            return info;
        }

        /// <summary>Downloads and swaps in the new exe. On success the caller should start the new
        /// exe (returned path) and exit; on failure returns false with a reason.</summary>
        public static bool Apply(Info info, out string newExePath, out string error)
        {
            newExePath = Application.ExecutablePath;
            error = null;
            try
            {
                byte[] data = GetBytes(info.Url);
                if (data == null || data.Length < 40000 || data[0] != (byte)'M' || data[1] != (byte)'Z')
                { error = "downloaded file was not a valid exe"; return false; }

                string exe = Application.ExecutablePath;
                string dir = Path.GetDirectoryName(exe);
                string staged = Path.Combine(dir, AssetName + ".new");
                string old = exe + ".old";

                File.WriteAllBytes(staged, data);          // our own write: no Mark-of-the-Web
                if (File.Exists(old)) { try { File.Delete(old); } catch (Exception) { } }
                File.Move(exe, old);                       // renaming a running exe is allowed on Windows
                File.Move(staged, exe);
                newExePath = exe;
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>Delete the leftover previous exe after a successful swap (call on startup).</summary>
        public static void CleanupOld()
        {
            try
            {
                string old = Application.ExecutablePath + ".old";
                if (File.Exists(old)) File.Delete(old);
                string staged = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), AssetName + ".new");
                if (File.Exists(staged)) File.Delete(staged);
            }
            catch (Exception) { }
        }

        // ---- helpers -----------------------------------------------------------------

        /// <summary>"1.2.10" &gt; "1.2.2": dotted numeric comparison, missing parts are 0.</summary>
        public static bool IsNewer(string a, string b)
        {
            int[] pa = Parts(a), pb = Parts(b);
            for (int i = 0; i < 4; i++)
            {
                int x = i < pa.Length ? pa[i] : 0, y = i < pb.Length ? pb[i] : 0;
                if (x != y) return x > y;
            }
            return false;
        }

        private static int[] Parts(string v)
        {
            if (string.IsNullOrEmpty(v)) return new int[0];
            int dash = v.IndexOf('-'); if (dash >= 0) v = v.Substring(0, dash);   // drop pre-release suffix
            string[] s = v.Split('.');
            var p = new int[s.Length];
            for (int i = 0; i < s.Length; i++) int.TryParse(s[i], out p[i]);
            return p;
        }

        private static string FindAssetUrl(string json, out long size)
        {
            size = 0;
            // scan for "name":"Claudometer.exe" then the nearby browser_download_url
            int i = 0;
            while (true)
            {
                int at = json.IndexOf("\"name\"", i);
                if (at < 0) return null;
                string name = JsonPeek.Str(json, FindEnclosingObject(json, at), "name");
                if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase))
                {
                    int obj = FindEnclosingObject(json, at);
                    size = JsonPeek.Int(json, obj, "size", 0);
                    string url = JsonPeek.Str(json, obj, "browser_download_url");
                    if (!string.IsNullOrEmpty(url)) return url;
                }
                i = at + 6;
            }
        }

        /// <summary>Index of the '{' that opens the object containing position <paramref name="pos"/>.</summary>
        private static int FindEnclosingObject(string s, int pos)
        {
            int depth = 0;
            for (int i = pos; i >= 0; i--)
            {
                char c = s[i];
                if (c == '}') depth++;
                else if (c == '{') { if (depth == 0) return i; depth--; }
            }
            return 0;
        }

        private static string GetString(string url)
        {
            byte[] b = GetBytes(url);
            return b == null ? null : Encoding.UTF8.GetString(b);
        }

        private static byte[] GetBytes(string url)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = UA;
                req.Accept = "application/vnd.github+json, application/octet-stream";
                req.Headers["X-GitHub-Api-Version"] = "2022-11-28";
                req.AllowAutoRedirect = true;
                req.Timeout = 60000;
                using (var res = (HttpWebResponse)req.GetResponse())
                using (var ms = new MemoryStream())
                {
                    res.GetResponseStream().CopyTo(ms);
                    return ms.ToArray();
                }
            }
            catch (Exception) { return null; }
        }
    }
}
