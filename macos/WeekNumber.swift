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

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let displayModeKey = "DisplayMode"

    private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    private let menu = NSMenu()
    private var modeItems: [NSMenuItem] = []
    private let loginItem = NSMenuItem(title: "Open at Login", action: #selector(toggleLogin), keyEquivalent: "")
    private var timer: Timer?
    private lazy var calculator = DateCalculatorController()

    private var displayMode: DisplayMode {
        get { DisplayMode(rawValue: UserDefaults.standard.integer(forKey: displayModeKey)) ?? .both }
        set { UserDefaults.standard.set(newValue.rawValue, forKey: displayModeKey) }
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem.button?.font = NSFont.monospacedDigitSystemFont(ofSize: NSFont.systemFontSize, weight: .regular)

        let showMenu = NSMenu()
        for mode in DisplayMode.allCases {
            let item = NSMenuItem(title: mode.title, action: #selector(selectDisplayMode(_:)), keyEquivalent: "")
            item.tag = mode.rawValue
            item.target = self
            showMenu.addItem(item)
            modeItems.append(item)
        }
        let showItem = NSMenuItem(title: "Show in Menu Bar", action: nil, keyEquivalent: "")
        showItem.submenu = showMenu

        let calculatorItem = NSMenuItem(title: "Date Calculator…", action: #selector(showCalculator), keyEquivalent: "d")
        calculatorItem.target = self
        loginItem.target = self

        menu.addItem(showItem)
        menu.addItem(calculatorItem)
        menu.addItem(.separator())
        menu.addItem(loginItem)
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Quit", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        menu.delegate = self
        statusItem.menu = menu

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

        let timer = Timer(timeInterval: 60, target: self, selector: #selector(refresh), userInfo: nil, repeats: true)
        timer.tolerance = 10
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer

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

    func menuWillOpen(_ menu: NSMenu) {
        for item in modeItems {
            item.state = item.tag == displayMode.rawValue ? .on : .off
        }
        loginItem.state = SMAppService.mainApp.status == .enabled ? .on : .off
    }

    @objc private func selectDisplayMode(_ sender: NSMenuItem) {
        displayMode = DisplayMode(rawValue: sender.tag) ?? .both
        refresh()
    }

    @objc private func showCalculator() {
        calculator.show()
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

// Small window answering "what date is N days ago / from now?".
@MainActor
final class DateCalculatorController: NSObject, NSTextFieldDelegate {
    private static let maxDays = 100_000

    private let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 420, height: 110),
                                  styleMask: [.titled, .closable], backing: .buffered, defer: false)
    private let daysField = NSTextField(string: "0")
    private let directionPopup = NSPopUpButton()
    private let resultLabel = NSTextField(wrappingLabelWithString: "")

    override init() {
        super.init()
        window.title = "Date Calculator"
        window.isReleasedWhenClosed = false

        daysField.delegate = self
        daysField.alignment = .right
        daysField.widthAnchor.constraint(equalToConstant: 90).isActive = true

        directionPopup.addItems(withTitles: ["days ago", "days from now"])
        directionPopup.target = self
        directionPopup.action = #selector(update)

        resultLabel.font = .systemFont(ofSize: 15, weight: .semibold)
        resultLabel.isSelectable = true
        resultLabel.preferredMaxLayoutWidth = 380

        let row = NSStackView(views: [daysField, directionPopup])
        row.orientation = .horizontal
        let stack = NSStackView(views: [row, resultLabel])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 14
        stack.edgeInsets = NSEdgeInsets(top: 20, left: 20, bottom: 20, right: 20)
        window.contentView = stack
        window.center()
    }

    func show() {
        update()
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
        window.makeFirstResponder(daysField)
    }

    func controlTextDidChange(_ obj: Notification) {
        update()
    }

    @objc private func update() {
        let text = daysField.stringValue.trimmingCharacters(in: .whitespaces)
        guard let days = Int(text), (0...Self.maxDays).contains(days) else {
            resultLabel.stringValue = "Enter a number of days from 0 to \(Self.maxDays)."
            return
        }
        let calendar = isoCalendar()
        let offset = directionPopup.indexOfSelectedItem == 0 ? -days : days
        guard let date = calendar.date(byAdding: .day, value: offset, to: calendar.startOfDay(for: Date())) else { return }

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
