using System.Text;
using FFXIVInjector.Core;
using FFXIVInjector.Mac;

/// <summary>
/// macOS backup/apply/restore flow on small fake sqpack files. The real Lumina/sqpack path is covered by the
/// sandbox run described in README-mac.md; these tests pin the safety contract: what is refused and what the
/// game folder looks like afterwards.
/// </summary>
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
internal static class MacPatchServiceTests
{
    private const string Version = "2026.11.03.0000.0000";
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("KOREAN-PATCH");

    private sealed class FakeProbe : IGameActivityProbe
    {
        public List<string> Processes { get; } = new();
        public List<string> Handles { get; } = new();
        public IReadOnlyList<string> FindGameProcesses() => Processes;
        public IReadOnlyList<string> FindOpenHandles(IReadOnlyCollection<string> files) => Handles;
    }

    /// <summary>Treats the marker in a dat0 as "Korean present", standing in for the Lumina-based inspector.</summary>
    private sealed class FakeInspector : IGameInspector
    {
        public string? FailWorkFolderContaining { get; set; }
        public InspectionResult Inspect(string gamePath, bool deep)
        {
            var result = new InspectionResult();
            string sq = Path.Combine(gamePath, "sqpack", "ffxiv");
            if (HasMarker(Path.Combine(sq, "0a0000.win32.dat0"))) result.TextHangul = 1;
            if (HasMarker(Path.Combine(sq, "000000.win32.dat0"))) result.FontHangulGlyphs = 1;
            if (FailWorkFolderContaining != null && gamePath.Contains(FailWorkFolderContaining)) result.Problems.Add("simulated broken index");
            return result;
        }
        private static bool HasMarker(string path) => File.Exists(path) && File.ReadAllBytes(path).AsSpan().IndexOf(Marker) >= 0;
    }

    private static void FakeInject(string work, string resources, GameLanguage language, Action<string> log)
    {
        string sq = Path.Combine(work, "sqpack", "ffxiv");
        foreach (var dat in new[] { "000000.win32.dat0", "0a0000.win32.dat0" })
            using (var s = new FileStream(Path.Combine(sq, dat), FileMode.Append)) s.Write(Marker);
        File.WriteAllBytes(Path.Combine(sq, "0a0000.win32.index"), Encoding.ASCII.GetBytes("patched-index-" + language));
    }

    private sealed class NoResources : IResourceProvider
    {
        public Task<ResourceSet> EnsureAsync(Action<string> log, CancellationToken ct = default) => Task.FromResult(new ResourceSet("/unused", "t1", "f1"));
    }

    public static async Task Run(string root)
    {
        await FullFlowRestoresIdenticalBytes(root);
        await RefusesWithoutBackupOrAfterVersionChange(root);
        await RefusesWhileGameRunsOrFilesOpen(root);
        RefusesToBackUpPatchedFiles(root);
        await FailedInjectionOrVerificationLeavesGameUntouched(root);
        await CorruptedBackupIsRejected(root);
        ParsersAndHelpers();
        Console.WriteLine("PASS: mac backup/apply/restore flow, version/process/open-file refusals, failed apply isolation, corrupted backup, parsers.");
    }

    private static async Task FullFlowRestoresIdenticalBytes(string root)
    {
        var (game, state) = Setup(root, "flow");
        var pristine = Snapshot(game);
        var service = Service(game, state);

        Assert(service.Status().State == GameFileState.PristineWithoutBackup, "fresh install must look pristine without backup");
        Assert(service.Backup(), "first backup creates files");
        Assert(!service.Backup(), "second backup is a no-op");
        string backupFile = Path.Combine(state, "pristine", Version, "0a0000.win32.dat0");
        Assert((File.GetUnixFileMode(backupFile) & UnixFileMode.UserWrite) == 0, "backup files must be read-only");
        Assert(service.Status().State == GameFileState.Pristine, "after backup: pristine");

        await service.ApplyAsync(new NoResources(), GameLanguage.English);
        var patched = Snapshot(game);
        Assert(!patched["sqpack/ffxiv/0a0000.win32.dat0"].SequenceEqual(pristine["sqpack/ffxiv/0a0000.win32.dat0"]), "apply must change the text dat");
        Assert(patched["ffxivgame.ver"].SequenceEqual(pristine["ffxivgame.ver"]), "apply must not touch ffxivgame.ver");
        Assert(patched.Keys.Order().SequenceEqual(pristine.Keys.Order()), "apply must not add or leave files in the game folder");
        Assert(service.Status().State == GameFileState.Patched, "after apply: patched");
        Assert(!Directory.EnumerateFileSystemEntries(Path.Combine(state, "work")).Any(), "work folder is cleaned up");

        // Re-applying starts from the pristine backup, not from the already patched files.
        await service.ApplyAsync(new NoResources(), GameLanguage.English);
        Assert(Snapshot(game)["sqpack/ffxiv/0a0000.win32.dat0"].SequenceEqual(patched["sqpack/ffxiv/0a0000.win32.dat0"]), "re-apply must not stack patches");

        Assert(service.Restore(), "restore replaces files");
        var restored = Snapshot(game);
        foreach (var (name, bytes) in pristine) Assert(restored[name].SequenceEqual(bytes), "restore must be byte-identical: " + name);
        foreach (var name in MacPatchService.TargetFiles)
            Assert((File.GetUnixFileMode(Path.Combine(game, "sqpack", "ffxiv", name)) & UnixFileMode.UserWrite) != 0, "restored files must stay writable for the launcher: " + name);
        Assert(service.Status().State == GameFileState.Pristine, "after restore: pristine");
        Assert(!service.Restore(), "second restore is a no-op");
    }

