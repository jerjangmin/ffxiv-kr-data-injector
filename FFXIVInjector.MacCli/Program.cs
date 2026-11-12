// macOS text/font patch CLI. See README-mac.md.
using FFXIVInjector.Core;
using FFXIVInjector.Core.Mac;

const string Usage = """
사용법: ffxiv-kr-mac <명령> [옵션]

명령
  status     게임 버전, 패치 상태, 백업 유무, 게임 실행 여부를 보여 줍니다.
  backup     현재 버전의 순정 파일을 APFS 클론으로 백업합니다(이미 있으면 해시만 확인).
  apply      순정 백업을 베이스로 한글 텍스트·폰트를 주입해 교체합니다. 백업이 먼저 있어야 합니다.
  verify     게임 파일의 인덱스 무결성, 전체 EXD·폰트 재읽기, 한글 여부를 검사합니다.
  restore    순정 백업으로 원복하고 해시를 확인합니다.
  prune      현재 버전이 아닌 이전 버전 백업을 지웁니다(--yes 필요).

옵션
  --game <폴더>       게임 폴더(기본: CrossOver 보틀 안의 .../FINAL FANTASY XIV - A Realm Reborn/game)
  --state <폴더>      백업·기록·리소스 폴더(기본: ~/Library/Application Support/FFXIVInjector-mac)
  --resources <폴더>  rawexd/와 font/가 든 한글 리소스 폴더를 직접 지정(내려받지 않음)
  --offline           리소스를 새로 확인하지 않고 받아 둔 것을 사용
  --lang <ja|en|de|fr> 한글로 덮어쓸 클라이언트 언어(기본: FFXIV.cfg의 Language 값)
  --verbose           주입 로그를 화면에도 출력(항상 --state/logs에 저장)

환경변수 FFXIV_KR_GAME, FFXIV_KR_STATE로 --game, --state 기본값을 바꿀 수 있습니다.
""";

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 2 : 0;
}

string command = args[0];
var options = new Dictionary<string, string?>();
for (int i = 1; i < args.Length; i++)
{
    string a = args[i];
    if (a is "--offline" or "--verbose" or "--yes") { options[a] = null; continue; }
    if (a is "--game" or "--state" or "--resources" or "--lang" && i + 1 < args.Length) { options[a] = args[++i]; continue; }
    Console.Error.WriteLine($"알 수 없는 옵션: {a}\n\n{Usage}");
    return 2;
}

string gamePath = Expand(options.GetValueOrDefault("--game") ?? Environment.GetEnvironmentVariable("FFXIV_KR_GAME") ?? MacPaths.DefaultGamePath);
string statePath = Expand(options.GetValueOrDefault("--state") ?? Environment.GetEnvironmentVariable("FFXIV_KR_STATE") ?? MacPaths.DefaultStateRoot);
bool verbose = options.ContainsKey("--verbose");

Directory.CreateDirectory(Path.Combine(statePath, "logs"));
string logPath = Path.Combine(statePath, "logs", $"{DateTime.Now:yyyyMMdd-HHmmss}-{command}.log");
using var logFile = new StreamWriter(logPath) { AutoFlush = true };
void Say(string message) { Console.WriteLine(message); logFile.WriteLine(message); }
void Detail(string message) { logFile.WriteLine(message); if (verbose) Console.WriteLine(message); }

var service = new MacPatchService(gamePath, statePath, new MacProcessGuard(), new GameInspector()) { Log = Say, Detail = Detail };

try
{
    switch (command)
    {
        case "status":
            PrintStatus(service.Status());
            return 0;
        case "backup":
            service.Backup();
            return 0;
        case "apply":
        {
            var language = ParseLanguage(options.GetValueOrDefault("--lang")) ?? MacPaths.ReadClientLanguage() ?? GameLanguage.English;
            IResourceProvider resources = options.TryGetValue("--resources", out var local) && local != null
                ? new LocalResourceProvider(Expand(local))
                : new DownloadResourceProvider(Path.Combine(statePath, "resources"), ResourceUrl(), options.ContainsKey("--offline"));
            await service.ApplyAsync(resources, language);
            Say("게임을 켜서 타이틀·로비·UI가 한글로 보이는지 확인하세요. 문제가 있으면 restore로 원복합니다.");
            return 0;
        }
        case "verify":
        {
            Say("검사 중(전체 EXD 재읽기, 수십 초 걸릴 수 있음)...");
            var (status, inspection) = service.Verify();
            PrintStatus(status);
            Say($"인덱스 항목 {inspection.IndexEntries:N0}개, EXD {inspection.SheetPagesRead:N0}쪽, 폰트 {inspection.FontFilesRead}개 읽음");
            Say($"한글: 텍스트 {inspection.TextHangul:N0}자, 폰트 글리프 {inspection.FontHangulGlyphs:N0}개{(inspection.Sample.Length > 0 ? $" (예: {inspection.Sample})" : "")}");
            if (inspection.Problems.Count > 0)
            {
                Say("문제 발견:");
                foreach (var p in inspection.Problems.Take(30)) Say("  - " + p);
                return 1;
            }
            bool ok = status.State is GameFileState.Pristine or GameFileState.Patched or GameFileState.PristineWithoutBackup;
            Say(ok ? "검증 통과." : "파일은 읽히지만 이 도구의 기록과 맞지 않습니다. 위 '다음 할 일'을 따르세요.");
            return ok ? 0 : 1;
        }
        case "restore":
            service.Restore();
            return 0;
        case "prune":
            if (!options.ContainsKey("--yes")) { Say("이전 버전 백업을 지우려면 --yes를 붙이세요."); return 2; }
            var removed = service.PruneOldBackups();
            Say(removed.Count == 0 ? "지울 이전 백업이 없습니다." : "삭제한 백업: " + string.Join(", ", removed));
            return 0;
        default:
            Console.Error.WriteLine($"알 수 없는 명령: {command}\n\n{Usage}");
            return 2;
    }
}
catch (RefusedException ex)
{
    Say("중단: " + ex.Message);
    return 1;
}
catch (Exception ex)
{
    Say("오류: " + ex.Message);
    logFile.WriteLine(ex.ToString());
    Say("자세한 기록: " + logPath);
    return 1;
}

