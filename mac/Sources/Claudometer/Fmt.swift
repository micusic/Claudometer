import Foundation

enum Fmt {
    static func duration(_ t: TimeInterval) -> String {
        let s = max(0, Int(t))
        if s >= 86400 { return "\(s/86400)d \((s%86400)/3600)h" }
        if s >= 3600 { return "\(s/3600)h \((s%3600)/60)m" }
        if s >= 60 { return "\(s/60)m" }
        return "\(s)s"
    }
    static func pct(_ p: Double?) -> String {
        guard let p = p, !p.isNaN else { return "—" }
        return "\(Int(p.rounded()))%"
    }
    private static func fmt(_ f: String) -> DateFormatter {
        let d = DateFormatter(); d.locale = Locale(identifier: "en_US"); d.dateFormat = f; return d
    }
    static func time(_ d: Date) -> String { fmt("HH:mm").string(from: d) }
    static func dayTime(_ d: Date) -> String { fmt("EEE HH:mm").string(from: d) }
}
