using System.IO.Compression;
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
}
