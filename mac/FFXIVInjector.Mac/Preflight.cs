using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace FFXIVInjector.Mac;

/// <summary>An "All Worlds Maintenance" window from the Lodestone. Client patches always ship with one.</summary>
public sealed record MaintenanceWindow(string Title, DateTime StartUtc, DateTime EndUtc);

public static partial class Lodestone
{
    public const string MaintenanceUrl = "https://na.finalfantasyxiv.com/lodestone/news/category/2";

    [GeneratedRegex(@"<p class=""news__list--title"">(?:<span[^>]*>[^<]*</span>)?([^<]*)</p>\s*<time[^>]*>.*?ldst_strftime\((\d+)", RegexOptions.Singleline)]
    private static partial Regex Item();
    [GeneratedRegex(@"^All Worlds Maintenance \(([^)]*)\)")]
    private static partial Regex AllWorlds();
    [GeneratedRegex(@"(?:(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s*)?(\d{1,2})\b")]
    private static partial Regex DayToken();

    /// <summary>Null when the page cannot be read; the caller then has to ask the user.</summary>
    public static async Task<List<MaintenanceWindow>?> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh) FFXIVInjector-mac");
            var list = Parse(await client.GetStringAsync(MaintenanceUrl, ct));
            return list.Count > 0 ? list : null; // An empty list means the page layout changed, not "no maintenance".
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public static List<MaintenanceWindow> Parse(string html)
    {
        var result = new List<MaintenanceWindow>();
        foreach (Match m in Item().Matches(html))
        {
            string title = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
            var allWorlds = AllWorlds().Match(title);
            if (!allWorlds.Success) continue;
            var published = DateTimeOffset.FromUnixTimeSeconds(long.Parse(m.Groups[2].Value)).UtcDateTime;
            var window = ParseWindow(allWorlds.Groups[1].Value, published);
            if (window != null && !result.Any(w => w.StartUtc == window.Value.Start && w.EndUtc == window.Value.End))
                result.Add(new MaintenanceWindow(title, window.Value.Start, window.Value.End));
        }
        return result;
    }

    /// <summary>
    /// "Nov. 9-10", "Nov. 30 - Dec. 1", "Oct. 19" (Pacific dates) to a generous UTC window: from the first day's
    /// start to noon UTC after the last day, which covers the end of that day in Pacific time.
    /// </summary>
    public static (DateTime Start, DateTime End)? ParseWindow(string dates, DateTime publishedUtc)
    {
        int? month = null;
        var days = new List<DateTime>();
        foreach (Match t in DayToken().Matches(dates))
        {
            if (t.Groups[1].Success) month = DateTime.ParseExact(t.Groups[1].Value, "MMM", CultureInfo.InvariantCulture).Month;
            if (month == null) return null;
            int year = publishedUtc.Year;
            if (month < publishedUtc.Month - 6) year++;      // December notice for January maintenance
            else if (month > publishedUtc.Month + 6) year--; // January follow-up for December maintenance
            int day = int.Parse(t.Groups[2].Value);
            if (day < 1 || day > DateTime.DaysInMonth(year, month.Value)) return null;
            days.Add(new DateTime(year, month.Value, day, 0, 0, 0, DateTimeKind.Utc));
        }
        if (days.Count == 0) return null;
        return (days.Min(), days.Max().AddDays(1).AddHours(12));
    }
}

public enum LaunchAction
{
    /// <summary>Korean is applied and nothing suggests an update: start FF14.</summary>
    Launch,
    /// <summary>Pristine with a backup, no maintenance since: apply, then start FF14.</summary>
    ApplyThenLaunch,
    /// <summary>Korean is applied but a maintenance (possible patch) happened since, or the Lodestone was unreadable.</summary>
    AskUpdate,
    /// <summary>Restore (if needed), start FF14 for the launcher to update, apply after it closes.</summary>
    Update,
    /// <summary>Do not touch anything; show the reason.</summary>
    Blocked,
}

public sealed record LaunchDecision(LaunchAction Action, string Reason, string? Maintenance = null);

public static class Preflight
{
    public static LaunchDecision Decide(MacStatus status, DateTime? acknowledgedUtc, IReadOnlyList<MaintenanceWindow>? maintenances, DateTime nowUtc)
    {
        if (status.RunningProcesses.Count > 0)
            return new(LaunchAction.Blocked, "FF14가 이미 실행 중입니다.");
        switch (status.State)
        {
            case GameFileState.Patched:
            {
                DateTime safe = Max(status.Applied!.CreatedUtc, acknowledgedUtc);
                if (maintenances == null) return new(LaunchAction.AskUpdate, "점검 공지를 확인하지 못했습니다.");
                var hit = Since(maintenances, safe, nowUtc);
                return hit == null ? new(LaunchAction.Launch, "한글 적용 상태, 그 뒤 점검 없음")
                    : new(LaunchAction.AskUpdate, "마지막 한글 적용 이후 점검이 있었습니다.", hit.Title);
            }
            case GameFileState.Pristine:
            {
                if (maintenances == null || status.BackupCreatedUtc == null) return new(LaunchAction.Update, "점검 공지를 확인하지 못했습니다.");
                var hit = Since(maintenances, status.BackupCreatedUtc.Value, nowUtc);
                return hit == null ? new(LaunchAction.ApplyThenLaunch, "순정 상태, 백업 이후 점검 없음")
                    : new(LaunchAction.Update, "백업 이후 점검이 있었습니다.", hit.Title);
            }
            case GameFileState.PristineWithoutBackup:
                return new(LaunchAction.Update, "이 게임 버전의 백업이 아직 없습니다.");
            case GameFileState.ForeignPatched when !status.HasBackup && status.OtherBackupVersions.Count > 0:
                return new(LaunchAction.Blocked, "한글이 적용된 채로 게임이 업데이트됐습니다. 게임 데이터를 순정으로 되돌려야 합니다(런처 복구 기능이 없으면 게임 데이터 재설치).");
            default:
                return new(LaunchAction.Blocked, "게임 파일이 이 도구의 백업·적용 기록과 다릅니다. 관리 메뉴에서 상태를 확인하세요.");
        }
    }

    private static MaintenanceWindow? Since(IReadOnlyList<MaintenanceWindow> list, DateTime safeUtc, DateTime nowUtc) =>
        list.Where(m => m.StartUtc <= nowUtc && m.EndUtc > safeUtc).OrderByDescending(m => m.StartUtc).FirstOrDefault();

    private static DateTime Max(DateTime a, DateTime? b) => b > a ? b.Value : a;
}
