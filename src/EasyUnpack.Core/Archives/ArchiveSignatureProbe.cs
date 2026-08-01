using System.Buffers.Binary;

namespace EasyUnpack.Core.Archives;

public static class ArchiveSignatureProbe
{
    private const int ProbeLength = 512;
    private const int ZipEndRecordLength = 22;
    private const int MaximumZipCommentLength = ushort.MaxValue;
    private const int ZipCentralDirectoryHeaderLength = 46;
    private const int Zip64EndRecordMinimumLength = 56;
    private const int Zip64LocatorLength = 20;
    private const int MaximumZip64EndRecordLength = 1024 * 1024;

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
            var endRecordOffset = tailOffset + index;
            var classicCentralDirectorySize = BinaryPrimitives.ReadUInt32LittleEndian(endRecord[12..]);
            var classicCentralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(endRecord[16..]);
            var usesZip64 = diskNumber == ushort.MaxValue ||
                            centralDirectoryDisk == ushort.MaxValue ||
                            entriesOnDisk == ushort.MaxValue ||
                            totalEntries == ushort.MaxValue ||
                            classicCentralDirectorySize == uint.MaxValue ||
                            classicCentralDirectoryOffset == uint.MaxValue;

            ZipDirectoryInfo directory;
            if (usesZip64)
            {
                if (!TryReadZip64Directory(stream, endRecord, endRecordOffset, out directory)) continue;
            }
            else
            {
                if (diskNumber != 0 || centralDirectoryDisk != 0 || entriesOnDisk != totalEntries || totalEntries == 0) continue;

                var archiveOffset = endRecordOffset - classicCentralDirectorySize - classicCentralDirectoryOffset;
                if (archiveOffset <= 0) continue;
                var centralDirectoryAbsoluteOffset = archiveOffset + classicCentralDirectoryOffset;
                if (centralDirectoryAbsoluteOffset < archiveOffset ||
                    centralDirectoryAbsoluteOffset + classicCentralDirectorySize != endRecordOffset)
                {
                    continue;
                }

                directory = new ZipDirectoryInfo(archiveOffset, centralDirectoryAbsoluteOffset, classicCentralDirectorySize);
            }

            if (directory.CentralDirectorySize < ZipCentralDirectoryHeaderLength ||
                !TryReadExactly(stream, directory.CentralDirectoryOffset, centralDirectoryHeader))
            {
                continue;
            }
            if (!StartsWith(centralDirectoryHeader, 0x50, 0x4B, 0x01, 0x02)) continue;

            if (!TryReadLocalHeaderOffset(stream, directory, centralDirectoryHeader, out var localHeaderOffset) ||
                !TryAdd(directory.ArchiveOffset, localHeaderOffset, out var localHeaderAbsoluteOffset) ||
                localHeaderAbsoluteOffset < directory.ArchiveOffset ||
                localHeaderAbsoluteOffset >= directory.CentralDirectoryOffset)
            {
                continue;
            }

