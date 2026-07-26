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
}
