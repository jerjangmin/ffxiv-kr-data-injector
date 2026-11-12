#!/bin/bash
# Developer build: the Mac tool against this checkout (not the latest upstream release) into publish/mac/ffxiv-kr-mac.
# Users run "FFXIV 한글패치.app" (build-app.sh), which builds against the latest upstream release itself.
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET="$(command -v dotnet || true)"
[ -z "$DOTNET" ] && [ -x "$HOME/.dotnet/dotnet" ] && DOTNET="$HOME/.dotnet/dotnet"
if [ -z "$DOTNET" ]; then
  echo "빌드하려면 .NET 10 SDK가 필요합니다: https://dotnet.microsoft.com/download/dotnet/10.0" >&2
  exit 1
fi
[ -f "$REPO/FFXIVInjector.Core/Secrets.cs" ] || cp "$REPO/FFXIVInjector.Core/Secrets.Example.cs" "$REPO/FFXIVInjector.Core/Secrets.cs"
case "$(uname -m)" in arm64) RID=osx-arm64 ;; *) RID=osx-x64 ;; esac
"$DOTNET" publish "$REPO/mac/FFXIVInjector.Mac/FFXIVInjector.Mac.csproj" -c Release -r "$RID" \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$REPO/publish/mac" -nologo -v quiet
echo "빌드 완료: $REPO/publish/mac/ffxiv-kr-mac"
