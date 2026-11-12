# FF14 맥 한글패치 (텍스트·폰트)

macOS 공식 클라이언트(CrossOver 래퍼 앱)에 한글 텍스트와 폰트만 적용하는 도구입니다. 이미지·음성(dinput8 로더)은 맥에서 지원하지 않습니다.

> 게임 데이터 수정은 스퀘어에닉스 이용약관 위반입니다. 텍스트·폰트는 로컬 파일만 바꾸지만 제재 위험이 0은 아닙니다. 한글 UI가 보이는 스크린샷·방송 공유는 피하세요.

## 처음 한 번

1. 런처로 최신 패치까지 받은 뒤 **게임과 런처를 완전히 종료**합니다.
2. Finder에서 이 폴더의 `FFXIV-한글패치-맥.command`를 더블클릭합니다.
   - "확인되지 않은 개발자" 경고가 뜨면 우클릭 → 열기를 고르세요.
   - 처음 실행할 때 도구를 빌드합니다(.NET 10 SDK 필요, `~/.dotnet`에 있으면 자동으로 찾음). 빌드된 도구는 .NET 없이 돌아갑니다.
3. 메뉴에서 `1) 한글 적용`을 고르고 `y`를 입력합니다. 순정 백업(1초) → 리소스 내려받기 → 주입·검증(약 1분) → 교체 순서로 진행됩니다.
4. 게임을 켜서 타이틀·로비·UI가 한글인지 확인합니다. 글자가 깨지거나 튕기면 바로 `2) 원복`을 실행하세요.

## 게임 업데이트가 있을 때 (중요)

패치된 파일 위에서 런처가 업데이트하면 파일이 꼬일 수 있습니다. 정기 점검이나 패치 공지가 있는 날에는:

1. 런처를 켜기 **전에** `.command` → `2) 원복`
2. 런처로 업데이트 → 게임까지 한 번 켰다가 **완전히 종료**
3. `.command` → `1) 한글 적용` (새 버전을 자동으로 백업한 뒤 적용)
4. 잘 되면 `4) 이전 버전 백업 정리`

원복을 잊고 업데이트했다면 도구가 적용·원복을 거부합니다(게임 버전이 백업과 다르기 때문). 이때는 런처의 **파일 검사·복구**로 순정 상태를 만든 뒤 3번부터 하세요.

## 동작 방식

- 게임 파일 위치: `~/Library/Application Support/FINAL FANTASY XIV ONLINE/Bottles/published_Final_Fantasy/drive_c/Program Files (x86)/SquareEnix/FINAL FANTASY XIV - A Realm Reborn/game`. 앱 번들(`/Applications/...app`)은 건드리지 않습니다.
- 바꾸는 파일: `sqpack/ffxiv/000000.*`(폰트), `0a0000.*`(텍스트) 6개. `ffxivgame.ver`는 바꾸지 않습니다.
- 백업: 현재 버전의 순정 파일을 APFS 클론(`cp -c`)으로 `~/Library/Application Support/FFXIVInjector-mac/pristine/<게임버전>/`에 저장합니다. 디스크를 거의 쓰지 않고, 파일은 읽기 전용입니다. 이미 한글이 들어 있는 파일은 백업하지 않습니다.
- 적용: 백업의 클론을 작업 폴더에 만들어 주입하고, 인덱스 무결성·전체 EXD 재읽기·폰트 읽기·한글 존재를 검사한 뒤에만 게임 폴더에 파일별 rename으로 교체합니다. 베이스는 항상 이 맥의 현재 버전 순정 백업입니다(업스트림 `original.zip`은 쓰지 않음). 교체 중 오류가 나면 백업으로 되돌립니다.
- 안전장치: 게임·런처·보틀 프로세스(`pgrep -f`)나 대상 파일을 연 프로세스(`lsof`)가 있으면 거부합니다. 게임 버전이 백업과 다르면 적용·원복을 거부합니다.
- 언어: `~/Documents/My Games/FINAL FANTASY XIV - A Realm Reborn/FFXIV.cfg`의 클라이언트 언어(지금은 English)를 한글로 덮습니다. 게임 언어를 바꾸면 원복 후 다시 적용하세요.
- 리소스: 업스트림 배포처의 `rawexd.zip`, `font.zip`을 내려받아 `FFXIVInjector-mac/resources`에 둡니다. 새 버전이 나오면 적용할 때 자동으로 갱신합니다. 게임보다 리소스가 오래되면 새로 생긴 문장은 영어로 남고, 열 구조가 바뀐 시트는 건너뜁니다(로그에 `[Warning]`).
- 로그: `FFXIVInjector-mac/logs/`.

## 터미널에서 직접 쓰기

```bash
./mac/build.sh                      # publish/mac/ffxiv-kr-mac 생성
B=publish/mac/ffxiv-kr-mac
$B status                           # 버전, 상태(순정/적용됨/...), 백업, 실행 여부, 다음 할 일
$B backup                           # 현재 버전 순정 백업
$B apply                            # 백업 기반 주입·검증·교체
$B verify                           # 인덱스·EXD·폰트 재읽기 검사
$B restore                          # 백업으로 원복 + 해시 확인
$B prune --yes                      # 이전 버전 백업 삭제
```

옵션: `--game <폴더>`, `--state <폴더>`, `--resources <rawexd/와 font/가 든 폴더>`, `--offline`, `--lang ja|en|de|fr`, `--verbose`. 환경변수 `FFXIV_KR_GAME`, `FFXIV_KR_STATE`로 기본 경로를 바꿀 수 있습니다(사본으로 시험할 때 유용). 빌드에는 `FFXIVInjector.Core/Secrets.cs`(리소스 주소)가 필요합니다.

## 개발자용 검증

- 회귀 테스트: `dotnet run --project FFXIVInjector.Tests -c Release` (맥 흐름 테스트 포함)
- 실제 데이터 시험은 게임 파일 사본으로만 합니다.

```bash
GAME="$HOME/Library/Application Support/FINAL FANTASY XIV ONLINE/Bottles/published_Final_Fantasy/drive_c/Program Files (x86)/SquareEnix/FINAL FANTASY XIV - A Realm Reborn/game"
mkdir -p /tmp/kr-sandbox/game/sqpack/ffxiv
cp -c "$GAME/ffxivgame.ver" /tmp/kr-sandbox/game/
cp -c "$GAME"/sqpack/ffxiv/{000000,0a0000}.win32.* /tmp/kr-sandbox/game/sqpack/ffxiv/
export FFXIV_KR_GAME=/tmp/kr-sandbox/game FFXIV_KR_STATE=/tmp/kr-sandbox/state
$B backup && $B apply && $B verify && $B restore
```
