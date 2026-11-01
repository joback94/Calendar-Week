# WeekNumber

Shows the ISO 8601 calendar week and/or today's date, plus a date calculator
("what date was 97 days ago?").

## Windows (`WeekNumber.cs`)

Tray icon. Right-click it for:

- **Show in tray icon** – calendar week and date (week above day of month), calendar week only,
  or date (day of month) only. Hover the icon for the full date.
- **Date calculator...** – also opens with a left-click on the icon: a small box where you type
  `-97` (97 days ago) or `97` (in 97 days) and see the full date and calendar week, or type a
  date (e.g. `25.12.2026`, in your regional format) to see how many days until or since it. Esc closes it.
- **Theme** – Same as Windows (follows light/dark mode), Light, Dark, or Eye comfort
  (warm sepia, no pure white, less blue light). Applies to the menu and the calculator.
- **Check for updates** – the app also checks by itself a couple of minutes after starting and
  twice a day; a newer version is downloaded, verified (SHA-256) and swapped in, then the app restarts.
- **Start with Windows**, **Exit**

**Download:** [WeekNumber.exe](https://github.com/joback94/Calendar-Week/raw/main/WeekNumber.exe)
is always the build of the latest `main`: after every push to `main`, GitHub Actions
rebuilds it, commits it in place of the previous one and publishes it as a GitHub Release
(`v1.0.<run number>`), which is where installed copies update from.
To build locally, run `build.ps1` (uses the C# compiler that ships with Windows).

## macOS (`macos/`)

Menu bar item with the same options. See [macos/README.md](macos/README.md).

**Download:** [WeekNumber-mac.zip](https://github.com/joback94/Calendar-Week/raw/main/WeekNumber-mac.zip)
(universal, signed with Developer ID and notarized; macOS 13 or later). Unzip and move
`WeekNumber.app` to Applications. Rebuilt by hand with `macos/build.sh`, not by CI.

## Icon

`icon/make-icon.swift` draws the app icon and writes `WeekNumber.ico` (Windows, built into the exe)
and `macos/AppIcon.icns` (Mac). Run it on a Mac with `swift icon/make-icon.swift` after changing the design.
