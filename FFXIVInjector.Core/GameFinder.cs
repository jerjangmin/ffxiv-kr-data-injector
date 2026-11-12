
namespace FFXIVInjector.Core;

public class GameFinder
{
    public string? FindGamePath()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows Registry Logic
            return FindWindowsGamePath();
        }
        else if (OperatingSystem.IsMacOS())
        {
            // MacOS Default Path Logic
            return FindMacGamePath();
        }
        
        return null;
    }

    private string? FindWindowsGamePath()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            // Try standard registry key
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SquareEnix\FINAL FANTASY XIV - A Realm Reborn");
            if (key != null)
            {
                var path = key.GetValue("install_location") as string;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    return path;
                }
            }
            
            // Try common default paths
            string[] commonPaths = {
                @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn",
                @"C:\Program Files\SquareEnix\FINAL FANTASY XIV - A Realm Reborn"
            };

            foreach (var path in commonPaths)
            {
                if (Directory.Exists(path)) return path;
            }
        }
        catch 
        {
            // Ignore registry errors
        }

        return null;
    }

    private string? FindMacGamePath()
    {
        // The game files live in the CrossOver bottle, not in the .app bundle (which has no sqpack).
        string root = Mac.MacPaths.BottleInstallRoot;
        return File.Exists(Path.Combine(root, "game", "sqpack", "ffxiv", "0a0000.win32.index")) ? root : null;
    }
}
