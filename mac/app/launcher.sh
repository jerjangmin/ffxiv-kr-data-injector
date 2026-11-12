#!/bin/bash
# "FF14 한글" entry point. Starts FF14 through the Korean patch tool so the launcher only ever updates pristine files:
#   - Korean applied, no maintenance since         -> start FF14
#   - maintenance since (possible client patch)    -> ask: update mode or just play
#   - update mode: restore -> start FF14 -> wait until it closes -> back up the new version -> apply -> offer to start
# Hold Option (⌥) while opening the app for the management menu (apply/restore/verify) in Terminal.
#
# Test hooks (no windows): FFXIV_KR_TEST_ANSWERS="button|button|..." answers dialogs in order and logs instead of
# showing UI; FFXIV_KR_FF14_OPEN replaces the command that starts FF14.
RES="$(cd "$(dirname "$0")/../Resources" && pwd)"
STATE="${FFXIV_KR_STATE:-$HOME/Library/Application Support/FFXIVInjector-mac}"
FF14_APP="/Applications/FINAL FANTASY XIV ONLINE.app"
TITLE="FF14 한글"
mkdir -p "$STATE/logs"
LOG="$STATE/logs/$(date +%Y%m%d-%H%M%S)-launcher.log"
exec >>"$LOG" 2>&1
echo "launcher start $(date)"
TEST_MODE=0
if [ -n "${FFXIV_KR_TEST_ANSWERS+x}" ]; then
  TEST_MODE=1
  ANSWER_FILE="$(mktemp)"; trap 'rm -f "$ANSWER_FILE"' EXIT
  printf '%s\n' "$FFXIV_KR_TEST_ANSWERS" | tr '|' '\n' > "$ANSWER_FILE"
fi

# dialog <message> <default button> <button>...  -> prints the chosen button ("" when closed)
dialog() {
  local msg="$1" def="$2"; shift 2
  echo "[dialog] $msg | buttons: $* | default: $def" >&2
  if [ "$TEST_MODE" = 1 ]; then
    # dialog runs inside $( ), so the queue lives in a file.
    local answer; answer="$(head -1 "$ANSWER_FILE")"
    tail -n +2 "$ANSWER_FILE" > "$ANSWER_FILE.next"; mv "$ANSWER_FILE.next" "$ANSWER_FILE"
    echo "[dialog] -> $answer" >&2; printf '%s' "$answer"; return
  fi
  osascript - "$msg" "$def" "$@" <<'APPLESCRIPT' 2>/dev/null
on run argv
  set msg to item 1 of argv
  set def to item 2 of argv
  set btns to items 3 thru -1 of argv
  try
    set r to display dialog msg with title "FF14 한글" buttons btns default button def with icon note
    return button returned of r
  on error
    return ""
  end try
end run
APPLESCRIPT
}

notify() {
  echo "[notify] $1"
  [ "$TEST_MODE" = 1 ] && return
  osascript - "$1" <<'APPLESCRIPT' >/dev/null 2>&1
on run argv
  display notification (item 1 of argv) with title "FF14 한글"
end run
APPLESCRIPT
}

# Non-blocking notice that stays until dismissed (the launcher keeps waiting meanwhile).
notice() {
  echo "[notice] $1"
  [ "$TEST_MODE" = 1 ] && return
  osascript - "$1" <<'APPLESCRIPT' >/dev/null 2>&1 &
on run argv
  display alert "FF14 한글" message (item 1 of argv)
end run
APPLESCRIPT
}

open_ff14() {
  echo "[open] FF14"
  if [ -n "${FFXIV_KR_FF14_OPEN:-}" ]; then bash -c "$FFXIV_KR_FF14_OPEN"; else /usr/bin/open "$FF14_APP"; fi
}

open_menu() {
  echo "[open] menu"
  [ "$TEST_MODE" = 1 ] && return
  /usr/bin/open -a Terminal "$RES/menu.command"
}

# Runs the tool; on failure shows the reason (last "중단:" / "오류:" line) and offers ways out.
run_tool() {
  local out code
  out="$("$BIN" "$@" 2>&1)"; code=$?
  printf '%s\n' "$out"
  LAST_ERROR="$(printf '%s\n' "$out" | grep -E '^(중단|오류): ' | tail -1)"
  return $code
}

fail() {
  local msg="$1"
  case "$(dialog "$msg

기록: $LOG" "닫기" "관리 메뉴" "FF14 그냥 실행" "닫기")" in
    "관리 메뉴") open_menu ;;
    "FF14 그냥 실행") open_ff14 ;;
  esac
  exit 1
}

apply_korean() {
  notify "한글을 적용하는 중입니다(약 1분)."
  run_tool backup && run_tool apply
}

