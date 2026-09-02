import Foundation

enum Analytics {
    static let window: TimeInterval = 5 * 3600

    static func build(history: History, loggedIn: Bool, apiStatus: String, now: Date) -> Snapshot {
        var s = Snapshot()
        s.loggedIn = loggedIn
        s.apiStatus = apiStatus
        s.sampleCount = history.samples.count

        guard let latest = history.latest else { return s }
        s.hasData = true
        s.apiLive = latest.utc
        s.fivePct = latest.fivePct
        s.fiveReset = latest.fiveReset
        s.sevenPct = latest.sevenPct
        s.sevenReset = latest.sevenReset
        s.opusPct = latest.opusPct
        s.sonnetPct = latest.sonnetPct

        if let five = latest.fivePct, !five.isNaN, let reset = latest.fiveReset {
            let start = reset.addingTimeInterval(-window)
            let win = history.inFiveHourWindow(reset: reset, now: now)
            for smp in win {
                if let p = smp.fivePct {
                    s.burnMin.append(smp.utc.timeIntervalSince(start) / 60)
                    s.burnPct.append(p)
                }
            }
            s.nowMinutes = (win.last?.utc ?? now).timeIntervalSince(start) / 60
            buildForecast(&s)
        }
        return s
    }

    /// Extrapolate the recent (~45 min) observed slope from now to the reset; usage only rises in a
    /// window, so a negative slope is treated as flat. Endpoint = reset, or the 100% crossing first.
    private static func buildForecast(_ s: inout Snapshot) {
        let n = s.burnPct.count
        guard n >= 2, s.nowMinutes < Snapshot.windowMinutes else { return }
        let nowMin = s.burnMin[n - 1], nowPct = s.burnPct[n - 1]
        var refMin = s.burnMin[0], refPct = s.burnPct[0]
        var i = n - 1
        while i >= 0 {
            if nowMin - s.burnMin[i] >= 45 { refMin = s.burnMin[i]; refPct = s.burnPct[i]; break }
            refMin = s.burnMin[i]; refPct = s.burnPct[i]
            i -= 1
        }
        let span = nowMin - refMin
        guard span >= 8 else { return }
        var rate = (nowPct - refPct) / span
        if rate < 0 { rate = 0 }
        let toReset = Snapshot.windowMinutes - nowMin
        var endPct = nowPct + rate * toReset
        var endMin = Snapshot.windowMinutes
        if endPct > 100 && rate > 0 { endMin = nowMin + (100 - nowPct) / rate; endPct = 100 }
        s.hasForecast = true
        s.forecastEndMin = endMin
        s.forecastEndPct = endPct
    }
}
