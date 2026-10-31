import AppKit
import ServiceManagement

// ISO 8601: weeks start on Monday, week 1 contains the first Thursday of the year.
private func isoCalendar() -> Calendar {
    var calendar = Calendar(identifier: .iso8601)
    calendar.timeZone = .current
    return calendar
}

private enum DisplayMode: Int, CaseIterable {
    case both, week, date

    var title: String {
        switch self {
        case .both: return "Calendar Week and Date"
        case .week: return "Calendar Week Only"
        case .date: return "Date Only"
        }
    }
}

private enum ThemeMode: Int, CaseIterable {
    case system, light, dark, eyeComfort

    var title: String {
        switch self {
        case .system: return "Same as macOS"
        case .light: return "Light"
        case .dark: return "Dark"
        case .eyeComfort: return "Eye Comfort"
        }
    }
}

private func rgb(_ hex: Int) -> NSColor {
    NSColor(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255,
            green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255,
            alpha: 1)
}

// Colors for the calculator flyout; same values as the Windows version.
private struct Palette {
    let back: NSColor
    let input: NSColor
    let text: NSColor
    let muted: NSColor
    let border: NSColor
    let appearance: NSAppearance.Name

    static let light = Palette(back: rgb(0xFFFFFF), input: rgb(0xFFFFFF), text: rgb(0x1B1B1B),
                               muted: rgb(0x6E6E6E), border: rgb(0xCCCCCC), appearance: .aqua)
    static let dark = Palette(back: rgb(0x202020), input: rgb(0x2D2D2D), text: rgb(0xF2F2F2),
                              muted: rgb(0xA0A0A0), border: rgb(0x454545), appearance: .darkAqua)
    // Warm sepia: no pure white, little blue, softer contrast for long reading.
    static let eyeComfort = Palette(back: rgb(0xF4ECD8), input: rgb(0xFBF5E6), text: rgb(0x4B3A2A),
                                    muted: rgb(0x8C7A62), border: rgb(0xD8C7A6), appearance: .aqua)

