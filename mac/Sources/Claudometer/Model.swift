import Foundation

/// One usage reading from the API, stamped with when we fetched it. Every field is a number
/// Anthropic returned — nothing here is derived. nil = that window was absent in the reading.
struct UsageSample: Codable {
    var utc: Date
    var fivePct: Double?
    var fiveReset: Date?
    var sevenPct: Double?
    var sevenReset: Date?
    var opusPct: Double?
    var sonnetPct: Double?
}

/// The authoritative reading parsed from api/oauth/usage.
struct UsageReading {
    var fivePct: Double?; var fiveReset: Date?
    var sevenPct: Double?; var sevenReset: Date?
    var opusPct: Double?
    var sonnetPct: Double?
}

/// Everything the panel needs for one refresh — assembled purely from stored API readings.
struct Snapshot {
    var loggedIn = false
    var hasData = false
    var apiStatus = ""
    var apiLive = Date()

    var fivePct: Double? = nil
    var fiveReset: Date? = nil

    var sevenPct: Double? = nil
    var sevenReset: Date? = nil

    var opusPct: Double? = nil
    var sonnetPct: Double? = nil

    // burn-up: minutes-from-window-start and percent, for the samples in the current window
    var burnMin: [Double] = []
    var burnPct: [Double] = []
    var nowMinutes: Double = 0

    // forecast endpoint (dashed extrapolation of the recent slope)
    var hasForecast = false
    var forecastEndMin: Double = 0
    var forecastEndPct: Double = 0

    var sampleCount = 0

    static let windowMinutes: Double = 300
}
