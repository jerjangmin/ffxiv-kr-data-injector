using System.Diagnostics;
using System.Linq;
using FFXIVInjector.Core.Mac;

namespace FFXIVInjector.Core;

public static class GameProcess
{
    private static readonly string[] ProcessNames = { "ffxiv_dx11", "ffxiv" };

    public static bool IsRunning() => GetActiveProcessName().Length > 0;

    public static string GetActiveProcessName()
    {
        foreach (var name in ProcessNames)
        {
            if (Process.GetProcessesByName(name).Any()) return name;
        }
        // Under CrossOver/Wine the game runs inside wine processes whose names are not "ffxiv_dx11".
        if (OperatingSystem.IsMacOS())
        {
            var found = new MacProcessGuard().FindGameProcesses();
            if (found.Count > 0) return found[0];
        }
        return string.Empty;
    }

    /// <summary>
    /// Throws when another process may be using the file. Windows enforces FileShare.None; Unix does not,
    /// so on macOS the open handles are checked with lsof instead.
    /// </summary>
    public static void EnsureFileNotInUse(string path)
    {
        if (!File.Exists(path)) return;
        if (OperatingSystem.IsMacOS())
        {
            var holders = new MacProcessGuard().FindOpenHandles([path]);
            if (holders.Count > 0)
                throw new IOException($"다른 프로세스가 파일을 사용 중입니다: {Path.GetFileName(path)} ({string.Join(", ", holders)})");
            return;
        }
        using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
}
