#!/bin/sh
# Builds WeekNumber.app (universal: Apple Silicon + Intel) with the Swift compiler from
# the Xcode Command Line Tools. Install them once with: xcode-select --install
#
# Signing:
#   - If a "Developer ID Application" certificate is in your keychain, the app is signed
#     with it (hardened runtime). Pick a specific one with DEVELOPER_ID="Developer ID Application: …".
#   - Otherwise it gets an ad-hoc signature: fine on this Mac, blocked by Gatekeeper elsewhere.
# Notarization (needs Developer ID): set NOTARY_PROFILE to a profile saved once with
#   xcrun notarytool store-credentials <profile> --apple-id <email> --team-id <TEAMID>
# The result is ../WeekNumber-mac.zip, ready to commit and download. A notarized build also writes
# ../WeekNumber-mac.version: installed copies compare it with their own version and update themselves.
set -eu
cd "$(dirname "$0")"

APP=build/WeekNumber.app
ZIP=../WeekNumber-mac.zip
rm -rf build
mkdir -p "$APP/Contents/MacOS"
cp Info.plist "$APP/Contents/Info.plist"

# Version = number of commits, so every build from a newer checkout counts higher.
VERSION=$(git rev-list --count HEAD)
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $VERSION" "$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString 1.0.$VERSION" "$APP/Contents/Info.plist"

for arch in arm64 x86_64; do
    swiftc -O -target "$arch-apple-macos13.0" \
        -framework AppKit -framework ServiceManagement \
        -o "build/WeekNumber-$arch" WeekNumber.swift
done
lipo -create -output "$APP/Contents/MacOS/WeekNumber" build/WeekNumber-arm64 build/WeekNumber-x86_64
rm build/WeekNumber-arm64 build/WeekNumber-x86_64

if [ -z "${DEVELOPER_ID:-}" ]; then
    DEVELOPER_ID=$(security find-identity -v -p codesigning 2>/dev/null |
        sed -n 's/.*"\(Developer ID Application: .*\)"$/\1/p' | head -n 1)
fi

if [ -n "$DEVELOPER_ID" ]; then
    echo "Signing with: $DEVELOPER_ID"
    codesign --force --options runtime --timestamp --sign "$DEVELOPER_ID" "$APP"
else
    echo "No Developer ID certificate found: ad-hoc signature (runs only on this Mac without warnings)."
    codesign --force --sign - "$APP"
fi
codesign --verify --strict --verbose=2 "$APP"

rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"

if [ -n "${NOTARY_PROFILE:-}" ]; then
    if [ -z "$DEVELOPER_ID" ]; then
        echo "NOTARY_PROFILE is set but there is no Developer ID signature; cannot notarize." >&2
        exit 1
    fi
    echo "Notarizing (usually takes a few minutes)…"
    xcrun notarytool submit "$ZIP" --keychain-profile "$NOTARY_PROFILE" --wait
    xcrun stapler staple "$APP"
    # Re-zip so the download carries the stapled ticket and opens even offline.
    rm -f "$ZIP"
    ditto -c -k --keepParent "$APP" "$ZIP"
    spctl --assess --type execute --verbose "$APP"
    echo "$VERSION" > ../WeekNumber-mac.version
    echo "Version $VERSION: commit WeekNumber-mac.zip and WeekNumber-mac.version together."
fi

echo "Built $APP and $(cd .. && pwd)/WeekNumber-mac.zip"
echo "Install: cp -R $APP /Applications/ && open /Applications/WeekNumber.app"