void PrintStatus(MacStatus s)
{
    Say($"게임 폴더: {s.GamePath}");
    Say($"게임 버전: {s.GameVersion}");
    Say($"순정 백업: {(s.HasBackup ? "있음" : "없음")}{(s.OtherBackupVersions.Count > 0 ? $" (다른 버전 백업: {string.Join(", ", s.OtherBackupVersions)})" : "")}");
    string state = s.State switch
    {
        GameFileState.Pristine => "순정(백업과 같음)",
        GameFileState.Patched => $"한글 적용됨({s.Applied?.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}, 언어 {s.Applied?.Language}, 텍스트 {s.Applied?.TextVersion}, 폰트 {s.Applied?.FontVersion})",
        GameFileState.PristineWithoutBackup => "순정으로 보임(아직 백업 없음)",
        GameFileState.ForeignPatched => "한글이 들어 있으나 이 도구의 적용 기록과 다름",
        _ => "알 수 없음(백업·기록과 다르고 한글도 없음)",
    };
    Say($"파일 상태: {state}");
    if (s.HasStrayDat1) Say("주의: 0a0000.win32.dat1이 있습니다(맥 순정 설치에는 없는 파일).");
    Say(s.RunningProcesses.Count > 0 ? "게임/런처 실행 중: " + string.Join(" | ", s.RunningProcesses) : "게임/런처: 꺼져 있음");
    string next = s.State switch
    {
        GameFileState.PristineWithoutBackup when s.OtherBackupVersions.Count > 0 => "게임이 업데이트됐습니다. backup 후 apply로 다시 적용하세요.",
        GameFileState.PristineWithoutBackup => "backup 후 apply로 적용할 수 있습니다.",
        GameFileState.Pristine => "apply로 적용할 수 있습니다.",
        GameFileState.Patched => "게임 업데이트(점검·패치) 전에는 런처를 켜기 전에 restore 하세요.",
        GameFileState.ForeignPatched when s.HasBackup => "restore로 순정 백업을 되돌린 뒤 다시 적용하세요.",
        GameFileState.ForeignPatched => "업데이트 전에 원복하지 못한 상태일 수 있습니다. 런처의 파일 검사·복구로 순정 상태를 만든 뒤 backup → apply 하세요.",
        _ when s.HasBackup => "restore로 순정 백업을 되돌릴 수 있습니다.",
        _ => "런처의 파일 검사·복구로 순정 상태를 만든 뒤 backup 하세요.",
    };
    Say("다음 할 일: " + next);
}

static GameLanguage? ParseLanguage(string? value) => value?.ToLowerInvariant() switch
{
    null => null,
    "ja" or "japanese" => GameLanguage.Japanese,
    "en" or "english" => GameLanguage.English,
    "de" or "german" => GameLanguage.German,
    "fr" or "french" => GameLanguage.French,
    _ => throw new RefusedException("--lang은 ja, en, de, fr 중 하나여야 합니다."),
};

static string ResourceUrl()
{
    string url = Environment.GetEnvironmentVariable("FFXIV_KR_RESOURCE_URL") ?? Config.ResourceBaseUrl;
    if (!url.StartsWith("https://", StringComparison.Ordinal) || url.Contains("your-cloudfront-url", StringComparison.Ordinal))
        throw new RefusedException("리소스 주소가 설정되지 않았습니다. FFXIVInjector.Core/Secrets.cs 또는 FFXIV_KR_RESOURCE_URL을 설정하세요.");
    return url;
}

static string Expand(string path) => Path.GetFullPath(path.StartsWith("~/")
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]) : path);
