# WeekNumber for macOS

Menu bar item showing today's date and ISO 8601 calendar week, e.g. `Mon 12 Oct  ·  W42`.
Click it for the full date, "Calendar week 42 of 53", a toggle to hide the date
(week only), and "Open at Login".

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
