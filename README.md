# WeekNumber

Shows the ISO 8601 calendar week and/or today's date, plus a date calculator
("what date was 97 days ago?").

## Windows (`WeekNumber.cs`)

Tray icon. Right-click it for:

- **Show in tray icon** – calendar week and date (week above day of month), calendar week only,
  or date (day of month) only. Hover the icon for the full date.
- **Date calculator...** – also opens with a left-click on the icon: a small box where you type
  `-97` (97 days ago) or `97` (in 97 days) and see the full date and calendar week. Esc closes it.
- **Start with Windows**, **Exit**

**Download:** [WeekNumber.exe](https://github.com/joback94/Calendar-Week/raw/main/WeekNumber.exe)
is always the build of the latest `main`: after every push to `main`, GitHub Actions
rebuilds it and commits it in place of the previous one.
To build locally, run `build.ps1` (uses the C# compiler that ships with Windows).

## macOS (`macos/`)

Menu bar item with the same options. See [macos/README.md](macos/README.md).
