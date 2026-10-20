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

GitHub Actions builds `WeekNumber.exe` on every push to `main`: open the latest
"Build Windows" run under the Actions tab and download the `WeekNumber` artifact.
To build locally, run `build.ps1` (uses the C# compiler that ships with Windows).

## macOS (`macos/`)

Menu bar item with the same options. See [macos/README.md](macos/README.md).
