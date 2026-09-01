using System;
using System.IO;
using System.Net;
using System.Text;

namespace TokenMeter
{
    /// <summary>One window's true state, straight from Anthropic.</summary>
    public class ApiWindow
    {
        public double Utilization;   // 0..100, or NaN when the window is inactive/absent
        public DateTime ResetUtc;

        public bool HasValue { get { return !double.IsNaN(Utilization); } }
    }

    /// <summary>The authoritative usage reading. What Claude Code's /usage panel shows.</summary>
    public class UsageReading
    {
        public ApiWindow FiveHour = new ApiWindow();
        public ApiWindow SevenDay = new ApiWindow();
        public ApiWindow SevenDayOpus = new ApiWindow();
        public ApiWindow SevenDaySonnet = new ApiWindow();
        public DateTime FetchedUtc;
    }

    /// <summary>
    /// Reads the authoritative usage endpoint: the real percentages and reset instants, the same
    /// numbers as /usage. It is rate-limited, so callers poll it sparingly; a distinct User-Agent
    /// (see OAuth) keeps requests off the shared bucket.
    /// </summary>
    public static class UsageApi
    {
        public const string Url = "https://api.anthropic.com/api/oauth/usage";

        public enum Status { Ok, Unauthorized, RateLimited, Error }

        public static Status Fetch(string accessToken, out UsageReading reading, out string message)
        {
            reading = null;
            message = null;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(Url);
                req.Method = "GET";
                req.Accept = "application/json";
                req.ContentType = "application/json";
                req.UserAgent = OAuth.UserAgent;                 // distinct UA, see OAuth.UserAgent
                req.Headers["Authorization"] = "Bearer " + accessToken;
                req.Headers["anthropic-beta"] = "oauth-2025-04-20";
                req.Timeout = 20000;

                using (var res = (HttpWebResponse)req.GetResponse())
                {
                    string body = ReadAll(res);
                    reading = Parse(body);
                    return Status.Ok;
                }
            }
            catch (WebException wex)
            {
                var res = wex.Response as HttpWebResponse;
                if (res != null)
                {
                    int code = (int)res.StatusCode;
                    message = "HTTP " + code + " " + Trim(ReadAll(res));
                    if (code == 401 || code == 403) return Status.Unauthorized;
                    if (code == 429) return Status.RateLimited;
                    return Status.Error;
                }
                message = wex.Message;
                return Status.Error;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Status.Error;
            }
        }

        private static UsageReading Parse(string json)
        {
            var r = new UsageReading();
            r.FetchedUtc = DateTime.UtcNow;
            Window(json, "five_hour", r.FiveHour);
            Window(json, "seven_day", r.SevenDay);
            Window(json, "seven_day_opus", r.SevenDayOpus);
            Window(json, "seven_day_sonnet", r.SevenDaySonnet);
            return r;
        }

        private static void Window(string json, string key, ApiWindow w)
        {
            int obj = JsonPeek.Obj(json, 0, key);
            if (obj < 0) { w.Utilization = double.NaN; return; }   // present as null -> not an object
            w.Utilization = JsonPeek.Num(json, obj, "utilization");
            string reset = JsonPeek.Str(json, obj, "resets_at");
            DateTime t;
            if (JsonPeek.TryTime(reset, out t)) w.ResetUtc = t;
        }

        private static string ReadAll(HttpWebResponse res)
        {
            try
            {
                using (Stream s = res.GetResponseStream())
                using (var rd = new StreamReader(s, Encoding.UTF8))
                    return rd.ReadToEnd();
            }
            catch (Exception) { return ""; }
        }

        private static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " ").Replace("\r", " ");
            return s.Length > 160 ? s.Substring(0, 160) : s;
        }
    }
}
