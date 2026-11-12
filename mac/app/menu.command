#!/bin/bash
# FF14 맥 한글패치(텍스트·폰트) 메뉴. 시작할 때마다 업스트림 최신 릴리스로 도구를 맞춘다.
RES="$(cd "$(dirname "$0")" && pwd)"

refresh_tool() {
  local out
  out="$("$RES/update.sh" | tee /dev/stderr)" || true
  BIN="$(printf '%s\n' "$out" | sed -n 's/^BIN=//p' | tail -1)"
}
pause() { echo; read -r -p "엔터를 누르면 메뉴로 돌아갑니다."; }
confirm() { read -r -p "$1 (y/N) " ok; [ "$ok" = "y" ] || [ "$ok" = "Y" ]; }

clear
echo "=== FF14 맥 한글패치 (텍스트·폰트) ==="
refresh_tool
if [ -z "$BIN" ] || [ ! -x "$BIN" ]; then
  echo; echo "도구를 준비하지 못했습니다. 위 메시지를 확인하세요."
  read -r -p "엔터를 누르면 닫힙니다."; exit 1
fi

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
  echo "  5) 도구 업데이트 확인"
  echo "  q) 종료"
  echo
  read -r -p "선택: " choice
  echo
  case "$choice" in
    1)
      echo "주의: 게임 데이터 수정은 스퀘어에닉스 이용약관 위반이며 제재 위험이 0은 아닙니다."
      echo "      게임과 런처가 완전히 꺼져 있어야 합니다."
      if confirm "계속할까요?"; then "$BIN" backup && "$BIN" apply; else echo "취소했습니다."; fi
      pause ;;
    2) "$BIN" restore; pause ;;
    3) "$BIN" verify; pause ;;
    4) if confirm "현재 버전이 아닌 백업을 지울까요?"; then "$BIN" prune --yes; fi; pause ;;
    5) refresh_tool; pause ;;
    q|Q) exit 0 ;;
    *) ;;
  esac
done