    private static async Task RefusesWithoutBackupOrAfterVersionChange(string root)
    {
        var (game, state) = Setup(root, "version");
        var service = Service(game, state);
        var before = Snapshot(game);
        await ExpectRefused(() => service.ApplyAsync(new NoResources(), GameLanguage.English), "apply without backup");
        ExpectRefused(() => service.Restore(), "restore without backup");
        SameFiles(game, before, "refusals without backup");

        service.Backup();
        await service.ApplyAsync(new NoResources(), GameLanguage.English);
        // The launcher updated the game while it was patched.
        File.WriteAllText(Path.Combine(game, "ffxivgame.ver"), "2026.11.10.0000.0000");
        var updated = Snapshot(game);
        await ExpectRefused(() => service.ApplyAsync(new NoResources(), GameLanguage.English), "apply after version change");
        ExpectRefused(() => service.Restore(), "restore with an older-version backup");
        ExpectRefused(() => service.Backup(), "backup of patched files after update");
        SameFiles(game, updated, "version refusals");
        var status = service.Status();
        Assert(status.State == GameFileState.ForeignPatched && status.OtherBackupVersions.SequenceEqual([Version]), "status reports update over patched files");
    }

    private static async Task RefusesWhileGameRunsOrFilesOpen(string root)
    {
        var (game, state) = Setup(root, "running");
        var probe = new FakeProbe();
        var service = Service(game, state, probe);
        service.Backup();
        var before = Snapshot(game);
        probe.Processes.Add("123 C:/game/ffxiv_dx11.exe");
        await ExpectRefused(() => service.ApplyAsync(new NoResources(), GameLanguage.English), "apply while running");
        ExpectRefused(() => service.Restore(), "restore while running");
        ExpectRefused(() => service.Backup(), "backup while running");
        probe.Processes.Clear();
        probe.Handles.Add("456 wine64-preloader");
        await ExpectRefused(() => service.ApplyAsync(new NoResources(), GameLanguage.English), "apply while files are open");
        SameFiles(game, before, "running refusals");
    }

    private static void RefusesToBackUpPatchedFiles(string root)
    {
        var (game, state) = Setup(root, "foreign");
        File.WriteAllBytes(Path.Combine(game, "sqpack", "ffxiv", "0a0000.win32.dat0"), Marker);
        ExpectRefused(() => Service(game, state).Backup(), "backup of files that already contain Korean");
        Assert(!Directory.Exists(Path.Combine(state, "pristine", Version)), "no backup for patched files");

        var (game2, state2) = Setup(root, "dat1");
        File.WriteAllBytes(Path.Combine(game2, "sqpack", "ffxiv", "0a0000.win32.dat1"), [1]);
        ExpectRefused(() => Service(game2, state2).Backup(), "backup when the upstream-only dat1 exists");
    }

    private static async Task FailedInjectionOrVerificationLeavesGameUntouched(string root)
    {
        var (game, state) = Setup(root, "failures");
        var inspector = new FakeInspector();
        var service = new MacPatchService(game, state, new FakeProbe(), inspector, (_, _, _, _) => throw new InvalidDataException("simulated injector crash"));
        service.Backup();
        var before = Snapshot(game);
        try { await service.ApplyAsync(new NoResources(), GameLanguage.English); throw new Exception("injector failure must surface"); }
        catch (InvalidDataException) { }
        SameFiles(game, before, "injector failure");

        inspector.FailWorkFolderContaining = Path.Combine(state, "work");
        service = new MacPatchService(game, state, new FakeProbe(), inspector, FakeInject);
        await ExpectRefused(() => service.ApplyAsync(new NoResources(), GameLanguage.English), "apply when the work folder fails verification");
        SameFiles(game, before, "verification failure");
        Assert(!Directory.EnumerateFileSystemEntries(Path.Combine(state, "work")).Any(), "failed work folders are removed");
    }

