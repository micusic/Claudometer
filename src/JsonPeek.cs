using System;
using System.Globalization;

namespace TokenMeter
{
    /// <summary>
    /// Just enough JSON reading to pull usage numbers out of a transcript line.
    ///
    /// A full parse is the wrong tool here: transcript lines run to 100KB+, most of it
    /// thinking text and tool payloads we never look at, and there are 60k+ of them.
    /// This walks the raw string instead and only descends into the objects it needs,
    /// which keeps a cold scan of ~270MB down to a couple of seconds.
    ///
    /// Descending matters for correctness too, not just speed: a usage object also carries
    /// an "iterations" array whose elements repeat input_tokens / output_tokens. Reading
    /// only direct members keeps those from being counted twice.
    /// </summary>
    public static class JsonPeek
    {
        /// <summary>
        /// Index just past the colon of a direct "key": member of the object starting at
        /// <paramref name="objStart"/>. Returns -1 if the key is not a direct member.
        /// </summary>
        public static int FindMember(string s, int objStart, string key)
        {
            if (objStart < 0 || objStart >= s.Length || s[objStart] != '{') return -1;
            int depth = 0;
            bool inStr = false;
            for (int i = objStart; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"')
                    {
                        inStr = false;
                        // A string closing at depth 1 and followed by ':' is a member key.
                        if (depth == 1)
                        {
                            int j = SkipWs(s, i + 1);
                            if (j < s.Length && s[j] == ':')
                            {
                                int keyStart = i - key.Length;
                                if (keyStart >= 1 && s[keyStart - 1] == '"' &&
                                    string.CompareOrdinal(s, keyStart, key, 0, key.Length) == 0)
                                    return SkipWs(s, j + 1);
                            }
                        }
                    }
                    continue;
                }
                if (c == '"') { inStr = true; continue; }
                if (c == '{' || c == '[') { depth++; continue; }
                if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth == 0) return -1;   // walked past the end of this object
                }
            }
            return -1;
        }

        private static int SkipWs(string s, int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
            return i;
        }

        /// <summary>Reads an integer member; returns dflt if absent, null, or non-numeric.</summary>
        public static long Int(string s, int objStart, string key, long dflt)
        {
            int p = FindMember(s, objStart, key);
            if (p < 0 || p >= s.Length) return dflt;
            bool neg = false;
            if (s[p] == '-') { neg = true; p++; }
            if (p >= s.Length || !char.IsDigit(s[p])) return dflt;
            long v = 0;
            while (p < s.Length && char.IsDigit(s[p]))
            {
                v = v * 10 + (s[p] - '0');
                p++;
            }
            return neg ? -v : v;
        }

        /// <summary>Reads a floating-point member; returns NaN if absent or null.</summary>
        public static double Num(string s, int objStart, string key)
        {
            int p = FindMember(s, objStart, key);
            if (p < 0 || p >= s.Length) return double.NaN;
            int start = p;
            if (s[p] == '-' || s[p] == '+') p++;
            bool any = false;
            while (p < s.Length && (char.IsDigit(s[p]) || s[p] == '.' || s[p] == 'e' || s[p] == 'E'
                                    || s[p] == '-' || s[p] == '+'))
            { if (char.IsDigit(s[p])) any = true; p++; }
            if (!any) return double.NaN;
            double v;
            return double.TryParse(s.Substring(start, p - start),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v) ? v : double.NaN;
        }

        /// <summary>Reads a string member verbatim. Ids, model names and timestamps carry no escapes.</summary>
        public static string Str(string s, int objStart, string key)
        {
            int p = FindMember(s, objStart, key);
            if (p < 0 || p >= s.Length || s[p] != '"') return null;
            p++;
            int start = p;
            while (p < s.Length)
            {
                if (s[p] == '\\') { p += 2; continue; }
                if (s[p] == '"') break;
                p++;
            }
            if (p >= s.Length) return null;
            return s.Substring(start, p - start);
        }

        /// <summary>Start index of an object-valued member, or -1.</summary>
        public static int Obj(string s, int objStart, string key)
        {
            int p = FindMember(s, objStart, key);
            if (p < 0 || p >= s.Length || s[p] != '{') return -1;
            return p;
        }

        /// <summary>Parses the ISO-8601 timestamps Claude Code writes ("2026-08-31T03:49:59.268Z").</summary>
        public static bool TryTime(string iso, out DateTime utc)
        {
            utc = DateTime.MinValue;
            if (string.IsNullOrEmpty(iso)) return false;
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);
        }
    }
}
