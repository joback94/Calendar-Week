import AppKit
import ServiceManagement

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    private let menu = NSMenu()
    private let loginItem = NSMenuItem(title: "Open at Login", action: #selector(toggleLogin), keyEquivalent: "")
    private var timer: Timer?

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem.button?.font = NSFont.monospacedDigitSystemFont(ofSize: NSFont.systemFontSize, weight: .regular)

        loginItem.target = self

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

        // ISO 8601: weeks start on Monday, week 1 contains the first Thursday of the year.
        var iso = Calendar(identifier: .iso8601)
        iso.timeZone = .current
        let week = iso.component(.weekOfYear, from: now)

        let date = DateFormatter()
        date.setLocalizedDateFormatFromTemplate("EEEdMMM")

        statusItem.button?.title = "CW \(week)  \(date.string(from: now))"
    }

    func menuWillOpen(_ menu: NSMenu) {
        loginItem.state = SMAppService.mainApp.status == .enabled ? .on : .off
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

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
