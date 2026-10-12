# WeekNumber for macOS

Menu bar item showing the ISO 8601 calendar week and today's date, e.g. `CW 42  Mon 12 Oct`.
Click it for "Open at Login" and Quit.

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
