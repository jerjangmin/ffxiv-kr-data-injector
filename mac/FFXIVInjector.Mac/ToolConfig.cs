using System.Text.Json;

namespace FFXIVInjector.Mac;

/// <summary>state/config.json. The updater fills these in from the latest upstream release.</summary>
public sealed class ToolConfig
{
    /// <summary>Base URL of the published Korean resources (rawexd.zip, font.zip, version.txt).</summary>
    public string ResourceBaseUrl { get; set; } = "";
    /// <summary>Upstream release whose executable the URL was read from.</summary>
    public string? ResourceUrlSource { get; set; }
    /// <summary>The user chose "just play" after a maintenance; maintenances ending before this are not asked again.</summary>
    public DateTime? MaintenanceAcknowledgedUtc { get; set; }

    public static string PathIn(string stateRoot) => Path.Combine(stateRoot, "config.json");

    public static ToolConfig Load(string stateRoot)
    {
        string path = PathIn(stateRoot);
        return File.Exists(path) ? JsonSerializer.Deserialize<ToolConfig>(File.ReadAllText(path)) ?? new() : new();
    }

    public void Save(string stateRoot)
    {
        Directory.CreateDirectory(stateRoot);
        string path = PathIn(stateRoot), temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}
