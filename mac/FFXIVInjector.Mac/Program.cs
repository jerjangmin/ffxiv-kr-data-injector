// macOS text/font patch CLI. See README-mac.md.
using System.Reflection;
using FFXIVInjector.Core;
using FFXIVInjector.Mac;

const string Usage = """
사용법: ffxiv-kr-mac <명령> [옵션]

명령
  status     게임 버전, 패치 상태, 백업 유무, 게임 실행 여부를 보여 줍니다.
  backup     현재 버전의 순정 파일을 APFS 클론으로 백업합니다(이미 있으면 해시만 확인).
  apply      순정 백업을 베이스로 한글 텍스트·폰트를 주입해 교체합니다. 백업이 먼저 있어야 합니다.
  verify     게임 파일의 인덱스 무결성, 전체 EXD·폰트 재읽기, 한글 여부를 검사합니다.
  restore    순정 백업으로 원복하고 해시를 확인합니다.
  update-game  게임 업데이트용: 원복 → 런처로 업데이트하는 동안 대기 → 런처 종료 후 새 버전 백업·재적용.
  prune      현재 버전이 아닌 이전 버전 백업을 지웁니다(--yes 필요).
  refresh-url --tag <릴리스>  업스트림 릴리스 실행 파일에서 한글 리소스 주소를 읽어 저장합니다.
  preflight  FF14 실행 전 판단(action=launch|apply-then-launch|ask-update|update|blocked).
  ack-maintenance  "그냥 플레이"를 기록해 지금까지의 점검은 다시 묻지 않습니다.
  wait-session [--start-timeout <초>]  FF14가 켜졌다가 완전히 꺼질 때까지 기다립니다.
  version    이 도구가 빌드된 업스트림 릴리스를 보여 줍니다.

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
    if (a is "--game" or "--state" or "--resources" or "--lang" or "--tag" or "--exe" or "--start-timeout" && i + 1 < args.Length) { options[a] = args[++i]; continue; }
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
            Say($"도구: 업스트림 {UpstreamTag()} 기반");
            PrintStatus(service.Status());
            return 0;
        case "preflight":
        {
            var status = service.Status();
            var maintenances = await Lodestone.FetchAsync();
            var decision = Preflight.Decide(status, ToolConfig.Load(statePath).MaintenanceAcknowledgedUtc, maintenances, DateTime.UtcNow);
            string action = decision.Action switch
            {
                LaunchAction.Launch => "launch", LaunchAction.ApplyThenLaunch => "apply-then-launch",
                LaunchAction.AskUpdate => "ask-update", LaunchAction.Update => "update", _ => "blocked",
            };
            Say($"action={action}");
            Say($"reason={decision.Reason}");
            Say($"maintenance={decision.Maintenance ?? ""}");
            Say($"first_run={(!status.HasBackup && status.OtherBackupVersions.Count == 0 && status.Applied == null ? 1 : 0)}");
            return 0;
        }
        case "ack-maintenance":
        {
            var config = ToolConfig.Load(statePath);
            config.MaintenanceAcknowledgedUtc = DateTime.UtcNow;
            config.Save(statePath);
            return 0;
        }
        case "wait-session":
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            TimeSpan? timeout = options.GetValueOrDefault("--start-timeout") is { } t ? TimeSpan.FromSeconds(int.Parse(t)) : null;
            try { await service.WaitForGameSessionAsync(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(15), cts.Token, timeout); }
            catch (OperationCanceledException) { Say("대기를 중단했습니다."); return 1; }
            return 0;
        }
        case "version":
            Console.WriteLine(UpstreamTag());
            return 0;
        case "refresh-url":
        {
            string? exePath = options.GetValueOrDefault("--exe");
            string tag = options.GetValueOrDefault("--tag") ?? UpstreamTag();
            string url = exePath != null
                ? ReleaseUrlExtractor.Extract(File.ReadAllBytes(Expand(exePath))) ?? throw new RefusedException("실행 파일에서 리소스 주소를 찾지 못했습니다.")
                : await ReleaseUrlExtractor.FromReleaseAsync(tag, Say);
            var config = ToolConfig.Load(statePath);
            bool changed = config.ResourceBaseUrl != url;
            config.ResourceBaseUrl = url;
            config.ResourceUrlSource = exePath ?? tag;
            config.Save(statePath);
            Say(changed ? "리소스 주소를 갱신했습니다." : "리소스 주소 변경 없음.");
            return 0;
        }
        case "backup":
            service.Backup();
            return 0;
        case "apply":
        case "update-game":
        {
            var language = ParseLanguage(options.GetValueOrDefault("--lang")) ?? MacPaths.ReadClientLanguage() ?? GameLanguage.English;
            IResourceProvider resources = options.TryGetValue("--resources", out var local) && local != null
                ? new LocalResourceProvider(Expand(local))
                : new DownloadResourceProvider(Path.Combine(statePath, "resources"), ResourceUrl(), options.ContainsKey("--offline"));
            if (command == "apply") await service.ApplyAsync(resources, language);
            else
            {
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
                try { await service.UpdateCycleAsync(resources, language, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(15), cts.Token); }
                catch (OperationCanceledException) { Say("중단했습니다. 게임 파일은 순정 상태입니다. 나중에 한글 적용을 실행하세요."); return 1; }
            }
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

string ResourceUrl()
{
    string url = Environment.GetEnvironmentVariable("FFXIV_KR_RESOURCE_URL") ?? ToolConfig.Load(statePath).ResourceBaseUrl;
    if (!url.StartsWith("https://", StringComparison.Ordinal))
        throw new RefusedException("한글 리소스 주소가 없습니다. 프로그램을 다시 실행해 업데이트를 받거나 refresh-url을 실행하세요.");
    return url;
}

static string UpstreamTag() =>
    typeof(MacPatchService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "UpstreamTag")?.Value is { Length: > 0 } tag ? tag : "dev";

static string Expand(string path) => Path.GetFullPath(path.StartsWith("~/")
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]) : path);