            if (TryReadExactly(stream, localHeaderAbsoluteOffset, localHeaderSignature) &&
                StartsWith(localHeaderSignature, 0x50, 0x4B, 0x03, 0x04))
            {
                return (directory.ArchiveOffset, endRecordOffset + endRecordLength - directory.ArchiveOffset);
            }
        }

        return null;
    }

    private static bool TryReadZip64Directory(
        FileStream stream,
        ReadOnlySpan<byte> classicEndRecord,
        long classicEndRecordOffset,
        out ZipDirectoryInfo directory)
    {
        directory = default;
        var locatorOffset = classicEndRecordOffset - Zip64LocatorLength;
        Span<byte> locator = stackalloc byte[Zip64LocatorLength];
        if (!TryReadExactly(stream, locatorOffset, locator) || !StartsWith(locator, 0x50, 0x4B, 0x06, 0x07)) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(locator[4..]) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(locator[16..]) != 1)
        {
            return false;
        }

        var zip64EndRecordRelativeOffset = BinaryPrimitives.ReadUInt64LittleEndian(locator[8..]);
        if (zip64EndRecordRelativeOffset > long.MaxValue) return false;

        var searchLength = (int)Math.Min(locatorOffset, MaximumZip64EndRecordLength);
        if (searchLength < Zip64EndRecordMinimumLength) return false;
        var searchOffset = locatorOffset - searchLength;
        var search = new byte[searchLength];
        if (!TryReadExactly(stream, searchOffset, search)) return false;

        for (var index = search.Length - Zip64EndRecordMinimumLength; index >= 0; index--)
        {
            var record = search.AsSpan(index);
            if (!StartsWith(record, 0x50, 0x4B, 0x06, 0x06)) continue;

            var recordPayloadLength = BinaryPrimitives.ReadUInt64LittleEndian(record[4..]);
            if (recordPayloadLength < 44 || recordPayloadLength > (ulong)(search.Length - index - 12)) continue;
            var recordLength = checked((long)recordPayloadLength + 12);
            if (index + recordLength != search.Length) continue;

            var diskNumber = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
            var centralDirectoryDisk = BinaryPrimitives.ReadUInt32LittleEndian(record[20..]);
            var entriesOnDisk = BinaryPrimitives.ReadUInt64LittleEndian(record[24..]);
            var totalEntries = BinaryPrimitives.ReadUInt64LittleEndian(record[32..]);
            var centralDirectorySize = BinaryPrimitives.ReadUInt64LittleEndian(record[40..]);
            var centralDirectoryRelativeOffset = BinaryPrimitives.ReadUInt64LittleEndian(record[48..]);
            if (diskNumber != 0 || centralDirectoryDisk != 0 || entriesOnDisk != totalEntries || totalEntries == 0 ||
                centralDirectorySize > long.MaxValue || centralDirectoryRelativeOffset > long.MaxValue)
            {
                continue;
            }

            if (!ClassicValueMatches(BinaryPrimitives.ReadUInt16LittleEndian(classicEndRecord[4..]), diskNumber) ||
                !ClassicValueMatches(BinaryPrimitives.ReadUInt16LittleEndian(classicEndRecord[6..]), centralDirectoryDisk) ||
                !ClassicValueMatches(BinaryPrimitives.ReadUInt16LittleEndian(classicEndRecord[8..]), entriesOnDisk) ||
                !ClassicValueMatches(BinaryPrimitives.ReadUInt16LittleEndian(classicEndRecord[10..]), totalEntries) ||
                !ClassicValueMatches(BinaryPrimitives.ReadUInt32LittleEndian(classicEndRecord[12..]), centralDirectorySize) ||
                !ClassicValueMatches(BinaryPrimitives.ReadUInt32LittleEndian(classicEndRecord[16..]), centralDirectoryRelativeOffset))
            {
                continue;
            }

            var recordOffset = searchOffset + index;
            var archiveOffset = recordOffset - (long)zip64EndRecordRelativeOffset;
            if (archiveOffset <= 0 ||
                !TryAdd(archiveOffset, (long)centralDirectoryRelativeOffset, out var centralDirectoryOffset) ||
                !TryAdd(centralDirectoryOffset, (long)centralDirectorySize, out var centralDirectoryEnd) ||
                centralDirectoryEnd != recordOffset)
            {
                continue;
            }

            directory = new ZipDirectoryInfo(archiveOffset, centralDirectoryOffset, (long)centralDirectorySize);
            return true;
        }

        return false;
    }

    private static bool TryReadLocalHeaderOffset(
        FileStream stream,
        ZipDirectoryInfo directory,
        ReadOnlySpan<byte> centralDirectoryHeader,
        out long localHeaderOffset)
    {
        var classicOffset = BinaryPrimitives.ReadUInt32LittleEndian(centralDirectoryHeader[42..]);
        if (classicOffset != uint.MaxValue)
        {
            localHeaderOffset = classicOffset;
            return true;
        }

        localHeaderOffset = 0;
        var fileNameLength = BinaryPrimitives.ReadUInt16LittleEndian(centralDirectoryHeader[28..]);
        var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(centralDirectoryHeader[30..]);
        if (extraLength == 0 ||
            !TryAdd(ZipCentralDirectoryHeaderLength, fileNameLength, out var extraOffset) ||
            !TryAdd(extraOffset, extraLength, out var entryLength) ||
            entryLength > directory.CentralDirectorySize ||
            !TryAdd(directory.CentralDirectoryOffset, extraOffset, out var extraAbsoluteOffset))
        {
            return false;
        }

        var extra = new byte[extraLength];
        if (!TryReadExactly(stream, extraAbsoluteOffset, extra)) return false;
        for (var index = 0; index + 4 <= extra.Length;)
        {
            var headerId = BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(index));
            var dataLength = BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(index + 2));
            index += 4;
            if (index + dataLength > extra.Length) return false;
            if (headerId != 0x0001)
            {
                index += dataLength;
                continue;
            }

            var data = extra.AsSpan(index, dataLength);
            var valueOffset = 0;
            if (BinaryPrimitives.ReadUInt32LittleEndian(centralDirectoryHeader[24..]) == uint.MaxValue) valueOffset += 8;
            if (BinaryPrimitives.ReadUInt32LittleEndian(centralDirectoryHeader[20..]) == uint.MaxValue) valueOffset += 8;
            if (valueOffset + 8 > data.Length) return false;
            var zip64Offset = BinaryPrimitives.ReadUInt64LittleEndian(data[valueOffset..]);
            if (zip64Offset > long.MaxValue) return false;
            localHeaderOffset = (long)zip64Offset;
            return true;
        }

        return false;
    }

    private static bool TryReadExactly(FileStream stream, long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset > stream.Length - buffer.Length) return false;
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return true;
    }

    private static bool TryAdd(long left, long right, out long result)
    {
        try
        {
            result = checked(left + right);
            return true;
        }
        catch (OverflowException)
        {
            result = 0;
            return false;
        }
    }

    private static bool ClassicValueMatches(ushort classicValue, ulong zip64Value) =>
        classicValue == ushort.MaxValue || classicValue == zip64Value;

    private static bool ClassicValueMatches(uint classicValue, ulong zip64Value) =>
        classicValue == uint.MaxValue || classicValue == zip64Value;

    private readonly record struct ZipDirectoryInfo(long ArchiveOffset, long CentralDirectoryOffset, long CentralDirectorySize);
}
