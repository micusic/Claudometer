import AppKit

/// Burn-up chart: % used across the fixed 5-hour window. Real readings (solid), a dashed forecast,
/// the pace line, and the 100% ceiling — same design as the Windows build.
enum BurnUp {
    static func draw(_ s: Snapshot, _ area: NSRect, _ th: Theme, _ v: PanelView) {
        let font = NSFont.systemFont(ofSize: 9.5)
        let leftPad: CGFloat = 32, bottomPad: CGFloat = 15
        let x0 = area.minX + leftPad
        let top = area.minY, bottom = area.maxY - bottomPad
        let w = area.maxX - x0, h = bottom - top
        let winMin = Snapshot.windowMinutes

        func X(_ m: Double) -> CGFloat { x0 + CGFloat(max(0, min(winMin, m)) / winMin) * w }
        func Y(_ p: Double) -> CGFloat { bottom - CGFloat(max(0, min(100, p)) / 100) * h }

        // y grid + labels
        for k in 0...4 {
            let val = Double(k) * 25
            let yy = Y(val)
            th.grid.setStroke()
            let g = NSBezierPath(); g.lineWidth = 1
            g.move(to: NSPoint(x: x0, y: yy)); g.line(to: NSPoint(x: area.maxX, y: yy)); g.stroke()
            v.tRight("\(Int(val))%", x0 - 4, yy - 7, font, th.axis)
        }

        // 100% ceiling (red dashed)
        strokeLine([NSPoint(x: x0, y: Y(100)), NSPoint(x: area.maxX, y: Y(100))], th.dangerFill, 1.4, dash: [5, 4])
        // pace line (0,0)->(reset,100)
        strokeLine([NSPoint(x: X(0), y: Y(0)), NSPoint(x: X(winMin), y: Y(100))], th.axis, 1.3, dash: nil)

        guard s.hasData, !s.burnPct.isEmpty else {
            v.t(s.loggedIn ? "Fetching usage…" : "Sign in to view",
                area.midX - 40, area.midY - 6, font, th.faint)
            xAxis(s, x0, winMin, w, bottom, v, th, font)
            return
        }

        let color = th.levelFill(s.fivePct ?? 0)

        // actual polyline
        if s.burnPct.count >= 2 {
            var pts = [NSPoint]()
            for i in 0..<s.burnPct.count { pts.append(NSPoint(x: X(s.burnMin[i]), y: Y(s.burnPct[i]))) }
            strokeLine(pts, color, 2.6, dash: nil)
        }
        let tip = NSPoint(x: X(s.burnMin.last ?? s.nowMinutes), y: Y(s.burnPct.last ?? 0))

        // forecast (dashed) from tip to endpoint
        if s.hasForecast {
            strokeLine([tip, NSPoint(x: X(s.forecastEndMin), y: Y(s.forecastEndPct))], color, 2.2, dash: [5, 3])
        }
        color.setFill(); NSBezierPath(ovalIn: NSRect(x: tip.x - 3.5, y: tip.y - 3.5, width: 7, height: 7)).fill()

        // now divider
        strokeLine([NSPoint(x: tip.x, y: top), NSPoint(x: tip.x, y: bottom)], th.divider, 1, dash: [3, 3])
        let nl = "now"; let nsz = v.size(nl, font)
        v.t(nl, min(max(x0, tip.x - nsz.width/2), area.maxX - nsz.width), top - 1, font, th.muted)

        xAxis(s, x0, winMin, w, bottom, v, th, font)
    }

    private static func xAxis(_ s: Snapshot, _ x0: CGFloat, _ winMin: Double, _ w: CGFloat,
                              _ bottom: CGFloat, _ v: PanelView, _ th: Theme, _ font: NSFont) {
        let start = (s.fiveReset ?? Date()).addingTimeInterval(-Analytics.window)
        var m: Double = 0
        while m <= winMin + 0.5 {
            let label = Fmt.time(start.addingTimeInterval(m * 60))
            let lx = x0 + CGFloat(m / winMin) * w
            let sz = v.size(label, font)
            v.t(label, min(max(x0, lx - sz.width/2), x0 + w - sz.width), bottom + 2, font, th.axis)
            m += 60
        }
    }

    private static func strokeLine(_ pts: [NSPoint], _ color: NSColor, _ width: CGFloat, dash: [CGFloat]?) {
        guard pts.count >= 2 else { return }
        color.setStroke()
        let p = NSBezierPath(); p.lineWidth = width; p.lineJoinStyle = .round; p.lineCapStyle = .round
        if let dash = dash { p.setLineDash(dash, count: dash.count, phase: 0) }
        p.move(to: pts[0]); for i in 1..<pts.count { p.line(to: pts[i]) }
        p.stroke()
    }
}
