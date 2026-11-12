#!/bin/bash
# Fetches the latest upstream release source (dambyul/ffxiv-kr-data-injector) and rebuilds the Mac tool on it.
# Keeps the previous build when anything fails. Prints the tool path on the last line of stdout ("BIN=<path>").
set -u
RES="$(cd "$(dirname "$0")" && pwd)"
if [ -d "$RES/tool/FFXIVInjector.Mac" ]; then TOOL_ROOT="$RES/tool"; else TOOL_ROOT="$(cd "$RES/.." && pwd)"; fi
STATE="${FFXIV_KR_STATE:-$HOME/Library/Application Support/FFXIVInjector-mac}"
REPO="dambyul/ffxiv-kr-data-injector"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
mkdir -p "$STATE/upstream" "$STATE/bin" "$STATE/toolsrc"

current="$(cat "$STATE/bin/current" 2>/dev/null || true)"   # "<tag>-<toolhash>"
current_bin="$STATE/bin/$current/ffxiv-kr-mac"
keep_current() {
  if [ -n "$current" ] && [ -x "$current_bin" ]; then
    echo "$1 기존 도구($current)를 그대로 씁니다."
    echo "BIN=$current_bin"; exit 0
  fi
  echo "$1 쓸 수 있는 도구가 없습니다." >&2; exit 1
}

tool_hash="$(cd "$TOOL_ROOT/FFXIVInjector.Mac" && find . -type f \( -name '*.cs' -o -name '*.csproj' \) -not -path './bin/*' -not -path './obj/*' | LC_ALL=C sort | xargs shasum -a 256 | shasum -a 256 | cut -c1-12)"

tag="${FFXIV_KR_UPSTREAM_TAG:-}"
if [ -z "$tag" ]; then
  tag="$(curl -fsSL --max-time 20 -H 'Accept: application/vnd.github+json' "https://api.github.com/repos/$REPO/releases/latest" \
    | sed -n 's/.*"tag_name"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -1)"
fi
case "$tag" in ''|*[!A-Za-z0-9._-]*) keep_current "업스트림 최신 릴리스를 확인하지 못했습니다(인터넷 연결 확인)." ;; esac
want="$tag-$tool_hash"

if [ "$current" = "$want" ] && [ -x "$current_bin" ]; then
  echo "도구 최신 상태: 업스트림 $tag"
else
  echo "업스트림 $tag 소스로 도구를 빌드합니다(1분 안팎)..."
  DOTNET="$(command -v dotnet || true)"
  [ -z "$DOTNET" ] && [ -x "$HOME/.dotnet/dotnet" ] && DOTNET="$HOME/.dotnet/dotnet"
  [ -z "$DOTNET" ] && [ -x /usr/local/share/dotnet/dotnet ] && DOTNET=/usr/local/share/dotnet/dotnet
  if [ -z "$DOTNET" ] || ! "$DOTNET" --list-sdks 2>/dev/null | grep -q '^10\.'; then
    echo "빌드에 .NET 10 SDK가 필요합니다. 관리자 권한 없이 ~/.dotnet에 설치할 수 있습니다(약 200MB)."
    if [ -t 0 ]; then read -r -p "설치할까요? (y/N) " ok; else ok=n; fi
    [ "$ok" = "y" ] || [ "$ok" = "Y" ] || keep_current ".NET SDK가 없어 빌드하지 못했습니다."
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$STATE/dotnet-install.sh" \
      && bash "$STATE/dotnet-install.sh" --channel 10.0 --install-dir "$HOME/.dotnet" \
      || keep_current ".NET SDK 설치에 실패했습니다."
    DOTNET="$HOME/.dotnet/dotnet"
  fi

  src="$STATE/upstream/$tag"
  if [ ! -f "$src/FFXIVInjector.Core/FFXIVInjector.Core.csproj" ]; then
    rm -rf "$src.tmp" && mkdir -p "$src.tmp"
    ( set -o pipefail; curl -fsSL --max-time 300 "https://codeload.github.com/$REPO/tar.gz/refs/tags/$tag" | tar -xz -C "$src.tmp" --strip-components 1 ) \
      && [ -f "$src.tmp/FFXIVInjector.Core/FFXIVInjector.Core.csproj" ] \
      || { rm -rf "$src.tmp"; keep_current "업스트림 $tag 소스를 받지 못했습니다."; }
    rm -rf "$src" && mv "$src.tmp" "$src"
  fi
  # Secrets.cs is untracked upstream. The tool passes the resource URL itself, so the example file only has to compile.
  [ -f "$src/FFXIVInjector.Core/Secrets.cs" ] || cp "$src/FFXIVInjector.Core/Secrets.Example.cs" "$src/FFXIVInjector.Core/Secrets.cs"

  build="$STATE/toolsrc/$want"
  rm -rf "$build" && mkdir -p "$build"
  rsync -a --exclude bin --exclude obj "$TOOL_ROOT/FFXIVInjector.Mac" "$build/"
  case "$(uname -m)" in arm64) rid=osx-arm64 ;; *) rid=osx-x64 ;; esac
  out="$STATE/bin/$want"
  rm -rf "$out.tmp"
  if ! "$DOTNET" publish "$build/FFXIVInjector.Mac/FFXIVInjector.Mac.csproj" -c Release -r "$rid" --self-contained true \
      -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none \
      -p:UpstreamRoot="$src" -p:UpstreamTag="$tag" -o "$out.tmp" -nologo -v quiet > "$build/build.log" 2>&1 \
      || [ "$("$out.tmp/ffxiv-kr-mac" version 2>/dev/null)" != "$tag" ]; then
    rm -rf "$out.tmp"
    keep_current "업스트림 $tag로 빌드하지 못했습니다(기록: $build/build.log)."
  fi
  rm -rf "$out" && mv "$out.tmp" "$out"
  echo "$want" > "$STATE/bin/current.tmp" && mv "$STATE/bin/current.tmp" "$STATE/bin/current"
  echo "도구 빌드 완료: 업스트림 $tag"
  # Keep the new build and the one before it; drop older builds and sources.
  for dir in "$STATE/bin"/*/ "$STATE/toolsrc"/*/; do
    name="$(basename "$dir")"
    [ "$name" = "$want" ] || [ "$name" = "$current" ] || rm -rf "$dir"
  done
  for dir in "$STATE/upstream"/*/; do
    name="$(basename "$dir")"
    [ "$name" = "$tag" ] || [ "$name" = "${current%-*}" ] || rm -rf "$dir"
  done
  current="$want"; current_bin="$out/ffxiv-kr-mac"
fi

# The resource URL is compiled into each upstream release; re-read it whenever the release changes.
if ! grep -q "\"ResourceUrlSource\": \"$tag\"" "$STATE/config.json" 2>/dev/null; then
  "$current_bin" refresh-url --tag "$tag" || echo "리소스 주소를 새로 읽지 못했습니다. 저장된 주소를 씁니다."
fi
echo "BIN=$current_bin"
