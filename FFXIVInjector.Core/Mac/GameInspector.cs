using System.Buffers.Binary;
using System.Text;

namespace FFXIVInjector.Core.Mac;

public sealed class InspectionResult
{
    /// <summary>Hangul syllables in the Addon sheet (page 0) across all client languages.</summary>
    public int TextHangul { get; set; }
    /// <summary>Hangul glyphs in common/font/axis_12.fdt.</summary>
    public int FontHangulGlyphs { get; set; }
    public int IndexEntries { get; set; }
    public int SheetPagesRead { get; set; }
    public int FontFilesRead { get; set; }
    public List<string> Problems { get; } = new();
    public string Sample { get; set; } = "";

    public bool LooksPristine => TextHangul == 0 && FontHangulGlyphs == 0;
    public bool LooksPatched => TextHangul > 0 && FontHangulGlyphs > 0;
}

public interface IGameInspector
{
    /// <summary>Quick mode reads indexes, the Addon sheet and one font. Deep mode reads back every EXD page and the font textures.</summary>
    InspectionResult Inspect(string gamePath, bool deep);
}

/// <summary>Read-only checks of the sqpack files this tool touches (ffxiv/000000 fonts, ffxiv/0a0000 EXD).</summary>
public sealed class GameInspector : IGameInspector
{
    private static readonly string[] Domains = ["000000", "0a0000"];
    private static readonly (int Id, string Suffix)[] ExhLanguages = [(0, ""), (1, "_ja"), (2, "_en"), (3, "_de"), (4, "_fr")];

    public InspectionResult Inspect(string gamePath, bool deep)
    {
        var result = new InspectionResult();
        string ffxiv = Path.Combine(gamePath, "sqpack", "ffxiv");
        foreach (var domain in Domains)
        {
            CheckIndex(ffxiv, domain, false, result);
            CheckIndex(ffxiv, domain, true, result);
        }
        if (result.Problems.Count > 0) return result; // Do not let Lumina read through a broken index.

        using var lumina = new Lumina.GameData(Path.Combine(gamePath, "sqpack"));
        foreach (var lang in new[] { "ja", "en", "de", "fr" })
        {
            var addon = TryGet(lumina, $"exd/addon_0_{lang}.exd", result);
            if (addon == null) continue;
            string text = Encoding.UTF8.GetString(addon);
            result.TextHangul += text.Count(IsHangul);
            if (result.Sample.Length == 0 && text.Any(IsHangul))
                result.Sample = string.Join(" / ", text.Split('\0').Where(s => s.Length is > 0 and < 20 && s.Any(IsHangul)).Take(5));
        }
        var fdt = TryGet(lumina, "common/font/axis_12.fdt", result);
        if (fdt == null) result.Problems.Add("common/font/axis_12.fdt를 읽을 수 없음");
        else result.FontHangulGlyphs = CountHangulGlyphs(fdt);

        if (deep)
        {
            ReadAllSheets(lumina, result);
            foreach (var name in FontFiles())
                if (lumina.FileExists(name) && TryGet(lumina, name, result) != null) result.FontFilesRead++;
            if (result.FontFilesRead == 0) result.Problems.Add("폰트 파일을 하나도 읽지 못함");
        }
        return result;
    }

    public static bool IsHangul(char c) => c is >= '\uAC00' and <= '\uD7A3';

    /// <summary>FDT glyph entries start at 0x40, 16 bytes each, beginning with the UTF-8 code packed into a uint32.</summary>
    public static int CountHangulGlyphs(byte[] fdt)
    {
        if (fdt.Length < 0x40 || Encoding.ASCII.GetString(fdt, 0, 4) != "fcsv") return 0;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(fdt.AsSpan(0x24));
        int n = 0;
        for (long i = 0; i < count && 0x40 + i * 16 + 4 <= fdt.Length; i++)
        {
            uint utf8 = BinaryPrimitives.ReadUInt32LittleEndian(fdt.AsSpan((int)(0x40 + i * 16)));
            if (utf8 is >= 0xEAB080 and <= 0xED9EA3) n++; // U+AC00 .. U+D7A3
        }
        return n;
    }

