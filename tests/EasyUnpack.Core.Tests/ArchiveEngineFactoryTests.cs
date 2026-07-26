using EasyUnpack.Core.Engines;

namespace EasyUnpack.Core.Tests;

public sealed class ArchiveEngineFactoryTests
{
    [Fact]
    public void CreatePreferred_uses_winrar_when_it_is_the_only_supported_engine()
    {
        var descriptor = new ArchiveEngineDescriptor(ArchiveEngineKind.WinRar, @"C:\\Tools\\WinRAR.exe", "test");

        var engine = ArchiveEngineFactory.CreatePreferred([descriptor]);

        Assert.IsType<WinRarEngine>(engine);
    }

    [Fact]
    public void CreatePreferred_honors_a_supported_preference()
    {
        var sevenZip = new ArchiveEngineDescriptor(ArchiveEngineKind.SevenZip, @"C:\\Tools\\7z.exe", "test");
        var winRar = new ArchiveEngineDescriptor(ArchiveEngineKind.WinRar, @"C:\\Tools\\WinRAR.exe", "test");

        var engine = ArchiveEngineFactory.CreatePreferred([sevenZip, winRar], ArchiveEngineKind.WinRar);

        Assert.IsType<WinRarEngine>(engine);
    }

    [Fact]
    public void CreatePreferred_uses_bandizip_when_it_is_the_only_supported_engine()
    {
        var descriptor = new ArchiveEngineDescriptor(ArchiveEngineKind.Bandizip, @"C:\\Tools\\bz.exe", "test");

        var engine = ArchiveEngineFactory.CreatePreferred([descriptor]);

        Assert.IsType<BandizipEngine>(engine);
    }

    [Fact]
    public void IsSupported_distinguishes_detected_tools_without_an_adapter()
    {
        Assert.True(ArchiveEngineFactory.IsSupported(ArchiveEngineKind.Bandizip));
        Assert.False(ArchiveEngineFactory.IsSupported(ArchiveEngineKind.PeaZip));
    }
}
