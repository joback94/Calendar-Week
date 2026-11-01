// Draws the app icon (a calendar page with one week highlighted) and writes:
//   ../macos/AppIcon.icns   (Finder / Launchpad icon of WeekNumber.app)
//   ../WeekNumber.ico       (file icon of WeekNumber.exe)
//   preview.png             (1024 px, macOS style, to look at)
// Run on a Mac from anywhere: swift icon/make-icon.swift
import AppKit

let here = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
let root = here.deletingLastPathComponent()

func rgb(_ hex: Int, _ alpha: CGFloat = 1) -> CGColor {
    CGColor(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255, alpha: alpha)
}

// macOS icons sit inside a 824/1024 rounded square with room for a shadow; Windows icons fill the canvas.
func render(_ px: Int, mac: Bool) -> Data {
    let ctx = CGContext(data: nil, width: px, height: px, bitsPerComponent: 8, bytesPerRow: 0,
                        space: CGColorSpace(name: CGColorSpace.sRGB)!,
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    let s = CGFloat(px)
    let inset = mac ? s * 100 / 1024 : (px <= 32 ? 0.5 : s / 32)
    let card = CGRect(x: inset, y: inset, width: s - 2 * inset, height: s - 2 * inset)
    let w = card.width
    let small = px <= 48
    let radius = w * (mac ? 0.225 : 0.18)
    let shape = CGPath(roundedRect: card, cornerWidth: radius, cornerHeight: radius, transform: nil)

    // Page, with a soft drop shadow on macOS.
    ctx.saveGState()
    if mac { ctx.setShadow(offset: CGSize(width: 0, height: -s * 0.012), blur: s * 0.03, color: rgb(0x000000, 0.3)) }
    ctx.addPath(shape)
    ctx.setFillColor(rgb(0xFFFFFF))
    ctx.fillPath()
    ctx.restoreGState()

    ctx.saveGState()
    ctx.addPath(shape)
    ctx.clip()
    let space = CGColorSpace(name: CGColorSpace.sRGB)!
    let paper = CGGradient(colorsSpace: space, colors: [rgb(0xFFFFFF), rgb(0xEEF1F6)] as CFArray, locations: [0, 1])!
    ctx.drawLinearGradient(paper, start: CGPoint(x: 0, y: card.maxY), end: CGPoint(x: 0, y: card.minY), options: [])

    // Blue header band.
    let headerH = w * (small ? 0.30 : 0.27)
    let header = CGRect(x: card.minX, y: card.maxY - headerH, width: w, height: headerH)
    ctx.saveGState()
    ctx.clip(to: header)
    let blue = CGGradient(colorsSpace: space, colors: [rgb(0x4F8EF7), rgb(0x2259D6)] as CFArray, locations: [0, 1])!
    ctx.drawLinearGradient(blue, start: CGPoint(x: 0, y: header.maxY), end: CGPoint(x: 0, y: header.minY), options: [])
    ctx.restoreGState()
    ctx.restoreGState()

    // Binder rings across the header edge.
    if !small || px >= 32 {
        let ringW = w * 0.075, ringH = w * 0.15
        for fx in [0.3, 0.7] as [CGFloat] {
            let r = CGRect(x: card.minX + w * fx - ringW / 2, y: card.maxY - headerH * 0.55 - ringH / 2 + headerH * 0.1,
                           width: ringW, height: ringH)
            ctx.addPath(CGPath(roundedRect: r, cornerWidth: ringW / 2, cornerHeight: ringW / 2, transform: nil))
            ctx.setFillColor(rgb(0xFFFFFF, 0.92))
            ctx.fillPath()
        }
    }

    // Day grid: rows of days, the second row is "this week".
    let padX = w * 0.11
    let top = header.minY - w * (small ? 0.09 : 0.07)
    let bottom = card.minY + w * (small ? 0.10 : 0.09)
    let rows = small ? 3 : 4
    let rowPitch = (top - bottom) / CGFloat(rows)
    let highlighted = 1
    let gridW = w - 2 * padX
    for row in 0..<rows {
        let cy = top - rowPitch * (CGFloat(row) + 0.5)
        if row == highlighted {
            let h = rowPitch * (small ? 0.78 : 0.86)
            let pill = CGRect(x: card.minX + padX - w * 0.035, y: cy - h / 2, width: gridW + w * 0.07, height: h)
            ctx.addPath(CGPath(roundedRect: pill, cornerWidth: h / 2, cornerHeight: h / 2, transform: nil))
            ctx.setFillColor(rgb(0xF97316))
            ctx.fillPath()
        }
        if small {
            // Too few pixels for single days: a bar per week.
            if row != highlighted {
                let h = rowPitch * 0.42
                let bar = CGRect(x: card.minX + padX, y: cy - h / 2, width: gridW, height: h)
                ctx.addPath(CGPath(roundedRect: bar, cornerWidth: h / 2, cornerHeight: h / 2, transform: nil))
                ctx.setFillColor(rgb(0xC9D1DE))
                ctx.fillPath()
            }
            continue
        }
        let pitch = gridW / 7
        let d = min(pitch, rowPitch) * 0.56
        for col in 0..<7 {
            let cx = card.minX + padX + pitch * (CGFloat(col) + 0.5)
            let dot = CGRect(x: cx - d / 2, y: cy - d / 2, width: d, height: d)
            ctx.addPath(CGPath(roundedRect: dot, cornerWidth: d * 0.3, cornerHeight: d * 0.3, transform: nil))
            ctx.setFillColor(row == highlighted ? rgb(0xFFFFFF) : rgb(0xC9D1DE))
            ctx.fillPath()
        }
    }

    let rep = NSBitmapImageRep(cgImage: ctx.makeImage()!)
    return rep.representation(using: .png, properties: [:])!
}

// macOS: iconset → icns.
let iconset = FileManager.default.temporaryDirectory.appendingPathComponent("AppIcon.iconset")
try? FileManager.default.removeItem(at: iconset)
try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)
for pt in [16, 32, 128, 256, 512] {
    try render(pt, mac: true).write(to: iconset.appendingPathComponent("icon_\(pt)x\(pt).png"))
    try render(pt * 2, mac: true).write(to: iconset.appendingPathComponent("icon_\(pt)x\(pt)@2x.png"))
}
let icns = root.appendingPathComponent("macos/AppIcon.icns")
let iconutil = Process()
iconutil.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
iconutil.arguments = ["-c", "icns", iconset.path, "-o", icns.path]
try iconutil.run()
iconutil.waitUntilExit()
precondition(iconutil.terminationStatus == 0, "iconutil failed")

// Windows: .ico with PNG entries (Windows Vista and later).
let sizes = [16, 20, 24, 32, 40, 48, 64, 256]
let images = sizes.map { render($0, mac: false) }
var ico = Data()
func le16(_ v: Int) { ico.append(contentsOf: [UInt8(v & 0xFF), UInt8(v >> 8 & 0xFF)]) }
func le32(_ v: Int) { le16(v & 0xFFFF); le16(v >> 16) }
le16(0); le16(1); le16(sizes.count)
var offset = 6 + 16 * sizes.count
for (size, png) in zip(sizes, images) {
    ico.append(UInt8(size % 256)); ico.append(UInt8(size % 256)) // 0 means 256
    ico.append(0); ico.append(0)
    le16(1); le16(32)
    le32(png.count); le32(offset)
    offset += png.count
}
images.forEach { ico.append($0) }
try ico.write(to: root.appendingPathComponent("WeekNumber.ico"))

try render(1024, mac: true).write(to: here.appendingPathComponent("preview.png"))
print("Wrote \(icns.path), \(root.appendingPathComponent("WeekNumber.ico").path), preview.png")
