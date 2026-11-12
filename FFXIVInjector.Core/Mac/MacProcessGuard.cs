using System.Diagnostics;

namespace FFXIVInjector.Core.Mac;

/// <summary>Detects the game, its launcher and CrossOver/Wine processes, and processes holding target files open.</summary>
public interface IGameActivityProbe
{
    /// <summary>Running game/launcher/bottle processes as "pid command" lines.</summary>
    IReadOnlyList<string> FindGameProcesses();
    /// <summary>Processes that currently have any of the given files open, as "pid command" lines.</summary>
    IReadOnlyList<string> FindOpenHandles(IReadOnlyCollection<string> files);
}

/// <summary>
/// macOS probe. Process.GetProcessesByName does not see Wine processes by their .exe names,
/// and FileShare.None is not enforced on Unix, so this uses pgrep -f and lsof instead.
/// </summary>
public sealed class MacProcessGuard : IGameActivityProbe
{
    // Matched case-insensitively against full command lines.
    public const string GameProcessPattern =
        @"ffxiv_dx11\.exe|ffxiv\.exe|ffxivboot|ffxivlauncher|ffxivupdater|ffxivsysinfo|published_Final_Fantasy|FINAL FANTASY XIV ONLINE\.app/Contents/";

    public IReadOnlyList<string> FindGameProcesses()
    {
        var (code, output) = Run("/usr/bin/pgrep", ["-f", "-i", "-l", GameProcessPattern]);
        // pgrep: 0 = match, 1 = no match, anything else = failure. Fail closed.
        if (code == 1) return [];
        if (code != 0) throw new InvalidOperationException("pgrep 실행 실패로 게임 실행 여부를 확인할 수 없습니다.");
        return ParsePgrep(output, SelfAndAncestors());
    }

    public IReadOnlyList<string> FindOpenHandles(IReadOnlyCollection<string> files)
    {
        var existing = files.Where(File.Exists).ToArray();
        if (existing.Length == 0) return [];
        var (code, output) = Run("/usr/sbin/lsof", ["-F", "pc", "--", .. existing]);
        // lsof exits 1 both when nothing is open and on some errors; output is empty for "nothing open".
        if (code != 0 && code != 1) throw new InvalidOperationException("lsof 실행 실패로 파일 사용 여부를 확인할 수 없습니다.");
        return ParseLsof(output, new HashSet<int> { Environment.ProcessId });
    }

    /// <summary>Parses "pid command" lines from pgrep -l, skipping excluded pids (this tool and its parents).</summary>
    public static IReadOnlyList<string> ParsePgrep(string output, ISet<int> excludedPids)
    {
        var result = new List<string>();
        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int space = raw.IndexOf(' ');
            string pidText = space < 0 ? raw : raw[..space];
            if (!int.TryParse(pidText, out int pid) || excludedPids.Contains(pid)) continue;
            result.Add(raw);
        }
        return result;
    }

    /// <summary>Parses lsof -F pc output ("p123", "cname" records).</summary>
    public static IReadOnlyList<string> ParseLsof(string output, ISet<int> excludedPids)
    {
        var result = new List<string>();
        int pid = -1;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line[0] == 'p') int.TryParse(line[1..], out pid);
            else if (line[0] == 'c' && pid > 0 && !excludedPids.Contains(pid) && !result.Any(r => r.StartsWith(pid + " ")))
                result.Add($"{pid} {line[1..]}");
        }
        return result;
    }

    private static HashSet<int> SelfAndAncestors()
    {
        var pids = new HashSet<int>();
        int pid = Environment.ProcessId;
        for (int depth = 0; pid > 1 && depth < 32 && pids.Add(pid); depth++)
        {
            var (code, output) = Run("/bin/ps", ["-o", "ppid=", "-p", pid.ToString()]);
            if (code != 0 || !int.TryParse(output.Trim(), out pid)) break;
        }
        return pids;
    }

    private static (int Code, string Output) Run(string file, string[] args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException(file + " 실행 실패");
        var stdout = p.StandardOutput.ReadToEndAsync();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result);
    }
}
