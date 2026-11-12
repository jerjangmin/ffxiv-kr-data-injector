# FF14 맥 한글패치 (텍스트·폰트)

macOS 공식 클라이언트(CrossOver 래퍼 앱)에 한글 텍스트와 폰트만 적용하는 프로그램입니다. 이미지·음성(dinput8 로더)은 맥에서 지원하지 않습니다.

> 게임 데이터 수정은 스퀘어에닉스 이용약관 위반입니다. 텍스트·폰트는 로컬 파일만 바꾸지만 제재 위험이 0은 아닙니다. 한글 UI가 보이는 스크린샷·방송 공유는 피하세요.

## 설치

```bash
./mac/build-app.sh --install     # /Applications/FF14 한글.app
```

앱에는 맥 전용 코드만 들어 있습니다. 주입 엔진(업스트림 `FFXIVInjector.Core`)은 앱을 켤 때마다 [dambyul/ffxiv-kr-data-injector](https://github.com/dambyul/ffxiv-kr-data-injector)의 **최신 릴리스 소스를 받아 빌드**합니다. 업스트림이 새 릴리스를 내면 다음 실행 때 자동으로 따라갑니다.

- 빌드에 .NET 10 SDK가 필요합니다(`~/.dotnet` 등). 없으면 관리 메뉴에서 관리자 권한 없이 설치할 수 있습니다.
- 한글 리소스 주소도 새 릴리스의 실행 파일에서 다시 읽어 옵니다.
- 인터넷이 안 되거나 새 릴리스로 빌드가 실패하면 이전에 빌드한 도구를 그대로 씁니다.

## 사용: FF14는 항상 "FF14 한글"로 켜기

Dock에서 원래 FF14 아이콘을 빼고 **FF14 한글**만 두세요. 원래 아이콘으로 켜면 아래 보호가 적용되지 않습니다.

"FF14 한글"을 열면 상황에 따라 알아서 처리합니다.

| 상황 | 동작 |
| --- | --- |
| 한글 적용 상태, 그 뒤 점검 없음 (대부분의 날) | 바로 FF14 실행 |
| 마지막 적용 이후 Lodestone에 "All Worlds Maintenance"가 있었음, 또는 공지를 못 읽음 | "업데이트 모드 / 그냥 플레이" 질문 |
| 처음 실행, 또는 새 게임 버전의 백업이 없음 | 업데이트 모드로 진행(처음엔 이용약관 위험 안내) |
| 순정 상태, 백업 이후 점검 없음 | 한글 적용(약 1분) 후 FF14 실행 |
| FF14가 이미 실행 중 / 파일 상태가 기록과 다름 | 아무것도 바꾸지 않고 안내 |

**업데이트 모드**
1. 한글을 원복합니다(1초).
2. FF14를 실행합니다. 런처에서 업데이트를 받으세요. 업데이트가 없으면 로그인 화면까지만 가도 됩니다.
3. 게임을 시작하지 말고 FF14를 종료(⌘Q)합니다.
4. 종료를 확인하면 새 버전을 백업하고 한글을 다시 적용합니다(약 1분). 끝나면 FF14를 다시 실행할지 묻습니다.

점검이 서버 작업뿐이었다면 "그냥 플레이"를 고르면 됩니다. 그 점검은 다시 묻지 않습니다.

**관리 메뉴**: ⌥(Option)를 누른 채 "FF14 한글"을 열면 터미널에 메뉴가 뜹니다(적용·원복·검증·이전 백업 정리·도구 업데이트·게임 업데이트 모드). 오류 안내 창에도 "관리 메뉴" 버튼이 있습니다.

> 게임 데이터 수정은 스퀘어에닉스 이용약관 위반입니다. 텍스트·폰트는 로컬 파일만 바꾸지만 제재 위험이 0은 아닙니다. 한글 UI가 보이는 스크린샷·방송 공유는 피하세요.

## 원래 아이콘으로 켜서 업데이트해 버렸다면

"FF14 한글"이 "한글이 적용된 채로 게임이 업데이트됐습니다"라며 멈춥니다. 섞인 파일을 순정으로 착각해 백업하지 않도록 일부러 막은 것입니다. 해당 파일(`sqpack/ffxiv/000000.*`, `0a0000.*`)을 순정으로 되돌려야 하는데, 맥 공식 런처에 파일 복구 기능이 있는지는 확인하지 못했습니다. 없다면 게임 데이터를 다시 받아야 합니다.

## 동작 방식

- 게임 파일: `~/Library/Application Support/FINAL FANTASY XIV ONLINE/Bottles/published_Final_Fantasy/drive_c/Program Files (x86)/SquareEnix/FINAL FANTASY XIV - A Realm Reborn/game`. 앱 번들(`/Applications/...app`)은 건드리지 않습니다.
- 바꾸는 파일: `sqpack/ffxiv/000000.*`(폰트), `0a0000.*`(텍스트) 6개. `ffxivgame.ver`는 바꾸지 않습니다.
- 백업: 현재 버전의 순정 파일을 APFS 클론(`cp -c`)으로 `~/Library/Application Support/FFXIVInjector-mac/pristine/<게임버전>/`에 저장합니다. 디스크를 거의 쓰지 않고 파일은 읽기 전용입니다. 한글이 이미 들어 있는 파일은 백업하지 않습니다.
- 적용: 백업의 클론을 작업 폴더에 만들어 주입하고, 인덱스 무결성·전체 EXD 재읽기·폰트 읽기·한글 존재를 검사한 뒤에만 게임 폴더에 파일별 rename으로 교체합니다. 베이스는 항상 이 맥의 현재 버전 순정 백업입니다(업스트림 `original.zip`은 쓰지 않음). 업스트림 엔진이 바뀌어 결과가 이상하면 이 검사에서 걸리고 게임 파일은 그대로 남습니다.
- 안전장치: 게임·런처·보틀 프로세스(`pgrep -f`)나 대상 파일을 연 프로세스(`lsof`)가 있으면 거부합니다. 게임 버전이 백업과 다르면 적용·원복을 거부합니다.
- 언어: `~/Documents/My Games/FINAL FANTASY XIV - A Realm Reborn/FFXIV.cfg`의 클라이언트 언어를 한글로 덮습니다. 게임 언어를 바꾸면 원복 후 다시 적용하세요.
- 리소스: 업스트림 배포처의 `rawexd.zip`, `font.zip`을 `FFXIVInjector-mac/resources`에 받아 두고, 새 버전이 나오면 적용할 때 갱신합니다. 게임보다 오래된 리소스라면 새로 생긴 문장은 영어로 남고, 열 구조가 바뀐 시트는 건너뜁니다(로그에 `[건너뜀]`).
- 점검 판단: `https://na.finalfantasyxiv.com/lodestone/news/category/2`의 "All Worlds Maintenance (날짜)" 제목을 읽습니다. 점검이 곧 클라이언트 패치는 아니라서 묻기만 하고, 로그인 전에는 실제 업데이트 여부를 알 수 없습니다.
- 상태 폴더 `~/Library/Application Support/FFXIVInjector-mac/`: `pristine/`(백업), `applied/`(적용 기록), `resources/`, `upstream/`(받은 소스), `bin/`(빌드된 도구, 최근 2개), `config.json`(리소스 주소, "그냥 플레이" 기록), `logs/`(실행 기록 포함).

## 터미널에서 직접 쓰기

```bash
B="$HOME/Library/Application Support/FFXIVInjector-mac/bin/$(cat "$HOME/Library/Application Support/FFXIVInjector-mac/bin/current")/ffxiv-kr-mac"
"$B" status     # 그 밖에 backup, apply, verify, restore, update-game, preflight, wait-session, prune --yes, version, refresh-url --tag <릴리스>
```

옵션: `--game <폴더>`, `--state <폴더>`, `--resources <rawexd/와 font/가 든 폴더>`, `--offline`, `--lang ja|en|de|fr`, `--verbose`. 환경변수 `FFXIV_KR_GAME`, `FFXIV_KR_STATE`로 기본 경로를, `FFXIV_KR_UPSTREAM_TAG`로 빌드할 업스트림 릴리스를 고정할 수 있습니다.

## 개발

- 구조: `mac/FFXIVInjector.Mac`(도구, 업스트림 Core는 수정 없이 `CsvInjector`만 사용), `mac/FFXIVInjector.Mac.Tests`, `mac/app`(`launcher.sh` = 앱 실행 흐름, `menu.command` = 관리 메뉴, `update.sh` = 업스트림 빌드), `mac/build-app.sh`, `mac/build.sh`(이 체크아웃 기준 개발용 빌드).
- 실행 흐름 시험(창 없음): `FFXIV_KR_TEST_ANSWERS="업데이트 모드|FF14 실행"`으로 대화창 응답을, `FFXIV_KR_FF14_OPEN=<명령>`으로 FF14 실행을 바꿔 `Contents/MacOS/launcher`를 직접 실행합니다.
- 테스트: `dotnet run --project mac/FFXIVInjector.Mac.Tests -c Release`
- 실제 데이터 시험은 게임 파일 사본으로만 합니다.

```bash
GAME="$HOME/Library/Application Support/FINAL FANTASY XIV ONLINE/Bottles/published_Final_Fantasy/drive_c/Program Files (x86)/SquareEnix/FINAL FANTASY XIV - A Realm Reborn/game"
mkdir -p /tmp/kr-sandbox/game/sqpack/ffxiv
cp -c "$GAME/ffxivgame.ver" /tmp/kr-sandbox/game/
cp -c "$GAME"/sqpack/ffxiv/{000000,0a0000}.win32.* /tmp/kr-sandbox/game/sqpack/ffxiv/
FFXIV_KR_GAME=/tmp/kr-sandbox/game FFXIV_KR_STATE=/tmp/kr-sandbox/state "publish/mac/FF14 한글.app/Contents/Resources/menu.command"
```
