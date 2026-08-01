using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using EasyUnpack.Core.Archives;

namespace EasyUnpack.Core.Tests;

public sealed class ArchiveSignatureProbeTests
{
    [Theory]
    [InlineData("377ABC AF271C", ArchiveFormat.SevenZip)]
    [InlineData("526172211A0700", ArchiveFormat.Rar)]
    [InlineData("526172211A070100", ArchiveFormat.Rar5)]
    [InlineData("504B0304", ArchiveFormat.Zip)]
    [InlineData("1F8B08", ArchiveFormat.GZip)]
    [InlineData("425A68", ArchiveFormat.BZip2)]
    [InlineData("FD377A585A00", ArchiveFormat.Xz)]
    [InlineData("28B52FFD", ArchiveFormat.Zstandard)]
    [InlineData("4D53434600000000", ArchiveFormat.Cab)]
    public void Detect_recognizes_known_archive_signatures(string hex, ArchiveFormat expected)
    {
        Assert.Equal(expected, ArchiveSignatureProbe.Detect(Convert.FromHexString(hex.Replace(" ", string.Empty))));
    }

    [Fact]
    public void Detect_returns_unknown_for_non_archive_data()
    {
        Assert.Equal(ArchiveFormat.Unknown, ArchiveSignatureProbe.Detect("not an archive"u8));
    }

    [Fact]
    public void Detect_recognizes_tar_header()
    {
        var bytes = new byte[512];
        "ustar"u8.CopyTo(bytes.AsSpan(257));

        Assert.Equal(ArchiveFormat.Tar, ArchiveSignatureProbe.Detect(bytes));
    }

    [Fact]
    public void Probe_recognizes_a_zip_appended_to_non_archive_data()
    {
        var prefix = "ordinary media prefix"u8.ToArray();
        var path = Path.GetTempFileName();
        try
        {
            var zip = CreateZipBytes();
            File.WriteAllBytes(path, [.. prefix, .. zip, .. "media trailer"u8]);

            var result = ArchiveSignatureProbe.Probe(path);

            Assert.Equal(ArchiveFormat.Zip, result.Format);
            Assert.True(result.HasKnownSignature);
            Assert.Equal(prefix.Length, result.ArchiveOffset);
            Assert.Equal(zip.Length, result.ArchiveLength);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Probe_rejects_an_unstructured_zip_end_marker()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [.. "ordinary media prefix"u8, 0x50, 0x4B, 0x05, 0x06, .. new byte[18]]);

            var result = ArchiveSignatureProbe.Probe(path);

            Assert.Equal(ArchiveFormat.Unknown, result.Format);
            Assert.Equal(0, result.ArchiveOffset);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Probe_recognizes_a_zip64_payload_appended_to_non_archive_data()
    {
        var prefix = "ordinary media prefix"u8.ToArray();
        var path = Path.GetTempFileName();
        try
        {
            var zip = CreateZip64Bytes(useZip64LocalHeaderOffset: true);
            File.WriteAllBytes(path, [.. prefix, .. zip, .. "media trailer"u8]);

            var result = ArchiveSignatureProbe.Probe(path);

            Assert.Equal(ArchiveFormat.Zip, result.Format);
            Assert.True(result.HasKnownSignature);
            Assert.Equal(prefix.Length, result.ArchiveOffset);
            Assert.Equal(zip.Length, result.ArchiveLength);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Probe_rejects_a_zip64_payload_with_an_invalid_locator_offset()
    {
        var prefix = "ordinary media prefix"u8.ToArray();
        var path = Path.GetTempFileName();
        try
        {
            var zip = CreateZip64Bytes();
            var locatorOffset = zip.Length - 42;
            BinaryPrimitives.WriteUInt64LittleEndian(zip.AsSpan(locatorOffset + 8), ulong.MaxValue);
            File.WriteAllBytes(path, [.. prefix, .. zip]);

            var result = ArchiveSignatureProbe.Probe(path);

            Assert.Equal(ArchiveFormat.Unknown, result.Format);
            Assert.False(result.HasKnownSignature);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] CreateZipBytes()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("content.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("content");
        }
        return buffer.ToArray();
    }

    private static byte[] CreateZip64Bytes(bool useZip64LocalHeaderOffset = false)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true);
        var fileName = "content.txt"u8.ToArray();

        writer.Write(0x04034B50u);
        writer.Write((ushort)45);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((ushort)fileName.Length);
        writer.Write((ushort)0);
        writer.Write(fileName);

        var centralDirectoryOffset = buffer.Position;
        writer.Write(0x02014B50u);
        writer.Write((ushort)45);
        writer.Write((ushort)45);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((ushort)fileName.Length);
        writer.Write((ushort)(useZip64LocalHeaderOffset ? 12 : 0));
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(0u);
        writer.Write(useZip64LocalHeaderOffset ? uint.MaxValue : 0u);
        writer.Write(fileName);
        if (useZip64LocalHeaderOffset)
        {
            writer.Write((ushort)0x0001);
            writer.Write((ushort)8);
            writer.Write(0ul);
        }
        var centralDirectorySize = buffer.Position - centralDirectoryOffset;

        var zip64EndRecordOffset = buffer.Position;
        writer.Write(0x06064B50u);
        writer.Write(44ul);
        writer.Write((ushort)45);
        writer.Write((ushort)45);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(1ul);
        writer.Write(1ul);
        writer.Write((ulong)centralDirectorySize);
        writer.Write((ulong)centralDirectoryOffset);

        writer.Write(0x07064B50u);
        writer.Write(0u);
        writer.Write((ulong)zip64EndRecordOffset);
        writer.Write(1u);

        writer.Write(0x06054B50u);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((uint)centralDirectorySize);
        writer.Write(uint.MaxValue);
        writer.Write((ushort)0);

        return buffer.ToArray();
    }
}
