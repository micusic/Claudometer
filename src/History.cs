using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TokenMeter
{
    /// <summary>
    /// One usage reading as returned by the API, stamped with when we fetched it. Every field is
    /// a number Anthropic gave us - nothing here is derived or predicted. NaN = the window was
    /// absent/inactive in that reading.
    /// </summary>
    public class UsageSample
    {
        public DateTime Utc;
        public double FivePct = double.NaN;
        public DateTime FiveResetUtc;
        public double SevenPct = double.NaN;
        public DateTime SevenResetUtc;
        public double OpusPct = double.NaN;
        public DateTime OpusResetUtc;
        public double SonnetPct = double.NaN;
        public DateTime SonnetResetUtc;

        public static UsageSample From(UsageReading r)
        {
            var s = new UsageSample();
            s.Utc = r.FetchedUtc;
            if (r.FiveHour.HasValue) { s.FivePct = r.FiveHour.Utilization; s.FiveResetUtc = r.FiveHour.ResetUtc; }
            if (r.SevenDay.HasValue) { s.SevenPct = r.SevenDay.Utilization; s.SevenResetUtc = r.SevenDay.ResetUtc; }
            if (r.SevenDayOpus.HasValue) { s.OpusPct = r.SevenDayOpus.Utilization; s.OpusResetUtc = r.SevenDayOpus.ResetUtc; }
            if (r.SevenDaySonnet.HasValue) { s.SonnetPct = r.SevenDaySonnet.Utilization; s.SonnetResetUtc = r.SevenDaySonnet.ResetUtc; }
            return s;
        }

        public void Write(BinaryWriter w)
        {
            w.Write(Utc.Ticks);
            w.Write(FivePct); w.Write(FiveResetUtc.Ticks);
            w.Write(SevenPct); w.Write(SevenResetUtc.Ticks);
            w.Write(OpusPct); w.Write(OpusResetUtc.Ticks);
            w.Write(SonnetPct); w.Write(SonnetResetUtc.Ticks);
        }

        public static UsageSample Read(BinaryReader r)
        {
            var s = new UsageSample();
            s.Utc = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
            s.FivePct = r.ReadDouble(); s.FiveResetUtc = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
            s.SevenPct = r.ReadDouble(); s.SevenResetUtc = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
            s.OpusPct = r.ReadDouble(); s.OpusResetUtc = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
            s.SonnetPct = r.ReadDouble(); s.SonnetResetUtc = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
            return s;
        }
    }

    /// <summary>
    /// The local store of API readings. This is the ONLY thing persisted, and it holds only what
    /// the API returned - so the burn-up chart is drawn from real datapoints, never a guess.
    /// </summary>
    public class History
    {
        private const int Version = 1;
        private static readonly TimeSpan Keep = TimeSpan.FromDays(9);   // enough for the 7-day window

        private readonly List<UsageSample> _samples = new List<UsageSample>();

        public IList<UsageSample> Samples { get { return _samples; } }
        public UsageSample Latest { get { return _samples.Count > 0 ? _samples[_samples.Count - 1] : null; } }

        public static string Path { get { return System.IO.Path.Combine(AppConfig.Dir, "history.bin"); } }

        /// <summary>Appends a reading, coalescing near-duplicate consecutive polls to keep the file small.</summary>
        public void Add(UsageSample s)
        {
            UsageSample last = Latest;
            // If the last sample is very recent and unchanged, just move its timestamp forward -
            // no need to store hundreds of identical idle points.
            if (last != null
                && Same(last.FivePct, s.FivePct) && last.FiveResetUtc == s.FiveResetUtc
                && Same(last.SevenPct, s.SevenPct)
                && (s.Utc - last.Utc) < TimeSpan.FromMinutes(6))
            {
                last.Utc = s.Utc;
            }
            else
            {
                _samples.Add(s);
            }
            Prune(s.Utc);
        }

        private static bool Same(double a, double b)
        {
            if (double.IsNaN(a) && double.IsNaN(b)) return true;
            return Math.Abs(a - b) < 0.001;
        }

        private void Prune(DateTime now)
        {
            DateTime cutoff = now - Keep;
            int drop = 0;
            while (drop < _samples.Count && _samples[drop].Utc < cutoff) drop++;
            if (drop > 0) _samples.RemoveRange(0, drop);
        }

        /// <summary>Samples inside the current five-hour window [reset-5h, now], for the burn-up line.</summary>
        public List<UsageSample> InFiveHourWindow(DateTime resetUtc, DateTime now)
        {
            DateTime start = resetUtc - TimeSpan.FromHours(5);
            var outp = new List<UsageSample>();
            foreach (UsageSample s in _samples)
                if (!double.IsNaN(s.FivePct) && s.FiveResetUtc == resetUtc && s.Utc >= start && s.Utc <= now)
                    outp.Add(s);
            return outp;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppConfig.Dir);
                string tmp = Path + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                using (var w = new BinaryWriter(fs, Encoding.UTF8))
                {
                    w.Write(Version);
                    w.Write(_samples.Count);
                    foreach (UsageSample s in _samples) s.Write(w);
                }
                if (File.Exists(Path)) File.Delete(Path);
                File.Move(tmp, Path);
            }
            catch (Exception) { }
        }

        public void Load()
        {
            _samples.Clear();
            try
            {
                if (!File.Exists(Path)) return;
                using (var fs = new FileStream(Path, FileMode.Open, FileAccess.Read))
                using (var r = new BinaryReader(fs, Encoding.UTF8))
                {
                    if (r.ReadInt32() != Version) return;
                    int n = r.ReadInt32();
                    for (int i = 0; i < n; i++) _samples.Add(UsageSample.Read(r));
                }
            }
            catch (Exception) { _samples.Clear(); }
        }
    }
}