    private static async Task CorruptedBackupIsRejected(string root)
    {
        var (game, state) = Setup(root, "corrupt");
        var service = Service(game, state);
        service.Backup();
        string file = Path.Combine(state, "pristine", Version, "0a0000.win32.index");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.WriteAllText(file, "tampered");
        var before = Snapshot(game);
        await ExpectRefused(() => service.ApplyAsync(new NoResources(), GameLanguage.English), "apply from a corrupted backup");
        SameFiles(game, before, "corrupted backup");
    }

    private static void ParsersAndHelpers()
    {
        var running = MacProcessGuard.ParsePgrep("100 /x/ffxiv-kr-mac apply --game /published_Final_Fantasy/game\n200 C:/x/ffxivlauncher64.exe\n", new HashSet<int> { 100 });
        Assert(running.SequenceEqual(["200 C:/x/ffxivlauncher64.exe"]), "pgrep parsing must drop this tool's own process");
        var holders = MacProcessGuard.ParseLsof("p10\ncwine64-preloader\nf12\np11\ncffxiv-kr-mac\n", new HashSet<int> { 11 });
        Assert(holders.SequenceEqual(["10 wine64-preloader"]), "lsof parsing");

        var exh = new ExhCompatibility.ExhColumns([0, 4, 8], [true, false, true]);
        Assert(ExhCompatibility.Check([0, 8], ["str", "str"], exh) == null, "matching CSV layout is accepted");
        Assert(ExhCompatibility.Check([12], ["str"], exh) != null, "unknown offset is rejected");
        Assert(ExhCompatibility.Check([4], ["str"], exh) != null, "string written into a numeric column is rejected");
        Assert(ExhCompatibility.Check([4], ["int32"], exh) == null, "numeric column with numeric CSV type is accepted");

        Assert(DownloadResourceProvider.ParseVersionText("2026.10.06.1104.2304") == "2026.10.06.1104.2304", "plain version.txt");
        Assert(DownloadResourceProvider.ParseVersionText("staging 2026.11.01.0000.0000\nstable 2026.10.06.0000.0000\n") == "2026.10.06.0000.0000", "multi-branch version.txt picks stable");

        string cfg = Path.Combine(Path.GetTempPath(), "ffxiv-cfg-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(cfg, "<Cutscene Settings>\r\nLanguage\t3\r\n");
        Assert(MacPaths.ReadClientLanguage(cfg) == GameLanguage.French, "FFXIV.cfg Language 3 is French");
        File.Delete(cfg);
        Assert(MacPaths.ReadClientLanguage(cfg) == null, "missing cfg gives no language");

        // FDT: glyph table at 0x40, 16-byte entries starting with the UTF-8 code ("가" = EA B0 80).
        byte[] fdt = new byte[0x40 + 32];
        Encoding.ASCII.GetBytes("fcsv0100").CopyTo(fdt, 0);
        BitConverter.GetBytes(2u).CopyTo(fdt, 0x24);
        BitConverter.GetBytes(0x41u).CopyTo(fdt, 0x40);
        BitConverter.GetBytes(0xEAB080u).CopyTo(fdt, 0x50);
        Assert(GameInspector.CountHangulGlyphs(fdt) == 1, "FDT Hangul glyph count");
    }

    // ---------- helpers ----------

    private static MacPatchService Service(string game, string state, FakeProbe? probe = null) =>
        new(game, state, probe ?? new FakeProbe(), new FakeInspector(), FakeInject);

    private static (string Game, string State) Setup(string root, string name)
    {
        string game = Path.Combine(root, "mac-" + name, "game");
        string sq = Path.Combine(game, "sqpack", "ffxiv");
        Directory.CreateDirectory(sq);
        File.WriteAllText(Path.Combine(game, "ffxivgame.ver"), Version);
        foreach (var file in MacPatchService.TargetFiles) File.WriteAllText(Path.Combine(sq, file), "pristine " + file);
        File.WriteAllText(Path.Combine(sq, "060000.win32.index"), "unrelated");
        return (game, Path.Combine(root, "mac-" + name, "state"));
    }

    private static Dictionary<string, byte[]> Snapshot(string game) =>
        Directory.GetFiles(game, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(game, f).Replace('\\', '/'), File.ReadAllBytes);

    private static void SameFiles(string game, Dictionary<string, byte[]> expected, string label)
    {
        var now = Snapshot(game);
        Assert(now.Keys.Order().SequenceEqual(expected.Keys.Order()) && expected.All(e => now[e.Key].SequenceEqual(e.Value)), "game folder changed: " + label);
    }

    private static void ExpectRefused(Action action, string label)
    {
        try { action(); }
        catch (RefusedException) { return; }
        throw new Exception("expected refusal: " + label);
    }

    private static async Task ExpectRefused(Func<Task> action, string label)
    {
        try { await action(); }
        catch (RefusedException) { return; }
        throw new Exception("expected refusal: " + label);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
