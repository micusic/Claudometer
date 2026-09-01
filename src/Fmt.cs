using System;

namespace TokenMeter
{
    /// <summary>Shared display formatting so the tray tooltip, panel and balloons agree.</summary>
    public static class Fmt
    {
        /// <summary>Compact duration: "2小时13分" / "38分钟" / "45秒".</summary>
        public static string Duration(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (t.TotalDays >= 1) return (int)t.TotalDays + "天" + t.Hours + "小时";
            if (t.TotalHours >= 1) return (int)t.TotalHours + "小时" + t.Minutes + "分";
            if (t.TotalMinutes >= 1) return (int)t.TotalMinutes + "分钟";
            return (int)t.TotalSeconds + "秒";
        }

        /// <summary>Clock time in the configured display zone (see <see cref="Tz"/>).</summary>
        public static string LocalTime(DateTime utc) { return Tz.Show(utc).ToString("HH:mm"); }

        public static string LocalDate(DateTime utc, string fmt) { return Tz.Show(utc).ToString(fmt); }
    }
}
