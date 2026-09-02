import AppKit

/// The popover panel, owner-drawn to match the Windows build: header + version, the 5-hour gauge,
/// the burn-up chart (actual + dashed forecast, pace, 100% ceiling), the 7-day gauge, per-model,
/// and a footer. Logged-out shows a Sign-in button.
final class PanelView: NSView {
    var snapshot = Snapshot()
    var onLogin: (() -> Void)?

    private var loginRect = NSRect.zero
    private let pad: CGFloat = 16

    override var isFlipped: Bool { true }

    private var theme: Theme {
        let dark = effectiveAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
        return Theme.current(dark)
    }

    // fonts
    private let fTitle = NSFont.systemFont(ofSize: 14, weight: .bold)
    private let fBig = NSFont.systemFont(ofSize: 26, weight: .bold)
    private let fBold = NSFont.systemFont(ofSize: 11, weight: .semibold)
    private let f = NSFont.systemFont(ofSize: 11)
    private let fSmall = NSFont.systemFont(ofSize: 9.5)

    override func draw(_ dirtyRect: NSRect) {
        let th = theme
        th.bg.setFill(); bounds.fill()
        var y = pad
        let W = bounds.width

        // header
        accentDot(th, x: pad, y: y + 4)
        t("Claudometer", pad + 15, y - 1, fTitle, th.text)
        let tw = size("Claudometer", fTitle).width
        t("v\(App.version)", pad + 15 + tw + 5, y + 5, fSmall, th.faint)
        y += 30
        line(th, y); y += 14

        if !snapshot.loggedIn { drawLogin(th, &y, W); return }
        if !snapshot.hasData {
            t(snapshot.apiStatus.isEmpty ? "Fetching official usage…" : snapshot.apiStatus,
              pad, y + 40, f, th.muted)
            return
        }

        drawFive(th, &y, W)
        drawChart(th, &y, W)
        drawSeven(th, &y, W)
        drawModels(th, &y, W)
        drawFooter(th, W)
    }

    // ---- sections ----

    private func drawLogin(_ th: Theme, _ y: inout CGFloat, _ W: CGFloat) {
        y += 30
        center("Sign in to Claude", th.text, fTitle, W, y); y += 26
        center("Shows only data from the official usage API.", th.muted, f, W, y); y += 17
        center("You sign in in your browser — no password here.", th.muted, f, W, y); y += 28
        let label = "Sign in to Claude"
        let sz = size(label, fBold)
        let bw = sz.width + 40, bh: CGFloat = 32
        loginRect = NSRect(x: (W - bw)/2, y: y, width: bw, height: bh)
        fillRound(loginRect, 7, th.accent)
        t(label, loginRect.midX - sz.width/2, loginRect.midY - sz.height/2, fBold, th.onAccent)
    }

    private func drawFive(_ th: Theme, _ y: inout CGFloat, _ W: CGFloat) {
        let pct = snapshot.fivePct ?? 0
        var head = "5-hour window"
        if let r = snapshot.fiveReset { head += "  \(Fmt.time(r.addingTimeInterval(-Analytics.window))) → \(Fmt.time(r))" }
        t(head, pad, y + 10, fBold, th.muted)
        let ps = Fmt.pct(snapshot.fivePct)
        tRight(ps, W - pad, y - 4, fBig, th.levelText(pct))
        y += 36
        roundBar(NSRect(x: pad, y: y, width: W - 2*pad, height: 9), pct/100, th.levelFill(pct), th)
        y += 18
        drawPill(th, y, W)
        if let r = snapshot.fiveReset {
            let s = r > snapshot.apiLive ? "resets in \(Fmt.duration(r.timeIntervalSince(Date())))" : "reset"
            t(s, pad, y + 1, f, th.muted)
        }
        y += 24
    }

    private func drawPill(_ th: Theme, _ y: CGFloat, _ W: CGFloat) {
        let age = Int(Date().timeIntervalSince(snapshot.apiLive))
        let text = age < 75 ? "official · just now" : "official · \(Fmt.duration(TimeInterval(age))) ago"
        let sz = size(text, fSmall)
        let w = 18 + sz.width + 10, h: CGFloat = 17
        let r = NSRect(x: W - pad - w, y: y - 1, width: w, height: h)
        fillRound(r, h/2, th.card)
        dot(th.okFill, x: r.minX + 8, y: r.midY - 3, d: 6)
        t(text, r.minX + 18, r.midY - sz.height/2, fSmall, th.okText)
    }

    private func drawChart(_ th: Theme, _ y: inout CGFloat, _ W: CGFloat) {
        t("Burn-up — this window", pad, y, fBold, th.text); y += 22
        let area = NSRect(x: pad, y: y, width: W - 2*pad, height: 120)
        BurnUp.draw(snapshot, area, th, self)
        y += area.height + 6
        // legend
        var x = pad
        x = swatch(th.levelFill(snapshot.fivePct ?? 0), "actual", x, y, dashed: false)
        x = swatch(th.levelFill(snapshot.fivePct ?? 0), "forecast", x + 8, y, dashed: true)
        x = swatch(th.axis, "pace", x + 8, y, dashed: false)
        _ = swatch(th.dangerFill, "limit", x + 8, y, dashed: true)
        tRight("\(snapshot.burnPct.count) points", W - pad, y, fSmall, th.faint)
        y += 18
        line(th, y); y += 14
    }