    private static void CheckIndex(string ffxiv, string domain, bool index2, InspectionResult result)
    {
        string path = Path.Combine(ffxiv, $"{domain}.win32.index{(index2 ? "2" : "")}");
        if (!File.Exists(path)) { result.Problems.Add($"{Path.GetFileName(path)} 없음"); return; }
        byte[] d = File.ReadAllBytes(path);
        if (d.Length < 0x410 || Encoding.ASCII.GetString(d, 0, 6) != "SqPack") { result.Problems.Add($"{Path.GetFileName(path)} 헤더 이상"); return; }
        int off = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x408));
        int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0x40C));
        int stride = index2 ? 8 : 16;
        if (off < 0 || size < 0 || (long)off + size > d.Length || size % stride != 0) { result.Problems.Add($"{Path.GetFileName(path)} 세그먼트 범위 이상"); return; }
        var datLengths = new Dictionary<int, long>();
        int bad = 0, unsorted = 0;
        ulong prev = 0;
        for (int i = 0; i < size; i += stride)
        {
            ulong hash = index2 ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(off + i)) : BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(off + i));
            if (hash < prev) unsorted++;
            prev = hash;
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(off + i + (index2 ? 4 : 8)));
            int datId = (int)((v >> 1) & 7);
            long pos = (long)(v & ~0xFu) << 3;
            if (!datLengths.TryGetValue(datId, out long len))
            {
                string dat = Path.Combine(ffxiv, $"{domain}.win32.dat{datId}");
                datLengths[datId] = len = File.Exists(dat) ? new FileInfo(dat).Length : -1;
            }
            if (pos >= len) bad++;
        }
        if (!index2) result.IndexEntries += size / stride;
        if (bad > 0) result.Problems.Add($"{Path.GetFileName(path)}: dat 범위를 벗어난 항목 {bad}개");
        if (unsorted > 0) result.Problems.Add($"{Path.GetFileName(path)}: 해시 정렬 깨짐 {unsorted}곳");
    }

    private static void ReadAllSheets(Lumina.GameData lumina, InspectionResult result)
    {
        var root = TryGet(lumina, "exd/root.exl", result);
        if (root == null) { result.Problems.Add("exd/root.exl을 읽을 수 없음"); return; }
        foreach (var line in Encoding.UTF8.GetString(root).Split('\n').Skip(1))
        {
            string name = line.Split(',')[0].Trim().ToLowerInvariant();
            if (name.Length == 0) continue;
            var exh = TryGet(lumina, $"exd/{name}.exh", result);
            if (exh == null || exh.Length < 0x20 || Encoding.ASCII.GetString(exh, 0, 4) != "EXHF") continue;
            int columns = BinaryPrimitives.ReadUInt16BigEndian(exh.AsSpan(0x08));
            int pages = BinaryPrimitives.ReadUInt16BigEndian(exh.AsSpan(0x0A));
            int languages = BinaryPrimitives.ReadUInt16BigEndian(exh.AsSpan(0x0C));
            int pageTable = 0x20 + columns * 4, langTable = pageTable + pages * 8;
            if (langTable + languages * 2 > exh.Length) { result.Problems.Add($"{name}.exh 구조 이상"); continue; }
            var langIds = Enumerable.Range(0, languages).Select(i => (int)exh[langTable + i * 2]).ToHashSet();
            for (int p = 0; p < pages; p++)
            {
                uint start = BinaryPrimitives.ReadUInt32BigEndian(exh.AsSpan(pageTable + p * 8));
                foreach (var (id, suffix) in ExhLanguages.Where(l => langIds.Contains(l.Id)))
                {
                    string file = $"exd/{name}_{start}{suffix}.exd";
                    var data = TryGet(lumina, file, result);
                    if (data == null) continue; // Not every declared language ships a file.
                    if (data.Length < 32 || Encoding.ASCII.GetString(data, 0, 4) != "EXDF") { result.Problems.Add($"{file}: EXDF 헤더 이상"); continue; }
                    uint indexSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x08));
                    uint dataSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x0C));
                    if (32L + indexSize + dataSize > data.Length) { result.Problems.Add($"{file}: 크기 필드 이상"); continue; }
                    result.SheetPagesRead++;
                }
            }
        }
        if (result.SheetPagesRead == 0) result.Problems.Add("EXD 페이지를 하나도 읽지 못함");
    }

    private static IEnumerable<string> FontFiles()
    {
        foreach (var family in new[] { "axis", "jupiter", "meidinger", "miedingermid", "trumpgothic" })
            foreach (var size in new[] { 12, 14, 16, 18, 20, 23, 34, 36, 45, 46, 68, 90, 96 })
            {
                yield return $"common/font/{family}_{size}.fdt";
                yield return $"common/font/{family}_{size}_lobby.fdt";
            }
        for (int i = 1; i <= 8; i++)
        {
            yield return $"common/font/font{i}.tex";
            yield return $"common/font/font_lobby{i}.tex";
        }
    }

    private static byte[]? TryGet(Lumina.GameData lumina, string path, InspectionResult result)
    {
        try { return lumina.GetFile(path)?.Data; }
        catch (Exception ex) { result.Problems.Add($"{path}: 읽기 실패 ({ex.GetType().Name}: {ex.Message})"); return null; }
    }
}
