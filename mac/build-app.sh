#!/bin/bash
# Assembles "FFXIV 한글패치.app" (launcher + menu + updater + Mac tool source) into publish/mac/.
# The app builds the tool itself on first run from the latest upstream release. --install copies it to ~/Applications.
set -euo pipefail
MAC="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$MAC/.." && pwd)"
APP="$REPO/publish/mac/FFXIV 한글패치.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/tool"
cp "$MAC/app/Info.plist" "$APP/Contents/Info.plist"
cp "$MAC/app/launcher.sh" "$APP/Contents/MacOS/launcher"
cp "$MAC/app/menu.command" "$MAC/app/update.sh" "$APP/Contents/Resources/"
rsync -a --exclude bin --exclude obj "$MAC/FFXIVInjector.Mac" "$APP/Contents/Resources/tool/"
chmod +x "$APP/Contents/MacOS/launcher" "$APP/Contents/Resources/menu.command" "$APP/Contents/Resources/update.sh"
plutil -lint "$APP/Contents/Info.plist" >/dev/null
echo "앱 생성: $APP"
if [ "${1:-}" = "--install" ]; then
  mkdir -p "$HOME/Applications"
  rm -rf "$HOME/Applications/FFXIV 한글패치.app"
  cp -R "$APP" "$HOME/Applications/"
  echo "설치: $HOME/Applications/FFXIV 한글패치.app"
fi
