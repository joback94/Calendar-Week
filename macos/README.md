# WeekNumber for macOS

Menu bar item showing the ISO 8601 calendar week and/or today's date, e.g. `CW 42  Mon 12 Oct`.

Click it for:

- **Show in Menu Bar** – calendar week and date, calendar week only, or date only
- **Date Calculator…** – enter a number of days, pick "days ago" or "days from now",
  and get the full date and its calendar week (e.g. 97 days ago → `Saturday, 11 July 2026  ·  CW 28`)
- **Open at Login**
- **Quit**

Requires macOS 13 or later.

## Build and install

```sh
xcode-select --install      # once, if you don't have the Command Line Tools
./macos/build.sh
cp -R macos/build/WeekNumber.app /Applications/
open /Applications/WeekNumber.app
```

Enable "Open at Login" only after the app is in /Applications, so the login item
points at the copy you keep.
