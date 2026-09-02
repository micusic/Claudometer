using System;
using System.Collections.Generic;

namespace TokenMeter
{
    /// <summary>
    /// Everything the UI needs for one refresh - assembled purely from API readings. No budgets,
    /// no burn rate, no projection: every value here is something Anthropic reported.
    /// </summary>
    public class Snapshot
    {
        public DateTime NowUtc;

        public bool LoggedIn;
        public bool HasData;             // at least one successful API reading is stored
        public string ApiStatus = "";
        public DateTime ApiLiveUtc;      // when the latest reading was fetched

        // Five-hour window (percent 0..100)
        public double FivePct = double.NaN;
        public DateTime FiveResetUtc;
        public TimeSpan ToReset;

        // Seven-day window
        public double SevenPct = double.NaN;
        public DateTime SevenResetUtc;
        public TimeSpan ToWeekReset;

        // Per-model weekly (percent 0..100; NaN when the API omitted it)
        public double OpusPct = double.NaN;
        public double SonnetPct = double.NaN;

        // Burn-up: real API samples inside the current five-hour window.
        // BurnMin[i] = minutes since window start; BurnPct[i] = utilization at that poll.
        public List<double> BurnMin = new List<double>();
        public List<double> BurnPct = new List<double>();
        public double NowMinutes;        // minutes from window start to the latest sample
        public const double WindowMinutes = 300;

        // Forecast: extrapolate the recent observed slope from "now" to the reset (an estimate,
        // shown dashed). ForecastEndMin/Pct is where the dashed line ends (the reset, or the 100%
        // crossing if it would hit the ceiling first).
        public bool HasForecast;
        public double ForecastEndMin;
        public double ForecastEndPct;

        public int SampleCount;
    }

    public static class Analytics
    {
        public static readonly TimeSpan Window = TimeSpan.FromHours(5);

        public static Snapshot Build(History hist, bool loggedIn, string apiStatus, DateTime nowUtc)
        {
            var s = new Snapshot();
            s.NowUtc = nowUtc;
            s.LoggedIn = loggedIn;
            s.ApiStatus = apiStatus ?? "";
            s.SampleCount = hist != null ? hist.Samples.Count : 0;

            UsageSample latest = hist != null ? hist.Latest : null;
            if (latest == null) return s;   // logged in but no reading yet

            s.HasData = true;
            s.ApiLiveUtc = latest.Utc;

            s.FivePct = latest.FivePct;
            s.FiveResetUtc = latest.FiveResetUtc;
            s.ToReset = s.FiveResetUtc > nowUtc ? s.FiveResetUtc - nowUtc : TimeSpan.Zero;

            s.SevenPct = latest.SevenPct;
            s.SevenResetUtc = latest.SevenResetUtc;
            s.ToWeekReset = s.SevenResetUtc > nowUtc ? s.SevenResetUtc - nowUtc : TimeSpan.Zero;

            s.OpusPct = latest.OpusPct;
            s.SonnetPct = latest.SonnetPct;

            // Burn-up trajectory from the real samples in this window.
            if (!double.IsNaN(s.FivePct) && s.FiveResetUtc > DateTime.MinValue)
            {
                DateTime start = s.FiveResetUtc - Window;
                List<UsageSample> win = hist.InFiveHourWindow(s.FiveResetUtc, nowUtc);
                foreach (UsageSample smp in win)
                {
                    s.BurnMin.Add((smp.Utc - start).TotalMinutes);
                    s.BurnPct.Add(smp.FivePct);
                }
                s.NowMinutes = win.Count > 0
                    ? (win[win.Count - 1].Utc - start).TotalMinutes
                    : (nowUtc - start).TotalMinutes;

                BuildForecast(s);
            }
            return s;
        }

        /// <summary>
        /// Extrapolate the recent observed slope from now to the reset. Uses the last ~45 minutes
        /// of readings for the rate; within a window usage only rises, so a negative slope (noise)
        /// is treated as flat. The endpoint is the reset, or the 100% crossing if it comes first.
        /// </summary>
        private static void BuildForecast(Snapshot s)
        {
            int n = s.BurnPct.Count;
            if (n < 2 || s.NowMinutes >= Snapshot.WindowMinutes) return;

            double nowMin = s.BurnMin[n - 1], nowPct = s.BurnPct[n - 1];
            double refMin = s.BurnMin[0], refPct = s.BurnPct[0];
            for (int i = n - 1; i >= 0; i--)
            {
                if (nowMin - s.BurnMin[i] >= 45) { refMin = s.BurnMin[i]; refPct = s.BurnPct[i]; break; }
                refMin = s.BurnMin[i]; refPct = s.BurnPct[i];
            }

            double span = nowMin - refMin;
            if (span < 8) return;   // too little history for a meaningful slope

            double ratePerMin = (nowPct - refPct) / span;
            if (ratePerMin < 0) ratePerMin = 0;

            double toReset = Snapshot.WindowMinutes - nowMin;
            double endPct = nowPct + ratePerMin * toReset;
            double endMin = Snapshot.WindowMinutes;
            if (endPct > 100 && ratePerMin > 0)
            {
                endMin = nowMin + (100 - nowPct) / ratePerMin;
                endPct = 100;
            }
            s.HasForecast = true;
            s.ForecastEndMin = endMin;
            s.ForecastEndPct = endPct;
        }
    }
}
