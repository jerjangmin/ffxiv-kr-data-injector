# FF14 맥 한글패치 (텍스트·폰트)

macOS 공식 클라이언트(CrossOver 래퍼 앱)에 한글 텍스트와 폰트만 적용하는 프로그램입니다. 이미지·음성(dinput8 로더)은 맥에서 지원하지 않습니다.

> 게임 데이터 수정은 스퀘어에닉스 이용약관 위반입니다. 텍스트·폰트는 로컬 파일만 바꾸지만 제재 위험이 0은 아닙니다. 한글 UI가 보이는 스크린샷·방송 공유는 피하세요.

## 설치

```bash
./mac/build-app.sh --install     # ~/Applications/FFXIV 한글패치.app
```

앱에는 맥 전용 코드만 들어 있습니다. 주입 엔진(업스트림 `FFXIVInjector.Core`)은 앱을 켤 때마다 [dambyul/ffxiv-kr-data-injector](https://github.com/dambyul/ffxiv-kr-data-injector)의 **최신 릴리스 소스를 받아 빌드**합니다. 업스트림이 새 릴리스를 내면 다음 실행 때 자동으로 따라갑니다.

- 빌드에 .NET 10 SDK가 필요합니다. 없으면 `~/.dotnet`에 관리자 권한 없이 설치할지 묻습니다.
- 한글 리소스 주소도 새 릴리스의 실행 파일에서 다시 읽어 옵니다.
- 인터넷이 안 되거나 새 릴리스로 빌드가 실패하면 이전에 빌드한 도구를 그대로 씁니다.

## 사용

1. 런처로 최신 패치까지 받은 뒤 **게임과 런처를 완전히 종료**합니다.
2. `FFXIV 한글패치.app`을 엽니다. 터미널 창에 메뉴가 뜹니다(처음엔 도구 빌드에 1분 안팎).
3. `1) 한글 적용` → `y`. 순정 백업(1초) → 리소스 내려받기 → 주입·검증(약 1분) → 교체 순서로 진행됩니다.
4. 게임을 켜서 타이틀·로비·UI가 한글인지 확인합니다. 글자가 깨지거나 튕기면 바로 `2) 원복`.

## 게임 업데이트가 있을 때 (중요)

패치된 파일 위에서 런처가 업데이트하면 파일이 꼬일 수 있습니다. 정기 점검이나 패치 공지가 있는 날에는:

1. 런처를 켜기 **전에** 앱 → `2) 원복`
2. 런처로 업데이트 → 게임까지 한 번 켰다가 **완전히 종료**
3. 앱 → `1) 한글 적용` (새 게임 버전을 자동으로 백업한 뒤 적용. 업스트림에 새 릴리스가 있으면 도구도 먼저 다시 빌드)
4. 잘 되면 `4) 이전 버전 백업 정리`

원복을 잊고 업데이트했다면 도구가 적용·원복을 거부합니다(게임 버전이 백업과 다르기 때문). 이때는 런처의 **파일 검사·복구**로 순정 상태를 만든 뒤 3번부터 하세요.

## 동작 방식

- 게임 파일: `~/Library/Application Support/FINAL FANTASY XIV ONLINE/Bottles/published_Final_Fantasy/drive_c/Program Files (x86)/SquareEnix/FINAL FANTASY XIV - A Realm Reborn/game`. 앱 번들(`/Applications/...app`)은 건드리지 않습니다.
- 바꾸는 파일: `sqpack/ffxiv/000000.*`(폰트), `0a0000.*`(텍스트) 6개. `ffxivgame.ver`는 바꾸지 않습니다.
- 백업: 현재 버전의 순정 파일을 APFS 클론(`cp -c`)으로 `~/Library/Application Support/FFXIVInjector-mac/pristine/<게임버전>/`에 저장합니다. 디스크를 거의 쓰지 않고 파일은 읽기 전용입니다. 한글이 이미 들어 있는 파일은 백업하지 않습니다.
- 적용: 백업의 클론을 작업 폴더에 만들어 주입하고, 인덱스 무결성·전체 EXD 재읽기·폰트 읽기·한글 존재를 검사한 뒤에만 게임 폴더에 파일별 rename으로 교체합니다. 베이스는 항상 이 맥의 현재 버전 순정 백업입니다(업스트림 `original.zip`은 쓰지 않음). 업스트림 엔진이 바뀌어 결과가 이상하면 이 검사에서 걸리고 게임 파일은 그대로 남습니다.
- 안전장치: 게임·런처·보틀 프로세스(`pgrep -f`)나 대상 파일을 연 프로세스(`lsof`)가 있으면 거부합니다. 게임 버전이 백업과 다르면 적용·원복을 거부합니다.
- 언어: `~/Documents/My Games/FINAL FANTASY XIV - A Realm Reborn/FFXIV.cfg`의 클라이언트 언어를 한글로 덮습니다. 게임 언어를 바꾸면 원복 후 다시 적용하세요.
- 리소스: 업스트림 배포처의 `rawexd.zip`, `font.zip`을 `FFXIVInjector-mac/resources`에 받아 두고, 새 버전이 나오면 적용할 때 갱신합니다. 게임보다 오래된 리소스라면 새로 생긴 문장은 영어로 남고, 열 구조가 바뀐 시트는 건너뜁니다(로그에 `[건너뜀]`).
- 상태 폴더 `~/Library/Application Support/FFXIVInjector-mac/`: `pristine/`(백업), `applied/`(적용 기록), `resources/`, `upstream/`(받은 소스), `bin/`(빌드된 도구, 최근 2개), `config.json`(리소스 주소), `logs/`.

## 터미널에서 직접 쓰기

```bash
B="$HOME/Library/Application Support/FFXIVInjector-mac/bin/$(cat "$HOME/Library/Application Support/FFXIVInjector-mac/bin/current")/ffxiv-kr-mac"
"$B" status     # 그 밖에 backup, apply, verify, restore, prune --yes, version, refresh-url --tag <릴리스>
```

옵션: `--game <폴더>`, `--state <폴더>`, `--resources <rawexd/와 font/가 든 폴더>`, `--offline`, `--lang ja|en|de|fr`, `--verbose`. 환경변수 `FFXIV_KR_GAME`, `FFXIV_KR_STATE`로 기본 경로를, `FFXIV_KR_UPSTREAM_TAG`로 빌드할 업스트림 릴리스를 고정할 수 있습니다.

## 개발

- 구조: `mac/FFXIVInjector.Mac`(도구, 업스트림 Core는 수정 없이 `CsvInjector`만 사용), `mac/FFXIVInjector.Mac.Tests`, `mac/app`(앱 런처·메뉴·업데이터), `mac/build-app.sh`, `mac/build.sh`(이 체크아웃 기준 개발용 빌드).
- 테스트: `dotnet run --project mac/FFXIVInjector.Mac.Tests -c Release`
- 실제 데이터 시험은 게임 파일 사본으로만 합니다.

```bash
GAME="$HOME/Library/Application Support/FINAL FANTASY XIV ONLINE/Bottles/published_Final_Fantasy/drive_c/Program Files (x86)/SquareEnix/FINAL FANTASY XIV - A Realm Reborn/game"
mkdir -p /tmp/kr-sandbox/game/sqpack/ffxiv
cp -c "$GAME/ffxivgame.ver" /tmp/kr-sandbox/game/
cp -c "$GAME"/sqpack/ffxiv/{000000,0a0000}.win32.* /tmp/kr-sandbox/game/sqpack/ffxiv/
FFXIV_KR_GAME=/tmp/kr-sandbox/game FFXIV_KR_STATE=/tmp/kr-sandbox/state "publish/mac/FFXIV 한글패치.app/Contents/Resources/menu.command"
```
