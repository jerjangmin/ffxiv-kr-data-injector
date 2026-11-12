using FFXIVInjector.Core;
namespace FFXIVInjector.Mac;

/// <summary>Locations used by the official macOS client (CrossOver bottle) and by the macOS tool.</summary>
public static class MacPaths
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Install root inside the bottle (contains boot/ and game/). The .app bundle has no sqpack.</summary>
    public static string BottleInstallRoot => Path.Combine(Home, "Library", "Application Support", "FINAL FANTASY XIV ONLINE",
        "Bottles", "published_Final_Fantasy", "drive_c", "Program Files (x86)", "SquareEnix", "FINAL FANTASY XIV - A Realm Reborn");

    public static string DefaultGamePath => Path.Combine(BottleInstallRoot, "game");

    /// <summary>Backups, apply records, downloaded resources and logs. Same APFS volume as the bottle, so cp -c clones.</summary>
    public static string DefaultStateRoot => Path.Combine(Home, "Library", "Application Support", "FFXIVInjector-mac");

    public static string ConfigPath => Path.Combine(Home, "Documents", "My Games", "FINAL FANTASY XIV - A Realm Reborn", "FFXIV.cfg");

    /// <summary>Reads "Language N" from FFXIV.cfg (0 ja, 1 en, 2 de, 3 fr). Null when unknown.</summary>
    public static GameLanguage? ReadClientLanguage(string? cfgPath = null)
    {
        try
        {
            foreach (var line in File.ReadLines(cfgPath ?? ConfigPath))
            {
                var parts = line.Split('\t', ' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[0] == "Language" && int.TryParse(parts[1], out int value))
                    return value switch { 0 => GameLanguage.Japanese, 1 => GameLanguage.English, 2 => GameLanguage.German, 3 => GameLanguage.French, _ => null };
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }
}
