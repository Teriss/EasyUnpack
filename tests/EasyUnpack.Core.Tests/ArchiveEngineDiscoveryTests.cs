using EasyUnpack.Core.Engines;

namespace EasyUnpack.Core.Tests;

public sealed class ArchiveEngineDiscoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"EasyUnpackEngineDiscoveryTests-{Guid.NewGuid():N}");

    public ArchiveEngineDiscoveryTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void FindAvailable_uses_the_7zip_backend_bundled_with_peazip()
    {
        var peazipPath = CreateFile("peazip.exe");
        var backendPath = CreateFile(Path.Combine("res", "bin", "7z", "7z.exe"));

        var engines = ArchiveEngineDiscovery.FindAvailable(new Dictionary<ArchiveEngineKind, string>
        {
            [ArchiveEngineKind.PeaZip] = peazipPath,
        });

        var backend = Assert.Single(engines, engine => string.Equals(engine.ExecutablePath, backendPath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ArchiveEngineKind.SevenZip, backend.Kind);
        Assert.Equal("PeaZip bundled 7-Zip backend", backend.DetectionSource);
        Assert.IsType<SevenZipEngine>(ArchiveEngineFactory.CreatePreferred([backend]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private string CreateFile(string relativePath)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }
}
