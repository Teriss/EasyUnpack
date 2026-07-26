using EasyUnpack.Core.Archives;
using EasyUnpack.Core.Naming;

namespace EasyUnpack.Core.Tests;

public sealed class ArchiveNamingPolicyTests
{
    [Theory]
    [InlineData(@"C:\\downloads\\作品名.jpg", ArchiveFormat.Rar, "作品名")]
    [InlineData(@"C:\\downloads\\作品名.part01.rar", ArchiveFormat.Rar, "作品名")]
    [InlineData(@"C:\\downloads\\作品名.7z.001", ArchiveFormat.SevenZip, "作品名")]
    [InlineData(@"C:\\downloads\\archive", ArchiveFormat.SevenZip, "archive")]
    public void GetLogicalName_removes_disguise_and_volume_suffix(string path, ArchiveFormat format, string expected)
    {
        Assert.Equal(expected, ArchiveNamingPolicy.GetLogicalName(path, format));
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("RI10003")]
    [InlineData("2f2a0d77-2319-4b87-b7cc-7e92b44f8f3d")]
    [InlineData("0123456789abcdef")]
    public void IsGenericContainerDirectory_accepts_configured_generic_patterns(string name)
    {
        Assert.True(ArchiveNamingPolicy.IsGenericContainerDirectory(name));
    }

    [Theory]
    [InlineData("作品名")]
    [InlineData("My Album")]
    [InlineData("R2D2")]
    public void IsGenericContainerDirectory_preserves_meaningful_names(string name)
    {
        Assert.False(ArchiveNamingPolicy.IsGenericContainerDirectory(name));
    }
}
