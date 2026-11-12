using FFXIVInjector.Mac;

/// <summary>What the "FF14 한글" launcher does before starting the game.</summary>
internal static class PreflightTests
{
    private static DateTime Utc(int y, int m, int d, int h = 0) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    public static void Run()
    {
        Parsing();
        Decisions();
        Console.WriteLine("PASS: Lodestone maintenance parsing and launch decisions.");
    }

    private static void Parsing()
    {
        // Shape of the NA Lodestone maintenance list (tag span, title, publish time script).
        string Item(string tag, string title, long published) =>
            $@"<li class=""news__list""><a href=""/lodestone/news/detail/x"" class=""news__list--link ic__maintenance--list""><div class=""clearfix""><p class=""news__list--title""><span class=""news__list--tag"">{tag}</span>{title}</p><time class=""news__list--time""><span id=""d"">-</span><script>document.getElementById('d').innerHTML = ldst_strftime({published}, 'YMD');</script></time></div></a></li>";
        long nov5 = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        long dec20 = new DateTimeOffset(2026, 12, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        string html = Item("[Follow-up]", "All Worlds Maintenance (Nov. 9-10): Follow-up", nov5 + 400000)
            + Item("[Maintenance]", "All Worlds Maintenance (Nov. 9-10)", nov5)
            + Item("[Maintenance]", "The Lodestone Maintenance (Nov. 12)", nov5)
            + Item("[Maintenance]", "All Worlds Maintenance (Dec. 31 - Jan. 1)", dec20)
            + Item("[Maintenance]", "All Worlds Maintenance (Jan. 6)", dec20);
        var list = Lodestone.Parse(html);
        Assert(list.Count == 3, "only All Worlds maintenances, follow-ups merged");
        Assert(list[0].StartUtc == Utc(2026, 11, 9) && list[0].EndUtc == Utc(2026, 11, 11, 12), "Nov. 9-10 window covers the Pacific end of Nov 10");
        Assert(list[1].StartUtc == Utc(2026, 12, 31) && list[1].EndUtc == Utc(2027, 1, 2, 12), "range across months and years");
        Assert(list[2].StartUtc == Utc(2027, 1, 6), "January maintenance announced in December is next year");
        Assert(Lodestone.ParseWindow("TBD", Utc(2026, 11, 1)) == null, "unparseable dates are skipped");
    }

    private static void Decisions()
    {
        var applied = new StateRecord { CreatedUtc = Utc(2026, 11, 5) };
        MacStatus Patched(params string[] running) => new() { State = GameFileState.Patched, HasBackup = true, Applied = applied, BackupCreatedUtc = Utc(2026, 11, 4), RunningProcesses = running.ToList() };
        var nov9 = new MaintenanceWindow("All Worlds Maintenance (Nov. 9-10)", Utc(2026, 11, 9), Utc(2026, 11, 11, 12));
        var oct19 = new MaintenanceWindow("All Worlds Maintenance (Oct. 19)", Utc(2026, 10, 19), Utc(2026, 10, 20, 12));
        var dec1 = new MaintenanceWindow("All Worlds Maintenance (Dec. 1)", Utc(2026, 12, 1), Utc(2026, 12, 2, 12));
        var now = Utc(2026, 11, 12);

        Assert(Preflight.Decide(Patched(), null, [oct19], now).Action == LaunchAction.Launch, "no maintenance since apply: just launch");
        Assert(Preflight.Decide(Patched(), null, [dec1], now).Action == LaunchAction.Launch, "upcoming maintenance is ignored until it starts");
        var ask = Preflight.Decide(Patched(), null, [nov9, oct19], now);
        Assert(ask.Action == LaunchAction.AskUpdate && ask.Maintenance == nov9.Title, "maintenance after apply: ask");
        Assert(Preflight.Decide(Patched(), Utc(2026, 11, 11, 13), [nov9], now).Action == LaunchAction.Launch, "'just play' silences that maintenance");
        Assert(Preflight.Decide(Patched(), null, null, now).Action == LaunchAction.AskUpdate, "unreadable Lodestone: ask");
        Assert(Preflight.Decide(Patched("1 ffxiv_dx11.exe"), null, [oct19], now).Action == LaunchAction.Blocked, "already running: do nothing");

        var pristine = new MacStatus { State = GameFileState.Pristine, HasBackup = true, BackupCreatedUtc = Utc(2026, 11, 5) };
        Assert(Preflight.Decide(pristine, null, [oct19], now).Action == LaunchAction.ApplyThenLaunch, "pristine, no maintenance since backup: apply then launch");
        Assert(Preflight.Decide(pristine, null, [nov9], now).Action == LaunchAction.Update, "pristine after maintenance: let the launcher update first");
        Assert(Preflight.Decide(new MacStatus { State = GameFileState.PristineWithoutBackup }, null, [], now).Action == LaunchAction.Update, "first run updates first");
        var forgot = new MacStatus { State = GameFileState.ForeignPatched, OtherBackupVersions = ["2026.11.03.0000.0000"] };
        Assert(Preflight.Decide(forgot, null, [], now).Action == LaunchAction.Blocked, "updated over patched files: blocked");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
