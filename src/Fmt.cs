using System;

namespace TokenMeter
{
    /// <summary>Shared display formatting so the tray tooltip, panel and balloons agree.</summary>
    public static class Fmt
    {
        /// <summary>Compact duration, localized: "2h 13m" / "38m" / "45s" (units per language).</summary>
        public static string Duration(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (t.TotalDays >= 1) return L.F("dur.dh", (int)t.TotalDays, t.Hours);
            if (t.TotalHours >= 1) return L.F("dur.hm", (int)t.TotalHours, t.Minutes);
            if (t.TotalMinutes >= 1) return L.F("dur.m", (int)t.TotalMinutes);
            return L.F("dur.s", (int)t.TotalSeconds);
        }

        /// <summary>Clock time in the configured display zone (see <see cref="Tz"/>).</summary>
        public static string LocalTime(DateTime utc) { return Tz.Show(utc).ToString("HH:mm"); }

        /// <summary>Date in the display zone, formatted under the current language's culture (day names, etc.).</summary>
        public static string LocalDate(DateTime utc, string fmt)
        {
            return Tz.Show(utc).ToString(fmt, System.Threading.Thread.CurrentThread.CurrentCulture);
        }
    }
}
