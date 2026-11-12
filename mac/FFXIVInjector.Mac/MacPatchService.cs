using FFXIVInjector.Core;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace FFXIVInjector.Mac;

public enum GameFileState
{
    /// <summary>Files match the pristine backup of the current version.</summary>
    Pristine,
    /// <summary>Files match the last apply record of the current version.</summary>
    Patched,
    /// <summary>No backup for this version yet, and the files contain no Korean text or font glyphs.</summary>
    PristineWithoutBackup,
    /// <summary>Korean text or fonts present, but not from this tool's record (other injector, or record lost).</summary>
    ForeignPatched,
    /// <summary>Does not match the backup or the record and shows no Korean data.</summary>
    Unknown,
}

public sealed class FileRecord
{
    public string Name { get; set; } = "";
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class StateRecord
{
    public string GameVersion { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public string? Language { get; set; }
    public string? TextVersion { get; set; }
    public string? FontVersion { get; set; }
    public List<FileRecord> Files { get; set; } = new();
}

public sealed class MacStatus
{
    public string GamePath { get; init; } = "";
    public string GameVersion { get; init; } = "";
    public GameFileState State { get; init; }
    public bool HasBackup { get; init; }
    public DateTime? BackupCreatedUtc { get; init; }
    public StateRecord? Applied { get; init; }
    public List<string> OtherBackupVersions { get; init; } = new();
    public List<string> RunningProcesses { get; init; } = new();
    public bool HasStrayDat1 { get; init; }
}

/// <summary>Thrown when an operation is refused for safety. The game files are untouched when this is thrown.</summary>
public sealed class RefusedException(string message) : Exception(message);

/// <summary>
/// macOS text/font patch flow: APFS-clone backup of the current version's own files, inject into a clone of that
/// backup, verify, swap files in by rename, and restore by cloning the backup back. Never uses original.zip.
/// </summary>
public sealed class MacPatchService
{
    /// <summary>Files the text/font injection rewrites. The macOS client has no 0a0000.win32.dat1.</summary>
    public static readonly string[] TargetFiles = ["000000.win32.dat0", "000000.win32.index", "000000.win32.index2",
        "0a0000.win32.dat0", "0a0000.win32.index", "0a0000.win32.index2"];
    public const string VersionFile = "ffxivgame.ver";
    private const long RequiredFreeBytes = 2L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public delegate void Injector(string workGamePath, string resourcesRoot, GameLanguage language, Action<string> log);

    private readonly string _game;
    private readonly string _state;
    private readonly IGameActivityProbe _probe;
    private readonly IGameInspector _inspector;
    private readonly Injector _inject;
    public Action<string> Log { get; set; } = _ => { };
    /// <summary>Verbose injector output (one line per injected file).</summary>
    public Action<string> Detail { get; set; } = _ => { };

    public MacPatchService(string gamePath, string stateRoot, IGameActivityProbe probe, IGameInspector inspector, Injector? injector = null)
    {
        _game = Path.GetFullPath(gamePath);
        _state = Path.GetFullPath(stateRoot);
        _probe = probe;
        _inspector = inspector;
        _inject = injector ?? DefaultInjector;
    }

    private string SqDir => Path.Combine(_game, "sqpack", "ffxiv");
    private string BackupDir(string version) => Path.Combine(_state, "pristine", version);
    private string AppliedPath(string version) => Path.Combine(_state, "applied", version + ".json");
    private IEnumerable<string> GameTargets => TargetFiles.Select(f => Path.Combine(SqDir, f));

    // ---------- status ----------

    public MacStatus Status()
    {
        ValidateGamePath();
        string version = ReadGameVersion();
        var backup = LoadBackupRecord(version);
        var applied = LoadApplied(version);
        return new MacStatus
        {
            GamePath = _game,
            GameVersion = version,
            State = DetermineState(backup, applied),
            HasBackup = backup != null,
            BackupCreatedUtc = backup?.CreatedUtc,
            Applied = applied,
            OtherBackupVersions = BackupVersions().Where(v => v != version).ToList(),
            RunningProcesses = _probe.FindGameProcesses().ToList(),
            HasStrayDat1 = File.Exists(Path.Combine(SqDir, "0a0000.win32.dat1")),
        };
    }

    // ---------- backup ----------

    /// <summary>Clones the current version's pristine files. Returns false when a valid backup already exists.</summary>
    public bool Backup()
    {
        ValidateGamePath();
        EnsureGameIdle();
        string version = ReadGameVersion();
        var existing = LoadBackupRecord(version);
        if (existing != null)
        {
            VerifyBackup(version, existing);
            Log($"게임 버전 {version}의 순정 백업이 이미 있습니다(해시 확인 완료).");
            return false;
        }
        if (File.Exists(Path.Combine(SqDir, "0a0000.win32.dat1")))
            throw new RefusedException("0a0000.win32.dat1이 있습니다. 맥 순정 설치에는 없는 파일이라, 다른 인젝터로 패치된 상태로 보입니다. 런처의 파일 검사·복구로 순정 상태를 만든 뒤 다시 백업하세요.");
        var inspection = _inspector.Inspect(_game, deep: false);
        if (inspection.Problems.Count > 0)
            throw new RefusedException("게임 파일 검사에 실패해 백업하지 않습니다: " + string.Join("; ", inspection.Problems));
        if (!inspection.LooksPristine)
            throw new RefusedException($"현재 게임 파일에 이미 한글이 들어 있습니다(텍스트 {inspection.TextHangul:N0}자, 폰트 글리프 {inspection.FontHangulGlyphs:N0}개). " +
                "패치된 파일은 백업하지 않습니다. 이 도구로 적용했다면 restore, 다른 도구였다면 런처의 파일 검사·복구로 순정 상태를 만든 뒤 다시 백업하세요.");

        string staging = Path.Combine(_state, "pristine", $".staging-{version}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var record = new StateRecord { GameVersion = version, CreatedUtc = DateTime.UtcNow };
            CloneFile(Path.Combine(_game, VersionFile), Path.Combine(staging, VersionFile));
            foreach (var name in TargetFiles)
            {
                string source = Path.Combine(SqDir, name);
                string copy = Path.Combine(staging, name);
                CloneFile(source, copy);
                string hash = Sha256(copy);
                if (hash != Sha256(source)) throw new IOException($"백업 중 {name}이 바뀌었습니다. 게임·런처를 끄고 다시 시도하세요.");
                record.Files.Add(new FileRecord { Name = name, Bytes = new FileInfo(copy).Length, Sha256 = hash });
            }
            if (ReadGameVersion() != version) throw new IOException("백업 중 게임 버전이 바뀌었습니다.");
            File.WriteAllText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(record, Json));
            File.WriteAllLines(Path.Combine(staging, "SHA256SUMS"), record.Files.Select(f => $"{f.Sha256}  {f.Name}"));
            foreach (var file in Directory.GetFiles(staging)) MakeReadOnly(file);
            Directory.Move(staging, BackupDir(version));
        }
        catch
        {
            DeleteTree(staging);
            throw;
        }
        Log($"순정 백업 완료: {BackupDir(version)}");
        return true;
    }

    // ---------- apply ----------

    public async Task ApplyAsync(IResourceProvider resources, GameLanguage language, CancellationToken ct = default)
    {
        ValidateGamePath();
        EnsureGameIdle();
        string version = ReadGameVersion();
        var backup = RequireBackupForCurrentVersion(version);
        var applied = LoadApplied(version);
        var state = DetermineState(backup, applied);
        if (state is not (GameFileState.Pristine or GameFileState.Patched))
            throw new RefusedException($"현재 게임 파일이 순정 백업과도, 마지막 적용 기록과도 다릅니다(상태: {state}). " +
                "먼저 restore로 순정 상태로 되돌린 뒤 다시 적용하세요.");
        EnsureFreeSpace();
        var res = await resources.EnsureAsync(Log, ct);

        string work = Path.Combine(_state, "work", Guid.NewGuid().ToString("N"));
        try
        {
            // The base is always the pristine backup of this exact version, never the current (maybe patched) files.
            string workSq = Path.Combine(work, "sqpack", "ffxiv");
            Directory.CreateDirectory(workSq);
            CloneFile(Path.Combine(BackupDir(version), VersionFile), Path.Combine(work, VersionFile));
            foreach (var name in TargetFiles)
            {
                CloneFile(Path.Combine(BackupDir(version), name), Path.Combine(workSq, name));
                MakeWritable(Path.Combine(workSq, name));
            }
            Log($"작업 폴더에 한글 주입 중({language})... 1분 안팎 걸립니다.");
            var sw = Stopwatch.StartNew();
            _inject(work, res.Root, language, Detail);
            ct.ThrowIfCancellationRequested();
            Log($"주입 완료({sw.Elapsed.TotalSeconds:F0}초). 작업 폴더 검증 중...");

            var check = _inspector.Inspect(work, deep: true);
            if (check.Problems.Count > 0)
                throw new RefusedException("주입 결과 검증 실패. 게임 파일은 바꾸지 않았습니다: " + string.Join("; ", check.Problems.Take(10)));
            if (!check.LooksPatched)
                throw new RefusedException($"주입 결과에 한글이 없습니다(텍스트 {check.TextHangul}, 폰트 {check.FontHangulGlyphs}). 게임 파일은 바꾸지 않았습니다.");
            Log($"작업 폴더 검증 통과: 인덱스 {check.IndexEntries:N0}개, EXD {check.SheetPagesRead:N0}쪽, 폰트 {check.FontFilesRead}개, 한글 {check.TextHangul:N0}자 ({check.Sample})");

            var record = new StateRecord
            {
                GameVersion = version, CreatedUtc = DateTime.UtcNow, Language = language.ToString(),
                TextVersion = res.TextVersion, FontVersion = res.FontVersion,
                Files = TargetFiles.Select(n => Describe(Path.Combine(workSq, n))).ToList(),
            };

            // Re-check right before touching the game folder.
            EnsureGameIdle();
            if (ReadGameVersion() != version) throw new RefusedException("작업 중 게임 버전이 바뀌었습니다. 게임 파일은 바꾸지 않았습니다.");
            ReplaceGameFiles(workSq, ".kr-new", backupForRollback: version);
            VerifyGameMatches(record, "적용");
            Directory.CreateDirectory(Path.GetDirectoryName(AppliedPath(version))!);
            File.WriteAllText(AppliedPath(version), JsonSerializer.Serialize(record, Json));
            Log($"적용 완료: 게임 버전 {version}, 언어 {language}, 텍스트 {res.TextVersion}, 폰트 {res.FontVersion}");
        }
        finally
        {
            DeleteTree(work);
        }
    }

    // ---------- verify ----------

    public (MacStatus Status, InspectionResult Inspection) Verify()
    {
        var status = Status();
        var inspection = _inspector.Inspect(_game, deep: true);
        return (status, inspection);
    }

    // ---------- restore ----------

    /// <summary>Clones the pristine backup back. Returns false when the files already match it.</summary>
    public bool Restore()
    {
        ValidateGamePath();
        EnsureGameIdle();
        string version = ReadGameVersion();
        var backup = LoadBackupRecord(version);
        if (backup == null)
        {
            var others = BackupVersions().ToList();
            throw new RefusedException(others.Count == 0
                ? $"게임 버전 {version}의 순정 백업이 없어 원복할 수 없습니다."
                : $"게임 버전이 {version}(으)로 바뀌어 백업({string.Join(", ", others)})으로 원복할 수 없습니다. " +
                  "업데이트가 패치된 파일 위에서 진행된 것입니다. 런처의 파일 검사·복구로 순정 상태를 만드세요.");
        }
        VerifyBackup(version, backup);
        if (Matches(backup))
        {
            Log("이미 순정 상태입니다. 바꿀 파일이 없습니다.");
            DeleteApplied(version);
            return false;
        }
        ReplaceGameFiles(BackupDir(version), ".kr-restore", backupForRollback: null);
        VerifyGameMatches(backup, "원복");
        DeleteApplied(version);
        if (File.Exists(Path.Combine(SqDir, "0a0000.win32.dat1")))
            Log("주의: 0a0000.win32.dat1이 남아 있습니다. 이 도구가 만든 파일이 아니라 지우지 않았습니다(인덱스가 참조하지 않으면 무해).");
        Log($"원복 완료: 게임 버전 {version} 순정 파일과 해시가 같습니다.");
        return true;
    }

    // ---------- game update cycle ----------

    /// <summary>
    /// For patch days: restore the pristine files, wait while the user updates with the launcher, then back up the
    /// new version and apply again. The launcher only ever patches pristine files.
    /// </summary>
    public async Task UpdateCycleAsync(IResourceProvider resources, GameLanguage language, TimeSpan poll, TimeSpan settle, CancellationToken ct = default)
    {
        var status = Status();
        if (status.RunningProcesses.Count > 0)
            throw new RefusedException("게임 또는 런처가 이미 실행 중입니다. 완전히 종료한 뒤 다시 시작하세요(이미 업데이트가 진행됐다면 먼저 확인이 필요합니다).");
        switch (status.State)
        {
            case GameFileState.Patched:
                Restore();
                break;
            case GameFileState.Pristine:
            case GameFileState.PristineWithoutBackup:
                Log("이미 순정 상태입니다.");
                break;
            default:
                throw new RefusedException(status.HasBackup
                    ? "현재 파일이 이 도구의 기록과 다릅니다. 먼저 원복(restore)한 뒤 다시 시작하세요."
                    : "현재 파일이 순정이 아니고 이 버전의 백업도 없습니다. 런처의 파일 검사·복구로 순정 상태를 만든 뒤 한글 적용을 하세요.");
        }
        string before = status.GameVersion;
        Log("");
        Log("지금 런처(FINAL FANTASY XIV ONLINE 앱)를 켜서 업데이트하세요.");
        Log("업데이트가 끝나면 게임을 시작하지 말고 앱을 완전히 종료(⌘Q)하세요. 종료를 감지하면 바로 한글을 다시 적용합니다.");
        Log("(그만두려면 Ctrl+C. 파일은 순정 상태로 남습니다.)");
        await WaitForGameSessionAsync(poll, settle, ct);

        string after = ReadGameVersion();
        Log(after == before ? $"게임 버전 변화 없음({after}). 그대로 다시 적용합니다." : $"게임이 {before} → {after}로 업데이트됐습니다.");
        Backup();
        await ApplyAsync(resources, language, ct);
    }

    /// <summary>Waits until the launcher/game has started and then stayed closed for <paramref name="settle"/>.</summary>
    public async Task WaitForGameSessionAsync(TimeSpan poll, TimeSpan settle, CancellationToken ct = default, TimeSpan? startTimeout = null)
    {
        var waiting = Stopwatch.StartNew();
        while (_probe.FindGameProcesses().Count == 0)
        {
            if (startTimeout != null && waiting.Elapsed > startTimeout)
                throw new RefusedException("FF14가 실행되지 않았습니다.");
            await Task.Delay(poll, ct);
        }
        Log("런처 실행을 감지했습니다. 업데이트 후 앱을 종료하면 이어서 진행합니다...");
        var quiet = Stopwatch.StartNew();
        bool running = true;
        while (true)
        {
            await Task.Delay(poll, ct);
            if (_probe.FindGameProcesses().Count > 0) { running = true; continue; }
            if (running) { running = false; quiet.Restart(); }
            // Launcher, updater and game hand over to each other; only a steady quiet period counts as closed.
            if (quiet.Elapsed >= settle) break;
        }
        Log("런처·게임 종료를 확인했습니다.");
    }

    /// <summary>Deletes backups of versions other than the current one. Only allowed when the current version is backed up.</summary>
    public List<string> PruneOldBackups()
    {
        ValidateGamePath();
        string version = ReadGameVersion();
        if (LoadBackupRecord(version) == null) throw new RefusedException("현재 버전 백업이 없어 이전 백업을 지우지 않습니다.");
        var removed = new List<string>();
        foreach (var old in BackupVersions().Where(v => v != version))
        {
            DeleteTree(BackupDir(old));
            if (File.Exists(AppliedPath(old))) File.Delete(AppliedPath(old));
            removed.Add(old);
        }
        return removed;
    }

    // ---------- helpers ----------

    private static void DefaultInjector(string work, string resources, GameLanguage language, Action<string> log)
    {
        var filter = ExhCompatibility.BuildFilter(work, resources, log);
        var injector = new CsvInjector { AbortOnFailure = true };
        injector.OnLog += log;
        injector.Inject(work, resources, language, filter);
    }

    private void ValidateGamePath()
    {
        if (_game.StartsWith("/Applications/", StringComparison.OrdinalIgnoreCase) || _game.Contains(".app/", StringComparison.OrdinalIgnoreCase))
            throw new RefusedException("앱 번들 안은 수정하지 않습니다. 보틀 안의 game 폴더를 지정하세요.");
        if (!File.Exists(Path.Combine(_game, VersionFile)) || !File.Exists(Path.Combine(SqDir, "0a0000.win32.index")))
            throw new RefusedException($"게임 폴더가 아닙니다(ffxivgame.ver, sqpack/ffxiv 없음): {_game}");
        string state = _state.TrimEnd('/') + "/";
        if (state.StartsWith(_game.TrimEnd('/') + "/", StringComparison.Ordinal))
            throw new RefusedException("백업 폴더를 게임 폴더 안에 둘 수 없습니다.");
    }

    private string ReadGameVersion() => File.ReadAllText(Path.Combine(_game, VersionFile)).Trim();

    private void EnsureGameIdle()
    {
        var running = _probe.FindGameProcesses();
        if (running.Count > 0)
            throw new RefusedException("게임 또는 런처가 실행 중입니다. 완전히 종료한 뒤 다시 실행하세요:\n  " + string.Join("\n  ", running));
        var holders = _probe.FindOpenHandles(GameTargets.Append(Path.Combine(_game, VersionFile)).ToArray());
        if (holders.Count > 0)
            throw new RefusedException("다른 프로세스가 게임 파일을 열고 있습니다:\n  " + string.Join("\n  ", holders));
    }

    private void EnsureFreeSpace()
    {
        Directory.CreateDirectory(_state);
        long free = new DriveInfo(_state).AvailableFreeSpace;
        if (free < RequiredFreeBytes) throw new RefusedException($"디스크 여유 공간이 부족합니다({free / 1024 / 1024:N0}MB). 2GB 이상 필요합니다.");
    }

    private StateRecord RequireBackupForCurrentVersion(string version)
    {
        var backup = LoadBackupRecord(version);
        if (backup != null) { VerifyBackup(version, backup); return backup; }
        var others = BackupVersions().ToList();
        throw new RefusedException(others.Count == 0
            ? $"게임 버전 {version}의 순정 백업이 없습니다. 먼저 backup을 실행하세요."
            : $"게임 버전({version})이 백업 버전({string.Join(", ", others)})과 다릅니다. 게임이 업데이트됐습니다. " +
              "현재 파일이 순정이면 backup으로 새 버전을 백업한 뒤 다시 적용하세요.");
    }

    private IEnumerable<string> BackupVersions()
    {
        string root = Path.Combine(_state, "pristine");
        if (!Directory.Exists(root)) return [];
        return Directory.GetDirectories(root).Select(Path.GetFileName).Where(n => n != null && !n.StartsWith('.')).Select(n => n!).Order();
    }

    private StateRecord? LoadBackupRecord(string version)
    {
        string manifest = Path.Combine(BackupDir(version), "manifest.json");
        if (!File.Exists(manifest)) return null;
        var record = JsonSerializer.Deserialize<StateRecord>(File.ReadAllText(manifest));
        if (record == null || record.GameVersion != version || !record.Files.Select(f => f.Name).Order().SequenceEqual(TargetFiles.Order()))
            throw new RefusedException($"백업 기록이 손상됐습니다: {manifest}");
        return record;
    }

    private void VerifyBackup(string version, StateRecord record)
    {
        string dir = BackupDir(version);
        if (File.ReadAllText(Path.Combine(dir, VersionFile)).Trim() != version)
            throw new RefusedException("백업의 ffxivgame.ver가 폴더 이름과 다릅니다: " + dir);
        foreach (var f in record.Files)
        {
            string path = Path.Combine(dir, f.Name);
            if (!File.Exists(path) || new FileInfo(path).Length != f.Bytes || Sha256(path) != f.Sha256)
                throw new RefusedException($"백업 파일이 손상됐습니다: {path}");
        }
    }

    private StateRecord? LoadApplied(string version)
    {
        string path = AppliedPath(version);
        return File.Exists(path) ? JsonSerializer.Deserialize<StateRecord>(File.ReadAllText(path)) : null;
    }

    private void DeleteApplied(string version)
    {
        if (File.Exists(AppliedPath(version))) File.Delete(AppliedPath(version));
    }

    private GameFileState DetermineState(StateRecord? backup, StateRecord? applied)
    {
        if (backup != null && Matches(backup)) return GameFileState.Pristine;
        if (applied != null && Matches(applied)) return GameFileState.Patched;
        var quick = _inspector.Inspect(_game, deep: false);
        if (quick.TextHangul > 0 || quick.FontHangulGlyphs > 0) return GameFileState.ForeignPatched;
        if (backup == null && quick.Problems.Count == 0 && !File.Exists(Path.Combine(SqDir, "0a0000.win32.dat1")))
            return GameFileState.PristineWithoutBackup;
        return GameFileState.Unknown;
    }

    private bool Matches(StateRecord record)
    {
        // Compare sizes first so the common mismatch costs no hashing.
        foreach (var f in record.Files)
        {
            string path = Path.Combine(SqDir, f.Name);
            if (!File.Exists(path) || new FileInfo(path).Length != f.Bytes) return false;
        }
        return record.Files.All(f => Sha256(Path.Combine(SqDir, f.Name)) == f.Sha256);
    }

    private void VerifyGameMatches(StateRecord record, string action)
    {
        var bad = record.Files.Where(f => Sha256(Path.Combine(SqDir, f.Name)) != f.Sha256).Select(f => f.Name).ToList();
        if (bad.Count > 0) throw new IOException($"{action} 후 해시가 기록과 다릅니다: {string.Join(", ", bad)}");
    }

    /// <summary>
    /// Stages every file next to its target (same directory, so rename is atomic per file), then renames them in.
    /// If anything fails after the first rename, the pristine backup is cloned back.
    /// </summary>
    private void ReplaceGameFiles(string sourceDir, string suffix, string? backupForRollback)
    {
        var staged = new List<(string Temp, string Target)>();
        bool renamed = false;
        try
        {
            foreach (var name in TargetFiles)
            {
                string target = Path.Combine(SqDir, name), temp = target + suffix;
                if (File.Exists(temp)) File.Delete(temp);
                CloneFile(Path.Combine(sourceDir, name), temp);
                MakeWritable(temp); // Backups are read-only; the launcher must be able to patch the restored files.
                staged.Add((temp, target));
            }
            foreach (var (temp, target) in staged)
            {
                renamed = true;
                File.Move(temp, target, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            foreach (var (temp, _) in staged) if (File.Exists(temp)) File.Delete(temp);
            if (renamed && backupForRollback != null)
            {
                Log("교체 중 오류. 순정 백업으로 되돌립니다: " + ex.Message);
                ReplaceGameFiles(BackupDir(backupForRollback), ".kr-rollback", null);
            }
            throw;
        }
    }

    private static FileRecord Describe(string path) => new() { Name = Path.GetFileName(path), Bytes = new FileInfo(path).Length, Sha256 = Sha256(path) };

    public static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>APFS clone (cp -c) so backups and staging cost almost no space; falls back to a normal copy.</summary>
    public static void CloneFile(string source, string destination)
    {
        if (File.Exists(destination)) throw new IOException("대상 파일이 이미 있습니다: " + destination);
        if (OperatingSystem.IsMacOS())
        {
            var psi = new ProcessStartInfo("/bin/cp") { UseShellExecute = false, RedirectStandardError = true };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(source);
            psi.ArgumentList.Add(destination);
            using var p = Process.Start(psi)!;
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode == 0) return;
            if (File.Exists(destination)) File.Delete(destination);
        }
        File.Copy(source, destination);
    }

    private static void MakeReadOnly(string path)
    {
        if (OperatingSystem.IsWindows()) new FileInfo(path).IsReadOnly = true;
        else File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    }

    private static void MakeWritable(string path)
    {
        if (OperatingSystem.IsWindows()) new FileInfo(path).IsReadOnly = false;
        else File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserWrite);
    }

    private static void DeleteTree(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories)) MakeWritable(f);
        Directory.Delete(path, true);
    }
}
