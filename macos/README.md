# WeekNumber for macOS

Menu bar item showing the ISO 8601 calendar week and/or today's date, e.g. `CW 42  Mon 12 Oct`.

- **Left click**: date calculator. Type `-97` (97 days ago) or `97` (in 97 days) and read the
  full date and its calendar week, or type a date (e.g. `25.12.26`, in your regional format) to
  see how many days until or since it. Esc or clicking elsewhere closes it.
- **Right click** (or Control-click): menu
  - **Show in Menu Bar** – calendar week and date, calendar week only, or date only
  - **Theme** – Same as macOS (follows light/dark), Light, Dark, or Eye Comfort
    (warm sepia, no pure white, less blue light). Applies to the calculator; the menu
    follows Light/Dark.
  - **Date Calculator…**, **Open at Login**, **Quit**

Requires macOS 13 or later.

## Build

```sh
xcode-select --install      # once, if you don't have the Command Line Tools
./macos/build.sh
```

This writes `macos/build/WeekNumber.app` and `WeekNumber-mac.zip` at the repo root.

- With a **Developer ID Application** certificate in your keychain the app is signed with it
  automatically (`security find-identity -v -p codesigning` lists it).
- To also **notarize** (so it opens without warnings on any Mac after download), save your
  credentials once and pass the profile name:

  ```sh
  xcrun notarytool store-credentials WeekNumber --apple-id <email> --team-id <TEAMID>
  NOTARY_PROFILE=WeekNumber ./macos/build.sh
  ```

  `store-credentials` asks for an app-specific password (create one at appleid.apple.com).

## Install

```sh
cp -R macos/build/WeekNumber.app /Applications/
open /Applications/WeekNumber.app
```

Enable "Open at Login" only after the app is in /Applications, so the login item
points at the copy you keep.
