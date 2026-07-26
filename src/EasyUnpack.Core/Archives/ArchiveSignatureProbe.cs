namespace EasyUnpack.Core.Archives;

public static class ArchiveSignatureProbe
{
    private const int ProbeLength = 512;

    public static ArchiveProbeResult Probe(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[ProbeLength];
        var count = stream.Read(buffer, 0, buffer.Length);
        var format = Detect(buffer.AsSpan(0, count));
        return new ArchiveProbeResult(path, format, format != ArchiveFormat.Unknown);
    }

    public static ArchiveFormat Detect(ReadOnlySpan<byte> data)
    {
        if (StartsWith(data, 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C)) return ArchiveFormat.SevenZip;
        if (StartsWith(data, 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00)) return ArchiveFormat.Rar;
        if (StartsWith(data, 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00)) return ArchiveFormat.Rar5;
        if (StartsWith(data, 0x50, 0x4B, 0x03, 0x04) || StartsWith(data, 0x50, 0x4B, 0x05, 0x06) || StartsWith(data, 0x50, 0x4B, 0x07, 0x08)) return ArchiveFormat.Zip;
        if (StartsWith(data, 0x1F, 0x8B, 0x08)) return ArchiveFormat.GZip;
        if (StartsWith(data, 0x42, 0x5A, 0x68)) return ArchiveFormat.BZip2;
        if (StartsWith(data, 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00)) return ArchiveFormat.Xz;
        if (StartsWith(data, 0x28, 0xB5, 0x2F, 0xFD)) return ArchiveFormat.Zstandard;
        if (StartsWith(data, 0x4D, 0x53, 0x43, 0x46, 0x00, 0x00, 0x00, 0x00)) return ArchiveFormat.Cab;
        if (StartsWith(data, 0x60, 0xEA)) return ArchiveFormat.Arj;
        if (data.Length >= 7 && data[2] == (byte)'-' && data[3] == (byte)'l' && data[6] == (byte)'-') return ArchiveFormat.Lzh;
        if (data.Length >= 262 && data.Slice(257, 5).SequenceEqual("ustar"u8)) return ArchiveFormat.Tar;

        return ArchiveFormat.Unknown;
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, params byte[] signature) =>
        data.Length >= signature.Length && data[..signature.Length].SequenceEqual(signature);
}
