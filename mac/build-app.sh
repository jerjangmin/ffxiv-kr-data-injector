#!/bin/bash
# Assembles "FF14 한글.app" (launcher + management menu + updater + Mac tool source) into publish/mac/.
# The app builds the tool itself on first run from the latest upstream release.
# --install copies it to /Applications and removes the older "FFXIV 한글패치.app".
set -euo pipefail
MAC="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$MAC/.." && pwd)"
NAME="FF14 한글.app"
APP="$REPO/publish/mac/$NAME"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/tool"
cp "$MAC/app/Info.plist" "$APP/Contents/Info.plist"
cp "$MAC/app/launcher.sh" "$APP/Contents/MacOS/launcher"
cp "$MAC/app/menu.command" "$MAC/app/update.sh" "$APP/Contents/Resources/"
rsync -a --exclude bin --exclude obj "$MAC/FFXIVInjector.Mac" "$APP/Contents/Resources/tool/"
# Reuse the game's own icon so the Dock entry looks like FF14 (read-only copy; the game bundle is not changed).
ICON="/Applications/FINAL FANTASY XIV ONLINE.app/Contents/Resources/CrossOverOEM.icns"
[ -f "$ICON" ] && cp "$ICON" "$APP/Contents/Resources/AppIcon.icns"
chmod +x "$APP/Contents/MacOS/launcher" "$APP/Contents/Resources/menu.command" "$APP/Contents/Resources/update.sh"
plutil -lint "$APP/Contents/Info.plist" >/dev/null
echo "앱 생성: $APP"
if [ "${1:-}" = "--install" ]; then
  rm -rf "/Applications/$NAME" "/Applications/FFXIV 한글패치.app"
  cp -R "$APP" "/Applications/"
  echo "설치: /Applications/$NAME"
fi
