import AppKit

/// The one-time login: open the Anthropic page in the browser, paste back the code. The app never
/// sees the password. Runs modally and returns the token on success.
final class LoginWindow: NSObject, NSWindowDelegate {
    private var window: NSWindow!
    private let verifier = OAuth.newVerifier()
    private let state = OAuth.newState()
    private var codeField: NSTextField!
    private var statusLabel: NSTextField!
    private var finishButton: NSButton!
    private var result: TokenSet?

    func run() -> TokenSet? {
        build()
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
        window.center()
        NSApp.runModal(for: window)
        return result
    }

    private func build() {
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 460, height: 250),
                          styleMask: [.titled, .closable], backing: .buffered, defer: false)
        window.title = "Sign in to Claude"
        window.delegate = self
        let v = NSView(frame: window.contentView!.bounds)
        v.autoresizingMask = [.width, .height]

        let note = label("Sign in in your own browser (Anthropic's page). This app never sees your password — it only receives a usage token, stored in your Keychain.",
                         NSRect(x: 20, y: 195, width: 420, height: 44), NSColor.secondaryLabelColor, 11)
        note.lineBreakMode = .byWordWrapping; note.maximumNumberOfLines = 3
        v.addSubview(note)

        v.addSubview(label("1.  Open the authorization page", NSRect(x: 20, y: 165, width: 420, height: 18), .labelColor, 12))
        let open = button("Open sign-in page in browser", NSRect(x: 34, y: 132, width: 240, height: 28), #selector(openBrowser))
        v.addSubview(open)

        v.addSubview(label("2.  Paste the code the page gives you", NSRect(x: 20, y: 104, width: 420, height: 18), .labelColor, 12))
        codeField = NSTextField(frame: NSRect(x: 34, y: 74, width: 406, height: 24))
        codeField.placeholderString = "code#state"
        v.addSubview(codeField)

        finishButton = button("Finish sign-in", NSRect(x: 34, y: 38, width: 140, height: 30), #selector(finish))
        finishButton.keyEquivalent = "\r"
        v.addSubview(finishButton)
        let cancel = button("Cancel", NSRect(x: 182, y: 38, width: 90, height: 30), #selector(cancel))
        v.addSubview(cancel)

        statusLabel = label("", NSRect(x: 20, y: 12, width: 420, height: 20), NSColor.secondaryLabelColor, 11)
        v.addSubview(statusLabel)
        window.contentView = v
    }

    @objc private func openBrowser() {
        if let u = URL(string: OAuth.authorizeURLString(verifier: verifier, state: state)) {
            NSWorkspace.shared.open(u)
            say("Browser opened. After signing in, copy the code the page shows and paste it above.", .secondaryLabelColor)
        }
    }

    @objc private func finish() {
        let pasted = codeField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard pasted.count >= 8 else { say("Paste the code from the page first.", .systemOrange); return }
        say("Verifying and exchanging the token…", .secondaryLabelColor)
        finishButton.isEnabled = false
        DispatchQueue.global().async {
            let res = OAuth.exchange(pasted: pasted, verifier: self.verifier, expectedState: self.state)
            DispatchQueue.main.async {
                self.finishButton.isEnabled = true
                if let t = res.token {
                    OAuth.saveToken(t)
                    self.result = t
                    NSApp.stopModal()
                    self.window.close()
                } else {
                    self.say(res.error ?? "Token exchange failed.", .systemRed)
                }
            }
        }
    }

    @objc private func cancel() { NSApp.stopModal(); window.close() }
    func windowWillClose(_ notification: Notification) { NSApp.stopModal() }

    private func say(_ s: String, _ c: NSColor) { statusLabel.stringValue = s; statusLabel.textColor = c }

    private func label(_ s: String, _ r: NSRect, _ color: NSColor, _ sz: CGFloat) -> NSTextField {
        let l = NSTextField(labelWithString: s); l.frame = r; l.textColor = color; l.font = .systemFont(ofSize: sz)
        return l
    }
    private func button(_ title: String, _ r: NSRect, _ action: Selector) -> NSButton {
        let b = NSButton(frame: r); b.title = title; b.bezelStyle = .rounded; b.target = self; b.action = action
        return b
    }
}
