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
            }
            return s;
        }
    }
}
