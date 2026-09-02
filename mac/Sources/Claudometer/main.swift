import AppKit

enum App { static let version = "1.2.0" }

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
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
        statusItem.button?.title = "◔"
        let menu = NSMenu()
        menu.delegate = self          // rebuilt on open so it reflects login state
        statusItem.menu = menu

        popover.behavior = .transient
        popover.contentSize = panel.bounds.size
        let vc = NSViewController(); vc.view = panel
        popover.contentViewController = vc
        panel.onLogin = { [weak self] in self?.popover.performClose(nil); self?.doLogin() }

        rebuild()
        timer = Timer.scheduledTimer(withTimeInterval: 15, repeats: true) { [weak self] _ in self?.tick() }
        if token != nil { pollNow() }
    }

    // ---- menu (rebuilt each open) ----

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        add(menu, "Show panel", #selector(showPanel))
        add(menu, "Refresh now", #selector(pollNow))
        menu.addItem(.separator())
        if token == nil {
            add(menu, "Sign in to Claude…", #selector(doLogin))
        } else {
            add(menu, "Re-sign in…", #selector(doLogin))
            add(menu, "Sign out", #selector(doLogout))
        }
        add(menu, "Open data folder", #selector(openFolder))
        menu.addItem(.separator())
        let v = NSMenuItem(title: "Claudometer v\(App.version)", action: nil, keyEquivalent: "")
        v.isEnabled = false
        menu.addItem(v)
        menu.addItem(NSMenuItem(title: "Quit", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
    }

    private func add(_ menu: NSMenu, _ title: String, _ action: Selector) {
        let it = NSMenuItem(title: title, action: action, keyEquivalent: "")
        it.target = self
        menu.addItem(it)
    }

    @objc private func showPanel() {
        panel.snapshot = snapshot; panel.needsDisplay = true
        guard let b = statusItem.button else { return }
        popover.show(relativeTo: b.bounds, of: b, preferredEdge: .minY)
        popover.contentViewController?.view.window?.makeKey()
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
        if let t = LoginWindow().run() { token = t; backoff = pollInterval; lastPoll = .distantPast; pollNow() }
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
