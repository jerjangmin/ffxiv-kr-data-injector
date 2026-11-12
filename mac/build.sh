#!/bin/bash
# Builds the self-contained macOS CLI into publish/mac/ffxiv-kr-mac (no .NET needed to run it).
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET="$(command -v dotnet || true)"
[ -z "$DOTNET" ] && [ -x "$HOME/.dotnet/dotnet" ] && DOTNET="$HOME/.dotnet/dotnet"
if [ -z "$DOTNET" ]; then
  echo "빌드하려면 .NET 10 SDK가 필요합니다: https://dotnet.microsoft.com/download/dotnet/10.0" >&2
  exit 1
fi
if [ ! -f "$REPO/FFXIVInjector.Core/Secrets.cs" ]; then
  echo "FFXIVInjector.Core/Secrets.cs가 없습니다. Secrets.Example.cs를 복사해 리소스 주소를 넣으세요." >&2
  exit 1
fi
case "$(uname -m)" in arm64) RID=osx-arm64 ;; *) RID=osx-x64 ;; esac
"$DOTNET" publish "$REPO/FFXIVInjector.MacCli/FFXIVInjector.MacCli.csproj" -c Release -r "$RID" \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$REPO/publish/mac" -nologo -v quiet
echo "빌드 완료: $REPO/publish/mac/ffxiv-kr-mac"
