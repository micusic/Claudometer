import Foundation
import CryptoKit

struct TokenSet: Codable {
    var accessToken: String
    var refreshToken: String
    var expiresAt: Date
    var scope: String
    var valid: Bool { !accessToken.isEmpty }
    var expired: Bool { Date() >= expiresAt.addingTimeInterval(-120) }
}

/// Claude Code's OAuth flow (PKCE), ported from the Windows build. Same public client and
/// copy-paste code flow; a unique per-process User-Agent keeps logins off the shared rate-limit
/// bucket. The user signs in in their browser — the app never sees the password.
enum OAuth {
    static let clientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e"
    static let authorizeURL = "https://claude.ai/oauth/authorize"
    static let tokenURL = "https://platform.claude.com/v1/oauth/token"
    static let redirectURI = "https://platform.claude.com/oauth/code/callback"
    static let scope = "user:inference user:profile user:sessions:claude_code user:mcp_servers"
    static let userAgent = "Claudometer-mac/\(App.version) (\(UUID().uuidString.prefix(12)))"

    // ---- PKCE ----
    static func newVerifier() -> String { base64url(randomBytes(32)) }
    static func newState() -> String { base64url(randomBytes(24)) }
    static func challenge(_ verifier: String) -> String {
        base64url(Data(SHA256.hash(data: Data(verifier.utf8))))
    }
    private static func randomBytes(_ n: Int) -> Data {
        var b = [UInt8](repeating: 0, count: n)
        _ = SecRandomCopyBytes(kSecRandomDefault, n, &b)
        return Data(b)
    }
    private static func base64url(_ d: Data) -> String {
        d.base64EncodedString().replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_").replacingOccurrences(of: "=", with: "")
    }

    static func authorizeURLString(verifier: String, state: String) -> String {
        var c = URLComponents(string: authorizeURL)!
        c.queryItems = [
            .init(name: "code", value: "true"),
            .init(name: "client_id", value: clientId),
            .init(name: "response_type", value: "code"),
            .init(name: "redirect_uri", value: redirectURI),
            .init(name: "scope", value: scope),
            .init(name: "code_challenge", value: challenge(verifier)),
            .init(name: "code_challenge_method", value: "S256"),
            .init(name: "state", value: state),
        ]
        return c.url!.absoluteString
    }

    struct ExchangeResult { var token: TokenSet?; var error: String?; var rateLimited = false }

    static func exchange(pasted: String, verifier: String, expectedState: String) -> ExchangeResult {
        var code = pasted.trimmingCharacters(in: .whitespacesAndNewlines)
        var state = expectedState
        if let hash = code.firstIndex(of: "#") {
            state = String(code[code.index(after: hash)...])
            code = String(code[..<hash])
        }
        if !expectedState.isEmpty && state != expectedState {
            return ExchangeResult(error: "The pasted code doesn't match this sign-in. Start again.")
        }
        let body = form([
            "grant_type": "authorization_code", "code": code, "state": state,
            "client_id": clientId, "redirect_uri": redirectURI, "code_verifier": verifier,
        ])
        return post(body)
    }

    static func refresh(_ refreshToken: String) -> TokenSet? {
        let body = form(["grant_type": "refresh_token", "refresh_token": refreshToken, "client_id": clientId])
        return post(body).token
    }

    private static func post(_ body: Data) -> ExchangeResult {
        guard let url = URL(string: tokenURL) else { return ExchangeResult(error: "bad url") }
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.httpBody = body
        req.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        req.setValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta")
        req.setValue(userAgent, forHTTPHeaderField: "User-Agent")
        req.timeoutInterval = 30

        let (data, status) = Net.sync(req)
        if status == 429 {
            return ExchangeResult(error: "Token service is rate-limited; wait a minute and retry with the same code.", rateLimited: true)
        }
        guard let data = data, status >= 200, status < 300,
              let obj = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let access = obj["access_token"] as? String, !access.isEmpty else {
            let msg = data.flatMap { String(data: $0, encoding: .utf8) } ?? "HTTP \(status)"
            return ExchangeResult(error: "Token exchange failed: \(msg.prefix(160))")
        }
        let expires = (obj["expires_in"] as? Double) ?? 3600
        let t = TokenSet(accessToken: access,
                         refreshToken: (obj["refresh_token"] as? String) ?? "",
                         expiresAt: Date().addingTimeInterval(expires),
                         scope: (obj["scope"] as? String) ?? "")
        return ExchangeResult(token: t)
    }

    private static func form(_ fields: [String: String]) -> Data {
        var cs = CharacterSet.alphanumerics; cs.insert(charactersIn: "-._~")
        let s = fields.map { k, v in
            "\(k)=\(v.addingPercentEncoding(withAllowedCharacters: cs) ?? "")"
        }.joined(separator: "&")
        return Data(s.utf8)
    }

    // ---- token persistence (Keychain) ----
    static func loadToken() -> TokenSet? {
        guard let s = Keychain.load(), let d = s.data(using: .utf8) else { return nil }
        let dec = JSONDecoder(); dec.dateDecodingStrategy = .iso8601
        return try? dec.decode(TokenSet.self, from: d)
    }
    static func saveToken(_ t: TokenSet) {
        let enc = JSONEncoder(); enc.dateEncodingStrategy = .iso8601
        if let d = try? enc.encode(t), let s = String(data: d, encoding: .utf8) { Keychain.save(s) }
    }
    static func clearToken() { Keychain.clear() }
}

/// Small synchronous URLSession wrapper (calls run off the main thread).
enum Net {
    static func sync(_ req: URLRequest) -> (Data?, Int) {
        let sem = DispatchSemaphore(value: 0)
        var outData: Data?; var code = 0
        URLSession.shared.dataTask(with: req) { data, resp, _ in
            outData = data
            code = (resp as? HTTPURLResponse)?.statusCode ?? 0
            sem.signal()
        }.resume()
        _ = sem.wait(timeout: .now() + 35)
        return (outData, code)
    }
}
