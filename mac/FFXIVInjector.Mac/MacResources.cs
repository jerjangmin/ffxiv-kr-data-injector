using FFXIVInjector.Core;
using System.IO.Compression;
using System.Text.Json;

namespace FFXIVInjector.Mac;

/// <summary>Korean text (rawexd) and font resources: a root folder that contains rawexd/ and font/.</summary>
public sealed record ResourceSet(string Root, string TextVersion, string FontVersion);

public interface IResourceProvider
{
    Task<ResourceSet> EnsureAsync(Action<string> log, CancellationToken ct = default);
}

/// <summary>Uses an existing folder (for example one prepared by hand). Nothing is downloaded.</summary>
public sealed class LocalResourceProvider(string root) : IResourceProvider
{
    public Task<ResourceSet> EnsureAsync(Action<string> log, CancellationToken ct = default)
    {
        string full = Path.GetFullPath(root);
        if (!Directory.Exists(Path.Combine(full, "rawexd")) || !Directory.Exists(Path.Combine(full, "font")))
            throw new DirectoryNotFoundException($"리소스 폴더에 rawexd/와 font/가 필요합니다: {full}");
        return Task.FromResult(new ResourceSet(full, "local", "local"));
    }
}

/// <summary>Downloads the published stable rawexd.zip and font.zip (not original.zip) into the state folder.</summary>
public sealed class DownloadResourceProvider(string root, string baseUrl, bool offline = false) : IResourceProvider
{
    private sealed class Versions { public string Text { get; set; } = ""; public string Font { get; set; } = ""; }

    public async Task<ResourceSet> EnsureAsync(Action<string> log, CancellationToken ct = default)
    {
        Directory.CreateDirectory(root);
        string versionsPath = Path.Combine(root, "versions.json");
        var local = File.Exists(versionsPath) ? JsonSerializer.Deserialize<Versions>(File.ReadAllText(versionsPath)) ?? new() : new();
        bool haveText = local.Text.Length > 0 && Directory.Exists(Path.Combine(root, "rawexd"));
        bool haveFont = local.Font.Length > 0 && Directory.Exists(Path.Combine(root, "font"));
        if (offline)
        {
            if (!haveText || !haveFont) throw new InvalidOperationException("오프라인 모드인데 받아 둔 한글 리소스가 없습니다.");
            return new ResourceSet(root, local.Text, local.Font);
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.Add("User-Agent", "FFXIVInjector-mac");
        string remoteText, remoteFont;
        try
        {
            remoteText = ParseVersionText(await client.GetStringAsync(Url("version.txt"), ct));
            remoteFont = ParseVersionText(await client.GetStringAsync(Url("font-version.txt"), ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            if (haveText && haveFont)
            {
                log($"리소스 버전 확인 실패({ex.Message}). 받아 둔 리소스를 씁니다: 텍스트 {local.Text}, 폰트 {local.Font}");
                return new ResourceSet(root, local.Text, local.Font);
            }
            throw new InvalidOperationException("한글 리소스를 받을 수 없습니다: " + ex.Message, ex);
        }

        if (!haveText || CompareVersions(remoteText, local.Text) > 0)
        {
            await DownloadAndSwapAsync(client, "rawexd.zip", "rawexd", log, ct);
            local.Text = remoteText;
            File.WriteAllText(versionsPath, JsonSerializer.Serialize(local));
        }
        if (!haveFont || CompareVersions(remoteFont, local.Font) > 0)
        {
            await DownloadAndSwapAsync(client, "font.zip", "font", log, ct);
            local.Font = remoteFont;
            File.WriteAllText(versionsPath, JsonSerializer.Serialize(local));
        }
        log($"한글 리소스: 텍스트 {local.Text}, 폰트 {local.Font}");
        return new ResourceSet(root, local.Text, local.Font);
    }

    /// <summary>version.txt may list several branches ("stable 2026.10.06..."); take the stable line's number.</summary>
    public static string ParseVersionText(string raw)
    {
        string search = raw.Trim();
        foreach (var line in search.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            if (line.Contains("stable", StringComparison.OrdinalIgnoreCase)) { search = line; break; }
        var match = System.Text.RegularExpressions.Regex.Match(search, @"\d+(\.\d+)+");
        return match.Success ? match.Value : throw new InvalidDataException("리소스 버전 형식을 알 수 없습니다: " + raw);
    }

    public static int CompareVersions(string a, string b)
    {
        long[] Parse(string v) => v.Split('.').Select(s => long.TryParse(s, out var n) ? n : 0).ToArray();
        var x = Parse(a); var y = Parse(b);
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            long p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
            if (p != q) return p.CompareTo(q);
        }
        return 0;
    }

    private string Url(string name) => $"{baseUrl.TrimEnd('/')}/{name}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

    private async Task DownloadAndSwapAsync(HttpClient client, string zipName, string folder, Action<string> log, CancellationToken ct)
    {
        string token = Guid.NewGuid().ToString("N");
        string zip = Path.Combine(root, $".dl-{token}.zip");
        string staging = Path.Combine(root, $".new-{folder}-{token}");
        string target = Path.Combine(root, folder);
        try
        {
            log($"{zipName} 내려받는 중...");
            using (var response = await client.GetAsync(Url(zipName), HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(zip);
                await input.CopyToAsync(output, ct);
            }
            ZipFile.ExtractToDirectory(zip, staging);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(staging, target);
        }
        finally
        {
            if (File.Exists(zip)) File.Delete(zip);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }
}
