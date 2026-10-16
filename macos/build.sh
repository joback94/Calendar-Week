#!/bin/sh
# Builds WeekNumber.app (universal: Apple Silicon + Intel) with the Swift compiler from
# the Xcode Command Line Tools. Install them once with: xcode-select --install
set -eu
cd "$(dirname "$0")"

APP=build/WeekNumber.app
rm -rf build
mkdir -p "$APP/Contents/MacOS"
cp Info.plist "$APP/Contents/Info.plist"

for arch in arm64 x86_64; do
    swiftc -O -target "$arch-apple-macos13.0" \
        -framework AppKit -framework ServiceManagement \
        -o "build/WeekNumber-$arch" WeekNumber.swift
done
lipo -create -output "$APP/Contents/MacOS/WeekNumber" build/WeekNumber-arm64 build/WeekNumber-x86_64
rm build/WeekNumber-arm64 build/WeekNumber-x86_64

# Ad-hoc signature: required to run on Apple Silicon and for "Open at Login".
codesign --force --sign - "$APP"

echo "Built $APP"
echo "Install: cp -R $APP /Applications/ && open /Applications/WeekNumber.app"
