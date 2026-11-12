using System.IO.Compression;
using System.Text;
using FFXIVInjector.Mac;

/// <summary>The resource URL is read from upstream's released single-file executable on every new release.</summary>
internal static class ReleaseUrlExtractorTests
{
    private const string Url = "https://d1example0.cloudfront.net/";

    public static void Run()
    {
        byte[] core = Encoding.Unicode.GetBytes("\u0001https://discord.gg/abc\u0000" + Url + "\u0000");
        Assert(ReleaseUrlExtractor.Extract(Bundle(core, compress: true)) == Url, "compressed bundle entry");
        Assert(ReleaseUrlExtractor.Extract(Bundle(core, compress: false)) == Url, "uncompressed bundle entry");
        Assert(ReleaseUrlExtractor.Extract([0x00, .. core]) == Url, "plain assembly at odd alignment");
        Assert(ReleaseUrlExtractor.Extract(Bundle(Encoding.Unicode.GetBytes("no url here"), compress: true)) == null, "missing URL gives null");
        Console.WriteLine("PASS: resource URL extraction from single-file bundle (compressed/uncompressed/plain/missing).");
    }

    /// <summary>Minimal .NET 6+ single-file bundle: payload, manifest, then the header offset just before the signature.</summary>
    private static byte[] Bundle(byte[] coreDll, bool compress)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("MZ-fake-apphost"));
        byte[] stored = coreDll;
        if (compress)
        {
            using var c = new MemoryStream();
            using (var d = new DeflateStream(c, CompressionLevel.Optimal, leaveOpen: true)) d.Write(coreDll);
            stored = c.ToArray();
        }
        long payload = ms.Position;
        w.Write(stored);
        long header = ms.Position;
        w.Write(6u); w.Write(0u); w.Write(1);
        WriteString(w, "bundle-id");
        w.Write(new byte[8 * 4 + 8]);
        w.Write(payload); w.Write((long)coreDll.Length); w.Write(compress ? (long)stored.Length : 0L);
        w.Write((byte)1);
        WriteString(w, "FFXIVInjector.Core.dll");
        w.Write(header);
        w.Write(Convert.FromHexString("8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae"));
        return ms.ToArray();
    }

    private static void WriteString(BinaryWriter w, string s)
    {
        byte[] b = Encoding.UTF8.GetBytes(s);
        w.Write((byte)b.Length); // lengths here are < 128
        w.Write(b);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
