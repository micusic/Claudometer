import AppKit

enum App { static let version = "1.2.0" }

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem!
    private let popover = NSPopover()
    private let panel = PanelView(frame: NSRect(x: 0, y: 0, width: 330, height: 470))

    private let history = History()
    private var token: TokenSet?
    private var snapshot = Snapshot()
    private var lastPoll = Date.distantPast
    private var pollInterval: TimeInterval = 90
    private var backoff: TimeInterval = 90
    private var apiStatus = ""
    private var polling = false
    private var timer: Timer?

    func applicationDidFinishLaunching(_ note: Notification) {
        history.load()
        token = OAuth.loadToken()

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let b = statusItem.button {
            b.title = "◔"
            b.target = self
            b.action = #selector(statusClicked)
            b.sendAction(on: [.leftMouseUp, .rightMouseUp])
        }

        popover.behavior = .transient
        popover.contentSize = panel.bounds.size
        let vc = NSViewController(); vc.view = panel
        popover.contentViewController = vc
        panel.onLogin = { [weak self] in self?.popover.performClose(nil); self?.doLogin() }

        rebuild()
        timer = Timer.scheduledTimer(withTimeInterval: 15, repeats: true) { [weak self] _ in self?.tick() }
        if token != nil { pollNow() }
    }

    // ---- status item ----

    @objc private func statusClicked() {
        let rightClick = NSApp.currentEvent?.type == .rightMouseUp
        if rightClick { showMenu() } else { togglePopover() }
    }

    private func togglePopover() {
        if popover.isShown { popover.performClose(nil); return }
        panel.snapshot = snapshot; panel.needsDisplay = true
        if let b = statusItem.button {
            popover.show(relativeTo: b.bounds, of: b, preferredEdge: .minY)
            popover.contentViewController?.view.window?.makeKey()
        }
    }

    private func showMenu() {
        let m = NSMenu()
        m.addItem(NSMenuItem(title: "Refresh now", action: #selector(pollNow), keyEquivalent: ""))
        m.addItem(.separator())
        if token == nil {
            m.addItem(NSMenuItem(title: "Sign in to Claude…", action: #selector(doLogin), keyEquivalent: ""))
        } else {
            m.addItem(NSMenuItem(title: "Re-sign in…", action: #selector(doLogin), keyEquivalent: ""))
            m.addItem(NSMenuItem(title: "Sign out", action: #selector(doLogout), keyEquivalent: ""))
        }
        m.addItem(NSMenuItem(title: "Open data folder", action: #selector(openFolder), keyEquivalent: ""))
        m.addItem(.separator())
        m.addItem(NSMenuItem(title: "Claudometer v\(App.version)", action: nil, keyEquivalent: ""))
        m.addItem(NSMenuItem(title: "Quit", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        for it in m.items where it.action != nil && it.target == nil { it.target = self }
        statusItem.menu = m
        statusItem.button?.performClick(nil)
        statusItem.menu = nil   // detach so left-click resumes toggling the popover
    }

    // ---- polling ----

    private func tick() {
        if token != nil && Date().timeIntervalSince(lastPoll) >= backoff { pollNow() }
        else { rebuild(); apply() }
    }

    @objc private func pollNow() {
        guard token != nil, !polling else { rebuild(); apply(); return }
        polling = true; lastPoll = Date()
        DispatchQueue.global().async { [weak self] in
            self?.doPoll()
            DispatchQueue.main.async { self?.polling = false; self?.rebuild(); self?.apply() }
        }
    }

    private func doPoll() {
        guard var t = token else { return }
        if t.expired {
            if let fresh = OAuth.refresh(t.refreshToken) { t = fresh; token = t; OAuth.saveToken(t) }
            else { apiStatus = "Session expired — sign in again"; return }
        }
        var (st, reading, _) = UsageAPI.fetch(t.accessToken)
        if st == .unauthorized, let fresh = OAuth.refresh(t.refreshToken) {
            t = fresh; token = t; OAuth.saveToken(t)
            (st, reading, _) = UsageAPI.fetch(t.accessToken)
        }
        switch st {
        case .ok:
            backoff = pollInterval; apiStatus = ""
            if let r = reading { history.add(r, at: Date()); history.save() }
        case .rateLimited:
            backoff = min(900, max(pollInterval, backoff) * 2)
            apiStatus = "Rate-limited, retrying in \(Int(backoff/60)) min"
        case .unauthorized: apiStatus = "Session expired — sign in again"
        case .error: apiStatus = "Connection failed, retrying"
        }
    }

    private func rebuild() {
        snapshot = Analytics.build(history: history, loggedIn: token != nil, apiStatus: apiStatus, now: Date())
    }

    private func apply() {
        if let b = statusItem.button {
            if !snapshot.loggedIn { b.title = "◔" }
            else if !snapshot.hasData { b.title = "…" }
            else { b.title = Fmt.pct(snapshot.fivePct) }
        }
        if popover.isShown { panel.snapshot = snapshot; panel.needsDisplay = true }
    }

    // ---- actions ----

    @objc private func doLogin() {
        let t = LoginWindow().run()
        if let t = t { token = t; backoff = pollInterval; lastPoll = .distantPast; pollNow() }
    }

    @objc private func doLogout() {
        OAuth.clearToken(); token = nil; apiStatus = ""
        rebuild(); apply()
    }

    @objc private func openFolder() { NSWorkspace.shared.open(Paths.dir) }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
