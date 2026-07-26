using EasyUnpack.Core.Configuration;
using EasyUnpack.Core.Engines;

namespace EasyUnpack.Core.Tests;

public sealed class EasyUnpackSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"EasyUnpackSettingsTests-{Guid.NewGuid():N}");

    public EasyUnpackSettingsStoreTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Settings_round_trip_manual_engine_paths_and_preference()
    {
        var path = Path.Combine(_directory, "settings.json");
        var settings = new EasyUnpackSettings
        {
            EnginePaths = new Dictionary<ArchiveEngineKind, string> { [ArchiveEngineKind.SevenZip] = @"C:\\Tools\\7z.exe" },
            PreferredEngine = ArchiveEngineKind.SevenZip,
        };

        await EasyUnpackSettingsStore.SaveAsync(settings, path);
        var loaded = await EasyUnpackSettingsStore.LoadAsync(path);

        Assert.Equal(ArchiveEngineKind.SevenZip, loaded.PreferredEngine);
        Assert.Equal(@"C:\\Tools\\7z.exe", loaded.EnginePaths[ArchiveEngineKind.SevenZip]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
