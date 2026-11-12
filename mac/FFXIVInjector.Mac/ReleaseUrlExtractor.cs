using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FFXIVInjector.Mac;

/// <summary>
/// The resource URL lives in upstream's untracked Secrets.cs and is compiled into the released Windows executable.
/// Reads it back from the .NET single-file bundle (FFXIVInjector.Core.dll, UTF-16 user string) so the Mac tool
/// follows the URL of each new upstream release.
/// </summary>
public static partial class ReleaseUrlExtractor
{
    public const string Repository = "dambyul/ffxiv-kr-data-injector";
    // .NET single-file bundle marker (SHA-256 of ".net core bundle").
    private static readonly byte[] BundleSignature = Convert.FromHexString("8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae");

    [GeneratedRegex(@"https://[A-Za-z0-9.\-]+\.cloudfront\.net/[A-Za-z0-9._\-/]*")]
    private static partial Regex CloudFrontUrl();

    public static async Task<string> FromReleaseAsync(string tag, Action<string> log, CancellationToken ct = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.Add("User-Agent", "FFXIVInjector-mac");
        string api = $"https://api.github.com/repos/{Repository}/releases/tags/{Uri.EscapeDataString(tag)}";
        using var release = JsonDocument.Parse(await client.GetStringAsync(api, ct));
        string? asset = release.RootElement.GetProperty("assets").EnumerateArray()
            .Select(a => a.GetProperty("browser_download_url").GetString())
            .FirstOrDefault(u => u != null && u.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (asset == null) throw new InvalidDataException($"릴리스 {tag}에 exe가 없습니다.");
        log($"릴리스 {tag} 실행 파일에서 리소스 주소를 읽는 중...");
        byte[] exe = await client.GetByteArrayAsync(asset, ct);
        return Extract(exe) ?? throw new InvalidDataException($"릴리스 {tag} 실행 파일에서 리소스 주소를 찾지 못했습니다.");
    }

    /// <summary>Finds the CloudFront base URL in a bundled executable, or in a plain assembly.</summary>
    public static string? Extract(byte[] exe)
    {
        var core = BundleEntry(exe, "FFXIVInjector.Core.dll");
        return FindUrl(core ?? exe);
    }

    public static string? FindUrl(byte[] assembly)
    {
        foreach (var text in new[] { Encoding.Unicode.GetString(assembly), Encoding.Unicode.GetString(assembly, 1, assembly.Length - 1) })
        {
            var match = CloudFrontUrl().Match(text);
            if (match.Success) return match.Value.EndsWith('/') ? match.Value : match.Value + "/";
        }
        return null;
    }

    /// <summary>Reads one file out of a .NET 6+ single-file bundle manifest. Null when not a bundle or not found.</summary>
    public static byte[]? BundleEntry(byte[] exe, string name)
    {
        int sig = exe.AsSpan().IndexOf(BundleSignature);
        if (sig < 8) return null;
        long header = BinaryPrimitives.ReadInt64LittleEndian(exe.AsSpan(sig - 8));
        if (header <= 0 || header >= exe.Length) return null;
        int p = (int)header;
        uint major = BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(p));
        int count = BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(p + 8));
        p += 12;
        ReadString(exe, ref p); // bundle id
        if (major >= 2) p += 8 * 4 + 8; // deps.json / runtimeconfig.json locations, flags
        for (int i = 0; i < count; i++)
        {
            long offset = BinaryPrimitives.ReadInt64LittleEndian(exe.AsSpan(p));
            long size = BinaryPrimitives.ReadInt64LittleEndian(exe.AsSpan(p + 8));
            long compressed = 0;
            p += 16;
            if (major >= 6) { compressed = BinaryPrimitives.ReadInt64LittleEndian(exe.AsSpan(p)); p += 8; }
            p += 1; // file type
            string path = ReadString(exe, ref p);
            if (!path.Replace('\\', '/').EndsWith("/" + name, StringComparison.OrdinalIgnoreCase) && !path.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (compressed == 0) return exe.AsSpan((int)offset, (int)size).ToArray();
            using var input = new DeflateStream(new MemoryStream(exe, (int)offset, (int)compressed), CompressionMode.Decompress);
            using var output = new MemoryStream();
            input.CopyTo(output);
            return output.ToArray();
        }
        return null;
    }

    private static string ReadString(byte[] data, ref int p)
    {
        int length = 0, shift = 0;
        while (true)
        {
            byte b = data[p++];
            length |= (b & 0x7f) << shift;
            shift += 7;
            if (b < 0x80) break;
        }
        string s = Encoding.UTF8.GetString(data, p, length);
        p += length;
        return s;
    }
}
