import Foundation

/// Reads the authoritative usage endpoint — the same numbers as /usage.
enum UsageAPI {
    enum Status { case ok, unauthorized, rateLimited, error }
    static let url = "https://api.anthropic.com/api/oauth/usage"

    static func fetch(_ accessToken: String) -> (Status, UsageReading?, String) {
        guard let u = URL(string: url) else { return (.error, nil, "bad url") }
        var req = URLRequest(url: u)
        req.httpMethod = "GET"
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        req.setValue("Bearer \(accessToken)", forHTTPHeaderField: "Authorization")
        req.setValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta")
        req.setValue(OAuth.userAgent, forHTTPHeaderField: "User-Agent")
        req.timeoutInterval = 20

        let (data, code) = Net.sync(req)
        if code == 401 || code == 403 { return (.unauthorized, nil, "HTTP \(code)") }
        if code == 429 { return (.rateLimited, nil, "HTTP 429") }
        guard let data = data, code >= 200, code < 300,
              let obj = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else {
            return (.error, nil, "HTTP \(code)")
        }
        var r = UsageReading()
        (r.fivePct, r.fiveReset) = window(obj, "five_hour")
        (r.sevenPct, r.sevenReset) = window(obj, "seven_day")
        (r.opusPct, _) = window(obj, "seven_day_opus")
        (r.sonnetPct, _) = window(obj, "seven_day_sonnet")
        return (.ok, r, "")
    }

    private static func window(_ obj: [String: Any], _ key: String) -> (Double?, Date?) {
        guard let w = obj[key] as? [String: Any] else { return (nil, nil) }
        let pct = (w["utilization"] as? Double) ?? (w["utilization"] as? NSNumber)?.doubleValue
        let reset = (w["resets_at"] as? String).flatMap(parseDate)
        return (pct, reset)
    }

    static func parseDate(_ s: String) -> Date? {
        let f1 = ISO8601DateFormatter()
        f1.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = f1.date(from: s) { return d }
        let f2 = ISO8601DateFormatter()
        f2.formatOptions = [.withInternetDateTime]
        return f2.date(from: s)
    }
}
