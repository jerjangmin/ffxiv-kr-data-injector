using System.Buffers.Binary;
using System.Text;

namespace FFXIVInjector.Mac;

/// <summary>
/// The upstream injector merges CSV values into EXD rows by byte offset. A CSV made for an older column layout
/// would write into the wrong fields, so such sheets are left out through the injector's file filter.
/// Kept here (not in upstream Core) so the tool builds against any unmodified upstream release.
/// </summary>
public static class ExhCompatibility
{
    public sealed record ExhColumns(List<int> Offsets, List<bool> IsString);

    public static ExhColumns? ParseExh(byte[] exh)
    {
        if (exh.Length < 0x20 || Encoding.ASCII.GetString(exh, 0, 4) != "EXHF") return null;
        int count = BinaryPrimitives.ReadUInt16BigEndian(exh.AsSpan(0x08));
        var columns = new ExhColumns(new(), new());
        for (int i = 0; i < count && 0x20 + i * 4 + 4 <= exh.Length; i++)
        {
            columns.IsString.Add(BinaryPrimitives.ReadUInt16BigEndian(exh.AsSpan(0x20 + i * 4)) == 0); // type 0 = string
            columns.Offsets.Add(BinaryPrimitives.ReadUInt16BigEndian(exh.AsSpan(0x20 + i * 4 + 2)));
        }
        return columns;
    }

    /// <summary>Reads lines 3 (offset,...) and 4 (types) of an exported sheet CSV.</summary>
    public static (List<int> Offsets, List<string> Types) ReadCsvHeader(string path)
    {
        using var reader = new StreamReader(path);
        reader.ReadLine(); reader.ReadLine();
        var offsets = (reader.ReadLine() ?? "").Split(',').Skip(1).Select(s => int.TryParse(s.Trim(), out int o) ? o : -1).ToList();
        var types = (reader.ReadLine() ?? "").Split(',').Skip(1).Select(s => s.Trim()).ToList();
        return (offsets, types);
    }

    /// <summary>Null when every CSV column exists in the EXH with the same string/non-string kind; otherwise the reason.</summary>
    public static string? Check(IReadOnlyList<int> csvOffsets, IReadOnlyList<string> csvTypes, ExhColumns exh)
    {
        for (int i = 0; i < csvOffsets.Count; i++)
        {
            int offset = csvOffsets[i];
            if (offset < 0) return $"column {i}: invalid offset";
            int idx = exh.Offsets.IndexOf(offset);
            if (idx < 0) return $"column {i}: offset {offset} not in EXH";
            if (i < csvTypes.Count && csvTypes[i].Equals("str", StringComparison.OrdinalIgnoreCase) != exh.IsString[idx])
                return $"column {i}: offset {offset} is {(exh.IsString[idx] ? "string" : "numeric")} in EXH but {csvTypes[i]} in CSV";
        }
        return null;
    }

    /// <summary>File filter for CsvInjector.Inject: every resource file except CSVs that do not fit the game's EXH.</summary>
    public static HashSet<string> BuildFilter(string gamePath, string resourcesRoot, Action<string> log)
    {
        var allowed = new HashSet<string>();
        using var lumina = new Lumina.GameData(Path.Combine(gamePath, "sqpack"));
        foreach (var file in Directory.EnumerateFiles(resourcesRoot, "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is not (".csv" or ".exd" or ".fdt" or ".tex")) continue;
            string rel = Path.GetRelativePath(resourcesRoot, file).Replace('\\', '/').ToLowerInvariant();
            if (ext == ".csv")
            {
                string sheet = rel.StartsWith("rawexd/") ? rel[7..^4] : rel[..^4];
                var exh = lumina.GetFile($"exd/{sheet}.exh")?.Data;
                var columns = exh == null ? null : ParseExh(exh);
                if (columns != null)
                {
                    var (offsets, types) = ReadCsvHeader(file);
                    string? reason = Check(offsets, types, columns);
                    if (reason != null) { log($"[건너뜀] {sheet}: CSV 열 구조가 현재 게임과 다름 ({reason})"); continue; }
                }
            }
            allowed.Add(rel);
        }
        return allowed;
    }
}
