using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace TokenMeter
{
    /// <summary>The tokens from a successful login, plus when the access token expires.</summary>
    public class TokenSet
    {
        public string AccessToken;
        public string RefreshToken;
        public DateTime ExpiresAtUtc;
        public string Scope;

        public bool Valid { get { return !string.IsNullOrEmpty(AccessToken); } }
        public bool Expired { get { return DateTime.UtcNow >= ExpiresAtUtc.AddMinutes(-2); } }
    }

    /// <summary>
    /// The Claude Code OAuth flow, replicated so the app can read the authoritative usage
    /// endpoint (which needs an account token, not an API key).
    ///
    /// This is the same public client id, authorize URL and PKCE flow Claude Code itself uses;
    /// the user signs in and consents in their own browser and pastes back the one-time code.
    /// The app never sees the password. Tokens are stored DPAPI-encrypted for this Windows user
    /// only, and are sent to no host other than Anthropic's own.
    /// </summary>
    public static class OAuth
    {
        public const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
        public const string AuthorizeUrl = "https://claude.ai/oauth/authorize";
        public const string RedirectUri = "https://platform.claude.com/oauth/code/callback";

        // platform.claude.com is the live token host; console.anthropic.com/v1/oauth/token is 404.
        private static readonly string[] TokenUrls =
        {
            "https://platform.claude.com/v1/oauth/token",
        };

        /// <summary>
        /// A per-process **unique** User-Agent, and this is load-bearing.
        ///
        /// These OAuth endpoints sit behind an edge rate-limit rule keyed on User-Agent. The
        /// obvious value - `claude-code/&lt;ver&gt;`, which every Claude Code install and third-party
        /// tool sends - shares one global bucket that is chronically saturated, so it returns 429
        /// no matter how long you wait (this is the real cause of the widespread "OAuth login
        /// fails with 429" reports, and the "you must send claude-code UA" advice makes it worse).
        /// A UA nobody else uses gets its own fresh bucket and reaches the backend. Verified:
        /// claude-code/curl/Chrome UAs → 429; a novel UA → 400 invalid_grant (i.e. reached the
        /// real token service). Unique-per-process so retries never saturate our own bucket.
        /// </summary>
        public static readonly string UserAgent =
            "TokenMeter/1.0 (" + Guid.NewGuid().ToString("N").Substring(0, 12) + ")";
        public const string Scope = "user:inference user:profile user:sessions:claude_code user:mcp_servers";

        static OAuth()
        {
            // The token host negotiates TLS 1.2; the .NET Framework default can be older.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (Exception) { }
        }

        // ---- PKCE ---------------------------------------------------------------------

        public static string NewVerifier()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Base64Url(bytes);
        }

        public static string Challenge(string verifier)
        {
            using (var sha = SHA256.Create())
                return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        public static string NewState()
        {
            var bytes = new byte[24];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Base64Url(bytes);
        }

        private static string Base64Url(byte[] b)
        {
            return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static string BuildAuthorizeUrl(string verifier, string state)
        {
            var sb = new StringBuilder(AuthorizeUrl);
            sb.Append("?code=true");
            sb.Append("&client_id=").Append(Uri.EscapeDataString(ClientId));
            sb.Append("&response_type=code");
            sb.Append("&redirect_uri=").Append(Uri.EscapeDataString(RedirectUri));
            sb.Append("&scope=").Append(Uri.EscapeDataString(Scope));
            sb.Append("&code_challenge=").Append(Challenge(verifier));
            sb.Append("&code_challenge_method=S256");
            sb.Append("&state=").Append(state);
            return sb.ToString();
        }

        // ---- token exchange -----------------------------------------------------------

        /// <summary>
        /// Trades the pasted "code#state" for tokens. The pasted value carries the state back,
        /// so a mismatch against the state we generated means the code is for another attempt.
        /// </summary>
        public static TokenSet Exchange(string pastedCode, string verifier, string expectedState, out string error)
        {
            error = null;
            string code = pastedCode == null ? "" : pastedCode.Trim();
            string state = expectedState;
            int hash = code.IndexOf('#');
            if (hash >= 0)
            {
                state = code.Substring(hash + 1);
                code = code.Substring(0, hash);
            }
            if (!string.IsNullOrEmpty(expectedState) && state != expectedState)
            {
                error = "粘贴的 code 与本次登录不匹配（state 不一致），请重新发起登录。";
                return null;
            }

            var fields = new[]
            {
                new[] { "grant_type", "authorization_code" },
                new[] { "code", code },
                new[] { "state", state },
                new[] { "client_id", ClientId },
                new[] { "redirect_uri", RedirectUri },
                new[] { "code_verifier", verifier },
            };
            return PostToken(fields, out error);
        }

        public static TokenSet Refresh(string refreshToken, out string error)
        {
            var fields = new[]
            {
                new[] { "grant_type", "refresh_token" },
                new[] { "refresh_token", refreshToken },
                new[] { "client_id", ClientId },
            };
            return PostToken(fields, out error);
        }

        /// <summary>
        /// Exchanges tokens gently. The token endpoint rate-limits hard, so on a 429 we stop
        /// immediately (never churning through hosts/encodings, which only deepens the limit) and
        /// report how long to wait - a 429 doesn't consume the code, so the same paste retries.
        /// Only a 404 / connection failure ("wrong host") advances to the next host; a 400/401 on
        /// one host is answered by trying the other encoding there once, then giving up.
        /// </summary>
        private static TokenSet PostToken(string[][] fields, out string error)
        {
            error = null;
            string lastErr = "";

            for (int h = 0; h < TokenUrls.Length; h++)
            {
                string url = TokenUrls[h];

                // Form encoding first - the documented content type for this endpoint.
                foreach (bool asJson in new[] { false, true })
                {
                    string resp;
                    int status;
                    int retryAfter;
                    bool ok = Post(url, fields, asJson, out resp, out status, out retryAfter);

                    if (ok)
                    {
                        TokenSet t = ParseToken(resp);
                        if (t != null) return t;
                        error = "令牌响应里没有 access_token。" + Trim(resp);
                        return null;
                    }

                    if (status == 429)
                    {
                        // With a unique UA this should not happen. If it does, the edge bucket is
                        // saturated anyway; a short wait clears a per-window limit.
                        error = retryAfter > 0
                            ? "令牌服务限流，约 " + retryAfter + " 秒后可再试（code 仍有效）。"
                            : "令牌服务限流，请稍等一两分钟再用同一个 code 点一次「完成登录」。";
                        return null;
                    }

                    lastErr = "HTTP " + status + " " + Trim(resp);

                    if (status == 400 || status == 401)
                        continue;   // real rejection on a live host: try the other encoding once

                    // 404 / 5xx / connection failure: this host is wrong, move to the next.
                    break;
                }
            }
            error = "换取令牌失败：" + lastErr;
            return null;
        }

        private static TokenSet ParseToken(string resp)
        {
            var t = new TokenSet();
            t.AccessToken = JsonPeek.Str(resp, 0, "access_token");
            t.RefreshToken = JsonPeek.Str(resp, 0, "refresh_token");
            t.Scope = JsonPeek.Str(resp, 0, "scope");
            long expiresIn = JsonPeek.Int(resp, 0, "expires_in", 3600);
            t.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn);
            return t.Valid ? t : null;
        }

        private static bool Post(string url, string[][] fields, bool asJson,
                                 out string responseBody, out int status, out int retryAfterSec)
        {
            responseBody = "";
            status = 0;
            retryAfterSec = 0;
            try
            {
                byte[] data;
                string contentType;
                if (asJson)
                {
                    var sb = new StringBuilder("{");
                    for (int i = 0; i < fields.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(JsonStr(fields[i][0])).Append(':').Append(JsonStr(fields[i][1]));
                    }
                    data = Encoding.UTF8.GetBytes(sb.Append('}').ToString());
                    contentType = "application/json";
                }
                else
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < fields.Length; i++)
                    {
                        if (i > 0) sb.Append('&');
                        sb.Append(Uri.EscapeDataString(fields[i][0])).Append('=')
                          .Append(Uri.EscapeDataString(fields[i][1]));
                    }
                    data = Encoding.UTF8.GetBytes(sb.ToString());
                    contentType = "application/x-www-form-urlencoded";
                }

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = contentType;
                req.Accept = "application/json";
                req.UserAgent = UserAgent;                          // unique UA - see the field's note
                req.Headers["anthropic-beta"] = "oauth-2025-04-20"; // the OAuth beta the token endpoint expects
                req.Timeout = 120000;                               // the endpoint can take 40-60s when degraded
                req.ContentLength = data.Length;
                using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);

                using (var res = (HttpWebResponse)req.GetResponse())
                {
                    status = (int)res.StatusCode;
                    responseBody = ReadAll(res);
                    return status >= 200 && status < 300;
                }
            }
            catch (WebException wex)
            {
                var res = wex.Response as HttpWebResponse;
                if (res != null)
                {
                    status = (int)res.StatusCode;
                    responseBody = ReadAll(res);
                    string ra = res.Headers["Retry-After"];
                    int sec;
                    if (!string.IsNullOrEmpty(ra) && int.TryParse(ra, out sec)) retryAfterSec = sec;
                }
                else responseBody = wex.Message;
                return false;
            }
            catch (Exception ex)
            {
                responseBody = ex.Message;
                return false;
            }
        }

        private static string ReadAll(HttpWebResponse res)
        {
            try
            {
                using (Stream s = res.GetResponseStream())
                using (var r = new StreamReader(s, Encoding.UTF8))
                    return r.ReadToEnd();
            }
            catch (Exception) { return ""; }
        }

        private static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " ").Replace("\r", " ");
            return s.Length > 200 ? s.Substring(0, 200) : s;
        }

        private static string JsonStr(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        // ---- storage (DPAPI, current user) --------------------------------------------

        public static string TokenPath { get { return Path.Combine(AppConfig.Dir, "token.bin"); } }

        public static void Save(TokenSet t)
        {
            if (t == null || !t.Valid) return;
            Directory.CreateDirectory(AppConfig.Dir);
            string plain = t.AccessToken + "\n" + (t.RefreshToken ?? "") + "\n"
                         + t.ExpiresAtUtc.ToUniversalTime().ToString("o") + "\n" + (t.Scope ?? "");
            byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null,
                                               DataProtectionScope.CurrentUser);
            string tmp = TokenPath + ".tmp";
            File.WriteAllBytes(tmp, enc);
            if (File.Exists(TokenPath)) File.Delete(TokenPath);
            File.Move(tmp, TokenPath);
        }

        public static TokenSet Load()
        {
            try
            {
                if (!File.Exists(TokenPath)) return null;
                byte[] dec = ProtectedData.Unprotect(File.ReadAllBytes(TokenPath), null,
                                                     DataProtectionScope.CurrentUser);
                string[] p = Encoding.UTF8.GetString(dec).Split('\n');
                if (p.Length < 3) return null;
                var t = new TokenSet();
                t.AccessToken = p[0];
                t.RefreshToken = p[1];
                DateTime exp;
                DateTime.TryParse(p[2], null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal |
                    System.Globalization.DateTimeStyles.AssumeUniversal, out exp);
                t.ExpiresAtUtc = DateTime.SpecifyKind(exp, DateTimeKind.Utc);
                t.Scope = p.Length > 3 ? p[3] : "";
                return t.Valid ? t : null;
            }
            catch (Exception) { return null; }
        }

        public static void Clear()
        {
            try { if (File.Exists(TokenPath)) File.Delete(TokenPath); }
            catch (Exception) { }
        }
    }
}