    static func forTheme(_ theme: ThemeMode, systemIsDark: Bool) -> Palette {
        switch theme {
        case .system: return systemIsDark ? .dark : .light
        case .light: return .light
        case .dark: return .dark
        case .eyeComfort: return .eyeComfort
        }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let displayModeKey = "DisplayMode"
    private let themeKey = "Theme"

    private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    private let menu = NSMenu()
    private var modeItems: [NSMenuItem] = []
    private var themeItems: [NSMenuItem] = []
    private let loginItem = NSMenuItem(title: "Open at Login", action: #selector(toggleLogin), keyEquivalent: "")
    private var timer: Timer?
    private lazy var calculator = DateCalculatorController()

    private var displayMode: DisplayMode {
        get { DisplayMode(rawValue: UserDefaults.standard.integer(forKey: displayModeKey)) ?? .both }
        set { UserDefaults.standard.set(newValue.rawValue, forKey: displayModeKey) }
    }

    private var theme: ThemeMode {
        get { ThemeMode(rawValue: UserDefaults.standard.integer(forKey: themeKey)) ?? .system }
        set { UserDefaults.standard.set(newValue.rawValue, forKey: themeKey) }
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        if let button = statusItem.button {
            button.font = NSFont.monospacedDigitSystemFont(ofSize: NSFont.systemFontSize, weight: .regular)
            // Left click opens the calculator, right click (or Control-click) opens the menu.
            button.target = self
            button.action = #selector(statusItemClicked(_:))
            button.sendAction(on: [.leftMouseUp, .rightMouseUp])
        }

        let showItem = NSMenuItem(title: "Show in Menu Bar", action: nil, keyEquivalent: "")
        showItem.submenu = NSMenu()
        for mode in DisplayMode.allCases {
            let item = NSMenuItem(title: mode.title, action: #selector(selectDisplayMode(_:)), keyEquivalent: "")
            item.tag = mode.rawValue
            item.target = self
            showItem.submenu?.addItem(item)
            modeItems.append(item)
        }

        let themeItem = NSMenuItem(title: "Theme", action: nil, keyEquivalent: "")
        themeItem.submenu = NSMenu()
        for mode in ThemeMode.allCases {
            let item = NSMenuItem(title: mode.title, action: #selector(selectTheme(_:)), keyEquivalent: "")
            item.tag = mode.rawValue
            item.target = self
            themeItem.submenu?.addItem(item)
            themeItems.append(item)
        }

        let calculatorItem = NSMenuItem(title: "Date Calculator…", action: #selector(showCalculator), keyEquivalent: "")
        calculatorItem.target = self
        loginItem.target = self

        menu.addItem(showItem)
        menu.addItem(themeItem)
        menu.addItem(calculatorItem)
        menu.addItem(.separator())
        menu.addItem(loginItem)
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Quit", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        menu.delegate = self

        // Day changes, clock/time zone/locale changes and wake from sleep all trigger an
        // immediate refresh; the timer is only a safety net.
        let center = NotificationCenter.default
        for name in [Notification.Name.NSCalendarDayChanged,
                     .NSSystemClockDidChange,
                     .NSSystemTimeZoneDidChange,
                     NSLocale.currentLocaleDidChangeNotification] {
            center.addObserver(self, selector: #selector(refresh), name: name, object: nil)
        }
        NSWorkspace.shared.notificationCenter.addObserver(
            self, selector: #selector(refresh), name: NSWorkspace.didWakeNotification, object: nil)
        DistributedNotificationCenter.default().addObserver(
            self, selector: #selector(systemAppearanceChanged),
            name: Notification.Name("AppleInterfaceThemeChangedNotification"), object: nil)

        let timer = Timer(timeInterval: 60, target: self, selector: #selector(refresh), userInfo: nil, repeats: true)
        timer.tolerance = 10
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer

        applyTheme()
        refresh()
    }

    @objc private func refresh() {
        NSTimeZone.resetSystemTimeZone()
        let now = Date()
        let week = isoCalendar().component(.weekOfYear, from: now)

        let formatter = DateFormatter()
        formatter.setLocalizedDateFormatFromTemplate("EEEdMMM")
        let weekText = "CW \(week)"
        let dateText = formatter.string(from: now)

        switch displayMode {
        case .both: statusItem.button?.title = "\(weekText)  \(dateText)"
        case .week: statusItem.button?.title = weekText
        case .date: statusItem.button?.title = dateText
        }
    }

    @objc private func statusItemClicked(_ sender: NSStatusBarButton) {
        let event = NSApp.currentEvent
        if event?.type == .rightMouseUp || event?.modifierFlags.contains(.control) == true {
            calculator.hide()
            // Attach the menu only for this click, so a left click keeps opening the calculator.
            statusItem.menu = menu
            sender.performClick(nil)
            statusItem.menu = nil
        } else {
            showCalculator()
        }
    }

    @objc private func showCalculator() {
        guard let button = statusItem.button else { return }
        calculator.toggle(below: button)
    }

    func menuWillOpen(_ menu: NSMenu) {
        for item in modeItems {
            item.state = item.tag == displayMode.rawValue ? .on : .off
        }
        for item in themeItems {
            item.state = item.tag == theme.rawValue ? .on : .off
        }
        loginItem.state = SMAppService.mainApp.status == .enabled ? .on : .off
    }

    @objc private func selectDisplayMode(_ sender: NSMenuItem) {
        displayMode = DisplayMode(rawValue: sender.tag) ?? .both
        refresh()
    }

    @objc private func selectTheme(_ sender: NSMenuItem) {
        theme = ThemeMode(rawValue: sender.tag) ?? .system
        applyTheme()
    }

    @objc private func systemAppearanceChanged() {
        // The app's effective appearance updates slightly after the notification.
        perform(#selector(applyTheme), with: nil, afterDelay: 0.2)
    }

    // NSApp.appearance is never set, so its effective appearance is the system's.
    @objc private func applyTheme() {
        let systemIsDark = NSApp.effectiveAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
        let palette = Palette.forTheme(theme, systemIsDark: systemIsDark)
        menu.appearance = theme == .system ? nil : NSAppearance(named: palette.appearance)
        calculator.applyTheme(palette)
    }

    @objc private func toggleLogin() {
        let service = SMAppService.mainApp
        do {
            if service.status == .enabled {
                try service.unregister()
            } else {
                try service.register()
                if service.status == .requiresApproval {
                    SMAppService.openSystemSettingsLoginItems()
                }
            }
        } catch {
            NSApp.activate(ignoringOtherApps: true)
            let alert = NSAlert(error: error)
            alert.messageText = "Could not change the login item"
            alert.informativeText = "\(error.localizedDescription)\n\nMove WeekNumber.app to /Applications and try again."
            alert.runModal()
        }
        loginItem.state = service.status == .enabled ? .on : .off
    }
}

// Borderless panel that can take keyboard input and closes on Esc.
final class FlyoutPanel: NSPanel {
    var onCancel: (() -> Void)?

    override var canBecomeKey: Bool { true }

    override func cancelOperation(_ sender: Any?) {
        onCancel?()
    }
}

// Small flyout under the menu bar item: type -97 (97 days ago) or 97 (in 97 days) and read the full date.
@MainActor
final class DateCalculatorController: NSObject, NSTextFieldDelegate {
    private static let maxDays: UInt = 100_000
    private static let width: CGFloat = 300

    private let panel = FlyoutPanel(contentRect: NSRect(x: 0, y: 0, width: 300, height: 100),
                                    styleMask: [.borderless, .nonactivatingPanel],
                                    backing: .buffered, defer: false)
    private let container = NSView()
    private let inputFrame = NSView()
    private let daysField = NSTextField(string: "")
    private let resultLabel = NSTextField(wrappingLabelWithString: "")
    private let hintLabel = NSTextField(labelWithString: "-97 = 97 days ago     97 = in 97 days")
    private var hiddenAt = Date.distantPast

    override init() {
        super.init()
        panel.isFloatingPanel = true
        panel.level = .popUpMenu
        panel.hasShadow = true
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.isReleasedWhenClosed = false
        panel.hidesOnDeactivate = false
        panel.onCancel = { [weak self] in self?.hide() }

        container.wantsLayer = true
        container.layer?.cornerRadius = 10
        container.layer?.borderWidth = 1
        panel.contentView = container

        // Borderless field inside a frame view that draws the border, so both can take the theme's colors.
        inputFrame.wantsLayer = true
        inputFrame.layer?.cornerRadius = 5
        inputFrame.layer?.borderWidth = 1
        daysField.translatesAutoresizingMaskIntoConstraints = false
        daysField.isBezeled = false
        daysField.isBordered = false
        daysField.drawsBackground = true
        daysField.focusRingType = .none
        daysField.font = .systemFont(ofSize: 14)
        daysField.placeholderString = "-97"
        daysField.delegate = self
        inputFrame.addSubview(daysField)

        resultLabel.font = .systemFont(ofSize: 15, weight: .semibold)
        resultLabel.isSelectable = true
        resultLabel.preferredMaxLayoutWidth = Self.width - 24
        hintLabel.font = .systemFont(ofSize: 11)

        let stack = NSStackView(views: [inputFrame, resultLabel, hintLabel])
        stack.translatesAutoresizingMaskIntoConstraints = false
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 8
        stack.edgeInsets = NSEdgeInsets(top: 12, left: 12, bottom: 12, right: 12)
        container.addSubview(stack)

        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            stack.topAnchor.constraint(equalTo: container.topAnchor),
            stack.bottomAnchor.constraint(equalTo: container.bottomAnchor),
            stack.widthAnchor.constraint(equalToConstant: Self.width),
            inputFrame.widthAnchor.constraint(equalToConstant: Self.width - 24),
            inputFrame.heightAnchor.constraint(equalToConstant: 28),
            daysField.leadingAnchor.constraint(equalTo: inputFrame.leadingAnchor, constant: 6),
            daysField.trailingAnchor.constraint(equalTo: inputFrame.trailingAnchor, constant: -6),
            daysField.centerYAnchor.constraint(equalTo: inputFrame.centerYAnchor),
        ])

        NotificationCenter.default.addObserver(self, selector: #selector(panelDidResignKey),
                                               name: NSWindow.didResignKeyNotification, object: panel)
        update()
    }

    fileprivate func applyTheme(_ palette: Palette) {
        panel.appearance = NSAppearance(named: palette.appearance)
        container.layer?.backgroundColor = palette.back.cgColor
        container.layer?.borderColor = palette.border.cgColor
        inputFrame.layer?.backgroundColor = palette.input.cgColor
        inputFrame.layer?.borderColor = palette.border.cgColor
        daysField.backgroundColor = palette.input
        daysField.textColor = palette.text
        resultLabel.textColor = palette.text
        hintLabel.textColor = palette.muted
    }

    // Shows the flyout under the menu bar item, or hides it if it is open.
    func toggle(below button: NSStatusBarButton) {
        // Clicking the item while the flyout is open can hide it first; don't reopen on that same click.
        if panel.isVisible || Date().timeIntervalSince(hiddenAt) < 0.3 {
            hide()
            return
        }
        guard let anchor = button.window?.frame else { return }

        update()
        let size = fittingSize()
        let screen = (button.window?.screen ?? NSScreen.main)?.visibleFrame ?? anchor
        let x = max(screen.minX + 8, min(anchor.midX - size.width / 2, screen.maxX - size.width - 8))
        panel.setFrame(NSRect(x: x, y: anchor.minY - size.height - 6, width: size.width, height: size.height),
                       display: true)

        NSApp.activate(ignoringOtherApps: true)
        panel.makeKeyAndOrderFront(nil)
        panel.makeFirstResponder(daysField)
        daysField.selectText(nil)
    }

    func hide() {
        guard panel.isVisible else { return }
        hiddenAt = Date()
        panel.orderOut(nil)
    }

    @objc private func panelDidResignKey() {
        hide()
    }

    func controlTextDidChange(_ obj: Notification) {
        update()
        // A longer or wrapped result changes the height; grow downwards from the top edge.
        guard panel.isVisible else { return }
        let size = fittingSize()
        var frame = panel.frame
        frame.origin.y = frame.maxY - size.height
        frame.size = size
        panel.setFrame(frame, display: true)
    }

    private func fittingSize() -> NSSize {
        container.layoutSubtreeIfNeeded()
        return container.fittingSize
    }

    private func update() {
        let text = daysField.stringValue.trimmingCharacters(in: .whitespaces)
        var days = 0
        if !text.isEmpty {
            guard let value = Int(text), value.magnitude <= Self.maxDays else {
                resultLabel.stringValue = "Enter a whole number of days"
                return
            }
            days = value
        }
        let calendar = isoCalendar()
        guard let date = calendar.date(byAdding: .day, value: days, to: calendar.startOfDay(for: Date())) else { return }

        let formatter = DateFormatter()
        formatter.dateStyle = .full
        formatter.timeStyle = .none
        resultLabel.stringValue = "\(formatter.string(from: date))  ·  CW \(calendar.component(.weekOfYear, from: date))"
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
