using System;

namespace TokenMeter
{
    /// <summary>
    /// Display timezone. Deliberately not the machine's local time: usage windows are reasoned
    /// about against a fixed wall clock, and that should not move because a laptop travelled or
    /// because the machine is set to a neighbouring zone with the same offset today.
    /// </summary>
    public static class Tz
    {
        public const string DefaultId = "Singapore Standard Time";

        private static TimeZoneInfo _zone;
        private static string _id;

        public static void Use(string id)
        {
            _id = string.IsNullOrEmpty(id) ? DefaultId : id;
            _zone = null;
        }

        public static TimeZoneInfo Zone
        {
            get
            {
                if (_zone != null) return _zone;
                try { _zone = TimeZoneInfo.FindSystemTimeZoneById(_id ?? DefaultId); }
                catch (Exception)
                {
                    try { _zone = TimeZoneInfo.FindSystemTimeZoneById(DefaultId); }
                    catch (Exception) { _zone = TimeZoneInfo.Local; }
                }
                return _zone;
            }
        }

        public static string Id { get { return Zone.Id; } }

        /// <summary>Converts a UTC instant to the display zone.</summary>
        public static DateTime Show(DateTime utc)
        {
            if (utc.Kind == DateTimeKind.Unspecified)
                utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            try { return TimeZoneInfo.ConvertTimeFromUtc(utc.ToUniversalTime(), Zone); }
            catch (Exception) { return utc.ToLocalTime(); }
        }

        /// <summary>Converts a wall-clock time in the display zone back to UTC.</summary>
        public static DateTime ToUtc(DateTime displayTime)
        {
            try
            {
                return TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(displayTime, DateTimeKind.Unspecified), Zone);
            }
            catch (Exception) { return displayTime.ToUniversalTime(); }
        }

        /// <summary>Short label for the UI, e.g. "SGT (UTC+8)".</summary>
        public static string Label()
        {
            TimeSpan off = Zone.GetUtcOffset(DateTime.UtcNow);
            string sign = off < TimeSpan.Zero ? "-" : "+";
            string hours = Math.Abs(off.Hours).ToString();
            if (off.Minutes != 0) hours += ":" + Math.Abs(off.Minutes).ToString("00");
            return Zone.Id + " (UTC" + sign + hours + ")";
        }
    }
}
