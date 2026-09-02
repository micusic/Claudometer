import Foundation
import Security

/// Application-support paths and the local history store (the only persisted usage data — and
/// it holds only what the API returned, mirroring the Windows build's contract).
enum Paths {
    static var dir: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        let d = base.appendingPathComponent("Claudometer", isDirectory: true)
        try? FileManager.default.createDirectory(at: d, withIntermediateDirectories: true)
        return d
    }
    static var history: URL { dir.appendingPathComponent("history.json") }
}

/// Local store of API readings. Rounds each reset to the whole minute so a window's readings
/// share one canonical boundary (the API's reset jitters sub-second), and coalesces idle repeats.
final class History {
    private(set) var samples: [UsageSample] = []
    private let keep: TimeInterval = 9 * 24 * 3600

    var latest: UsageSample? { samples.last }

    func load() {
        samples = []
        guard let data = try? Data(contentsOf: Paths.history) else { return }
        let dec = JSONDecoder(); dec.dateDecodingStrategy = .iso8601
        if let s = try? dec.decode([UsageSample].self, from: data) { samples = s }
    }

    func save() {
        let enc = JSONEncoder(); enc.dateEncodingStrategy = .iso8601
        if let data = try? enc.encode(samples) { try? data.write(to: Paths.history, options: .atomic) }
    }

    func add(_ reading: UsageReading, at now: Date) {
        var s = UsageSample(utc: now)
        s.fivePct = reading.fivePct;   s.fiveReset = Self.roundMinute(reading.fiveReset)
        s.sevenPct = reading.sevenPct; s.sevenReset = Self.roundMinute(reading.sevenReset)
        s.opusPct = reading.opusPct;   s.sonnetPct = reading.sonnetPct

        if let last = samples.last,
           eq(last.fivePct, s.fivePct), last.fiveReset == s.fiveReset, eq(last.sevenPct, s.sevenPct),
           now.timeIntervalSince(last.utc) < 360 {
            samples[samples.count - 1].utc = now       // coalesce an unchanged idle repeat
        } else {
            samples.append(s)
        }
        let cutoff = now.addingTimeInterval(-keep)
        samples.removeAll { $0.utc < cutoff }
    }

    /// Samples in the current five-hour window, by reset-time tolerance (not exact equality).
    func inFiveHourWindow(reset: Date, now: Date) -> [UsageSample] {
        let start = reset.addingTimeInterval(-5 * 3600)
        return samples.filter {
            guard let p = $0.fivePct, let r = $0.fiveReset, !p.isNaN else { return false }
            return $0.utc >= start && $0.utc <= now && abs(r.timeIntervalSince(reset)) < 1800
        }
    }

    private func eq(_ a: Double?, _ b: Double?) -> Bool {
        if a == nil && b == nil { return true }
        guard let a = a, let b = b else { return false }
        return abs(a - b) < 0.001
    }

    private static func roundMinute(_ d: Date?) -> Date? {
        guard let d = d else { return nil }
        return Date(timeIntervalSince1970: (d.timeIntervalSince1970 / 60).rounded() * 60)
    }
}

/// OAuth token storage in the macOS Keychain (the parallel to Windows DPAPI).
enum Keychain {
    private static let service = "ai.claudometer.app"
    private static let account = "oauth-token"

    static func save(_ value: String) {
        let data = Data(value.utf8)
        let base: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
                                   kSecAttrService as String: service,
                                   kSecAttrAccount as String: account]
        SecItemDelete(base as CFDictionary)
        var add = base; add[kSecValueData as String] = data
        SecItemAdd(add as CFDictionary, nil)
    }

    static func load() -> String? {
        let q: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
                                kSecAttrService as String: service,
                                kSecAttrAccount as String: account,
                                kSecReturnData as String: true,
                                kSecMatchLimit as String: kSecMatchLimitOne]
        var out: AnyObject?
        guard SecItemCopyMatching(q as CFDictionary, &out) == errSecSuccess,
              let data = out as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    static func clear() {
        let base: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
                                   kSecAttrService as String: service,
                                   kSecAttrAccount as String: account]
        SecItemDelete(base as CFDictionary)
    }
}