    private func drawSeven(_ th: Theme, _ y: inout CGFloat, _ W: CGFloat) {
        let pct = snapshot.sevenPct ?? 0
        var head = "7-day window"
        if let r = snapshot.sevenReset { head += "  resets \(Fmt.dayTime(r))" }
        t(head, pad, y, fBold, th.muted)
        tRight(Fmt.pct(snapshot.sevenPct), W - pad, y, fBold, th.levelText(pct))
        y += 20
        roundBar(NSRect(x: pad, y: y, width: W - 2*pad, height: 7), pct/100, th.levelFill(pct), th)
        y += 12
        if let r = snapshot.sevenReset, r > Date() {
            t("resets in \(Fmt.duration(r.timeIntervalSince(Date())))", pad, y, f, th.muted)
        }
        y += 20; line(th, y); y += 14
    }

    private func drawModels(_ th: Theme, _ y: inout CGFloat, _ W: CGFloat) {
        let hasO = snapshot.opusPct != nil, hasS = snapshot.sonnetPct != nil
        if !hasO && !hasS { t("7-day by model: not in this reading", pad, y, fSmall, th.faint); return }
        t("7-day · by model", pad, y, fBold, th.text); y += 22
        if let o = snapshot.opusPct { modelRow(th, "Opus", o, &y, W) }
        if let s = snapshot.sonnetPct { modelRow(th, "Sonnet", s, &y, W) }
    }

    private func modelRow(_ th: Theme, _ name: String, _ pct: Double, _ y: inout CGFloat, _ W: CGFloat) {
        t(name, pad, y, f, th.text)
        let a = Fmt.pct(pct); let asz = size(a, f)
        tRight(a, W - pad, y, f, th.muted)
        let tx = pad + 62
        let tw = W - pad - tx - asz.width - 10
        if tw > 10 { roundBar(NSRect(x: tx, y: y + 6, width: tw, height: 5), pct/100, th.levelFill(pct), th) }
        y += 20
    }

    private func drawFooter(_ th: Theme, _ W: CGFloat) {
        let y = bounds.height - 22
        line(th, y - 8)
        t("official API · read \(Fmt.time(snapshot.apiLive))", pad, y, fSmall, th.faint)
        tRight("\(snapshot.sampleCount) local records", W - pad, y, fSmall, th.faint)
    }

    // ---- primitives ----

    func t(_ s: String, _ x: CGFloat, _ y: CGFloat, _ font: NSFont, _ color: NSColor) {
        (s as NSString).draw(at: NSPoint(x: x, y: y), withAttributes: [.font: font, .foregroundColor: color])
    }
    func tRight(_ s: String, _ rightX: CGFloat, _ y: CGFloat, _ font: NSFont, _ color: NSColor) {
        t(s, rightX - size(s, font).width, y, font, color)
    }
    private func center(_ s: String, _ color: NSColor, _ font: NSFont, _ W: CGFloat, _ y: CGFloat) {
        t(s, (W - size(s, font).width)/2, y, font, color)
    }
    func size(_ s: String, _ font: NSFont) -> NSSize { (s as NSString).size(withAttributes: [.font: font]) }

    private func line(_ th: Theme, _ y: CGFloat) {
        th.divider.setStroke()
        let p = NSBezierPath(); p.lineWidth = 1
        p.move(to: NSPoint(x: pad, y: y + 0.5)); p.line(to: NSPoint(x: bounds.width - pad, y: y + 0.5)); p.stroke()
    }
    private func accentDot(_ th: Theme, x: CGFloat, y: CGFloat) { dot(th.accent, x: x, y: y, d: 9) }
    func dot(_ color: NSColor, x: CGFloat, y: CGFloat, d: CGFloat) {
        color.setFill(); NSBezierPath(ovalIn: NSRect(x: x, y: y, width: d, height: d)).fill()
    }
    func fillRound(_ r: NSRect, _ radius: CGFloat, _ color: NSColor) {
        color.setFill(); NSBezierPath(roundedRect: r, xRadius: radius, yRadius: radius).fill()
    }
    private func roundBar(_ r: NSRect, _ frac: Double, _ fill: NSColor, _ th: Theme) {
        fillRound(r, r.height/2, th.track)
        guard frac > 0 else { return }
        var w = CGFloat(min(1.0, frac)) * r.width
        if w < r.height { w = r.height }
        fillRound(NSRect(x: r.minX, y: r.minY, width: w, height: r.height), r.height/2, fill)
    }
    private func swatch(_ color: NSColor, _ label: String, _ x: CGFloat, _ y: CGFloat, dashed: Bool) -> CGFloat {
        color.setStroke()
        let p = NSBezierPath(); p.lineWidth = 2.4
        if dashed { p.setLineDash([4, 3], count: 2, phase: 0) }
        p.move(to: NSPoint(x: x, y: y + 7)); p.line(to: NSPoint(x: x + 13, y: y + 7)); p.stroke()
        t(label, x + 16, y, fSmall, theme.muted)
        return x + 16 + size(label, fSmall).width
    }

    override func mouseDown(with event: NSEvent) {
        let p = convert(event.locationInWindow, from: nil)
        if !snapshot.loggedIn && loginRect.contains(p) { onLogin?() }
    }
}