update_mode() {
  local need_restore="$1"
  if [ "$need_restore" = 1 ]; then
    run_tool restore || fail "원복하지 못해 업데이트 모드를 시작할 수 없습니다.
${LAST_ERROR}"
  fi
  open_ff14
  notice "FF14 런처에서 업데이트를 받으세요(업데이트가 없으면 로그인 화면까지만).

끝나면 게임을 시작하지 말고 FF14를 종료(⌘Q)하세요. 종료를 확인하면 한글을 다시 적용합니다."
  run_tool wait-session --start-timeout 300 || fail "FF14 실행·종료를 확인하지 못했습니다. 게임 파일은 순정 상태입니다.
${LAST_ERROR}"
  if apply_korean; then
    [ "$(dialog "한글 적용을 마쳤습니다. FF14를 실행할까요?" "FF14 실행" "닫기" "FF14 실행")" = "FF14 실행" ] && open_ff14
    exit 0
  fi
  fail "한글을 적용하지 못했습니다. 게임 파일은 순정(영어) 상태라 그대로 플레이할 수 있습니다.
${LAST_ERROR}"
}

# Option (⌥) held at launch -> management menu.
if [ "$TEST_MODE" = 0 ] && [ "$(osascript -l JavaScript -e 'ObjC.import("AppKit"); ($.NSEvent.modifierFlags & 0x80000) ? "1" : "0"' 2>/dev/null)" = 1 ]; then
  open_menu; exit 0
fi

[ -f "$STATE/bin/current" ] || notify "처음 실행이라 한글패치 도구를 준비합니다(1분 안팎)."
BIN="$("$RES/update.sh" </dev/null | tee /dev/stderr | sed -n 's/^BIN=//p' | tail -1)"
if [ -z "$BIN" ] || [ ! -x "$BIN" ]; then
  if [ "$(dialog "한글패치 도구를 준비하지 못했습니다(인터넷 연결이나 .NET SDK 설치가 필요할 수 있습니다). 관리 메뉴에서 자세한 안내를 볼 수 있습니다." "관리 메뉴" "FF14 그냥 실행" "관리 메뉴")" = "관리 메뉴" ]; then
    open_menu
  else
    open_ff14
  fi
  exit 1
fi

PRE="$("$BIN" preflight 2>&1)"; echo "$PRE"
action="$(printf '%s\n' "$PRE" | sed -n 's/^action=//p')"
reason="$(printf '%s\n' "$PRE" | sed -n 's/^reason=//p')"
maint="$(printf '%s\n' "$PRE" | sed -n 's/^maintenance=//p')"
first="$(printf '%s\n' "$PRE" | sed -n 's/^first_run=//p')"

case "$action" in
  launch)
    open_ff14 ;;
  apply-then-launch)
    if apply_korean; then open_ff14; else fail "한글을 적용하지 못했습니다.
${LAST_ERROR}"; fi ;;
  ask-update)
    case "$(dialog "${reason}${maint:+
($maint)}

오늘 게임 패치가 있었다면 '업데이트 모드'를 고르세요. 한글을 잠시 원복하고 FF14 런처로 업데이트한 뒤 다시 적용합니다.
패치가 없었다면 '그냥 플레이'를 고르세요(이 점검은 다시 묻지 않습니다)." "업데이트 모드" "관리 메뉴" "그냥 플레이" "업데이트 모드")" in
      "업데이트 모드") update_mode 1 ;;
      "그냥 플레이") "$BIN" ack-maintenance; open_ff14 ;;
      "관리 메뉴") open_menu ;;
    esac ;;
  update)
    if [ "$first" = 1 ]; then
      intro="FF14에 한글 텍스트·폰트를 적용합니다.
주의: 게임 데이터 수정은 스퀘어에닉스 이용약관 위반이며 제재 위험이 0은 아닙니다.

"
    else
      intro="${reason}
"
    fi
    [ "$(dialog "${intro}먼저 FF14를 실행해 업데이트를 확인합니다. 업데이트를 받은 뒤(없으면 로그인 화면에서) 게임을 시작하지 말고 FF14를 종료하면 한글을 적용합니다." "계속" "취소" "계속")" = "계속" ] && update_mode 0 ;;
  *)
    if [ "$reason" = "FF14가 이미 실행 중입니다." ]; then
      dialog "$reason" "확인" "확인" >/dev/null
    else
      case "$(dialog "${reason:-상태를 확인하지 못했습니다.}" "닫기" "관리 메뉴" "FF14 그냥 실행" "닫기")" in
        "관리 메뉴") open_menu ;;
        "FF14 그냥 실행") open_ff14 ;;
      esac
    fi ;;
esac
exit 0
