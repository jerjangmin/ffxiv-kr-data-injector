#!/bin/bash
# 더블클릭하면 터미널에서 열리는 FF14 맥 한글패치(텍스트·폰트) 메뉴. 자세한 설명은 README-mac.md.
REPO="$(cd "$(dirname "$0")" && pwd)"
BIN="$REPO/publish/mac/ffxiv-kr-mac"

# 처음 실행했거나 소스가 바뀌었으면 빌드한다.
if [ ! -x "$BIN" ] || [ -n "$(find "$REPO/FFXIVInjector.Core" "$REPO/FFXIVInjector.MacCli" -name '*.cs' -newer "$BIN" 2>/dev/null | head -1)" ]; then
  echo "도구를 빌드합니다(처음 한 번, 1분 이내)..."
  "$REPO/mac/build.sh" || { read -r -p "빌드 실패. 엔터를 누르면 닫힙니다."; exit 1; }
fi

pause() { echo; read -r -p "엔터를 누르면 메뉴로 돌아갑니다."; }

while true; do
  clear
  echo "=== FF14 맥 한글패치 (텍스트·폰트) ==="
  echo
  "$BIN" status
  echo
  echo "  1) 한글 적용 (처음이면 순정 백업 후 적용)"
  echo "  2) 원복 (순정 파일로 되돌리기, 게임 업데이트 전에 필수)"
  echo "  3) 검증 (파일 무결성·한글 여부 검사)"
  echo "  4) 이전 버전 백업 정리"
  echo "  q) 종료"
  echo
  read -r -p "선택: " choice
  echo
  case "$choice" in
    1)
      echo "주의: 게임 데이터 수정은 스퀘어에닉스 이용약관 위반이며 제재 위험이 0은 아닙니다."
      echo "      게임과 런처가 완전히 꺼져 있어야 합니다."
      read -r -p "계속할까요? (y/N) " ok
      if [ "$ok" = "y" ] || [ "$ok" = "Y" ]; then
        "$BIN" backup && "$BIN" apply
      else
        echo "취소했습니다."
      fi
      pause ;;
    2) "$BIN" restore; pause ;;
    3) "$BIN" verify; pause ;;
    4)
      read -r -p "현재 버전이 아닌 백업을 지울까요? (y/N) " ok
      if [ "$ok" = "y" ] || [ "$ok" = "Y" ]; then "$BIN" prune --yes; fi
      pause ;;
    q|Q) exit 0 ;;
    *) ;;
  esac
done
