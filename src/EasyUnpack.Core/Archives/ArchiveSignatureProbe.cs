using System.Buffers.Binary;

namespace EasyUnpack.Core.Archives;

public static class ArchiveSignatureProbe
{
    private const int ProbeLength = 512;
    private const int ZipEndRecordLength = 22;
    private const int MaximumZipCommentLength = ushort.MaxValue;
    private const int ZipCentralDirectoryHeaderLength = 46;

    public static ArchiveProbeResult Probe(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[ProbeLength];
        var count = stream.Read(buffer, 0, buffer.Length);
        var format = Detect(buffer.AsSpan(0, count));
        if (format != ArchiveFormat.Unknown) return new ArchiveProbeResult(path, format, true);

        var embeddedZip = FindEmbeddedZip(stream);
        return embeddedZip is null
            ? new ArchiveProbeResult(path, ArchiveFormat.Unknown, false)
            : new ArchiveProbeResult(path, ArchiveFormat.Zip, true, embeddedZip.Value.Offset, embeddedZip.Value.Length);
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

    private static (long Offset, long Length)? FindEmbeddedZip(FileStream stream)
    {
        if (stream.Length < ZipEndRecordLength) return null;

        var tailLength = (int)Math.Min(stream.Length, ZipEndRecordLength + MaximumZipCommentLength);
        var tailOffset = stream.Length - tailLength;
        var tail = new byte[tailLength];
        stream.Position = tailOffset;
        stream.ReadExactly(tail);
        Span<byte> centralDirectoryHeader = stackalloc byte[ZipCentralDirectoryHeaderLength];
        Span<byte> localHeaderSignature = stackalloc byte[4];

        for (var index = tail.Length - ZipEndRecordLength; index >= 0; index--)
        {
            var endRecord = tail.AsSpan(index);
            if (!StartsWith(endRecord, 0x50, 0x4B, 0x05, 0x06)) continue;

            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(endRecord[20..]);
            var endRecordLength = ZipEndRecordLength + commentLength;
            if (index + endRecordLength > tail.Length) continue;

            var diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(endRecord[4..]);
            var centralDirectoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(endRecord[6..]);
            var entriesOnDisk = BinaryPrimitives.ReadUInt16LittleEndian(endRecord[8..]);
            var totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(endRecord[10..]);
            if (diskNumber != 0 || centralDirectoryDisk != 0 || entriesOnDisk != totalEntries || totalEntries == 0) continue;

            var centralDirectorySize = BinaryPrimitives.ReadUInt32LittleEndian(endRecord[12..]);
            var centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(endRecord[16..]);
            var endRecordOffset = tailOffset + index;
            var archiveOffset = endRecordOffset - centralDirectorySize - centralDirectoryOffset;
            if (archiveOffset <= 0) continue;

            var centralDirectoryAbsoluteOffset = archiveOffset + centralDirectoryOffset;
            if (centralDirectoryAbsoluteOffset < archiveOffset ||
                centralDirectoryAbsoluteOffset + centralDirectorySize != endRecordOffset ||
                centralDirectorySize < ZipCentralDirectoryHeaderLength)
            {
                continue;
            }

            stream.Position = centralDirectoryAbsoluteOffset;
            stream.ReadExactly(centralDirectoryHeader);
            if (!StartsWith(centralDirectoryHeader, 0x50, 0x4B, 0x01, 0x02)) continue;

            var localHeaderOffset = BinaryPrimitives.ReadUInt32LittleEndian(centralDirectoryHeader[42..]);
            var localHeaderAbsoluteOffset = archiveOffset + localHeaderOffset;
            if (localHeaderAbsoluteOffset < archiveOffset || localHeaderAbsoluteOffset >= centralDirectoryAbsoluteOffset) continue;

            stream.Position = localHeaderAbsoluteOffset;
            stream.ReadExactly(localHeaderSignature);
            if (StartsWith(localHeaderSignature, 0x50, 0x4B, 0x03, 0x04))
            {
                return (archiveOffset, endRecordOffset + endRecordLength - archiveOffset);
            }
        }

        return null;
    }
}
