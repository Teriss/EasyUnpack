using EasyUnpack.Core.Archives;

namespace EasyUnpack.Core.Tests;

public sealed class ArchiveVolumeResolverTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"EasyUnpackVolumeTests-{Guid.NewGuid():N}");

    public ArchiveVolumeResolverTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Resolve_finds_all_7zip_volumes_from_any_selected_part()
    {
        var paths = CreateFiles("work.7z.001", "work.7z.002", "work.7z.003");

        var set = ArchiveVolumeResolver.Resolve(new ArchiveCandidate(paths[1], ArchiveFormat.SevenZip, true));

        Assert.True(set.IsComplete);
        Assert.Equal(paths[0], set.PrimaryPath);
        Assert.Equal(paths, set.SourcePaths);
    }

    [Fact]
    public void Resolve_reports_missing_7zip_middle_volume()
    {
        var paths = CreateFiles("work.7z.001", "work.7z.003");

        var set = ArchiveVolumeResolver.Resolve(new ArchiveCandidate(paths[1], ArchiveFormat.SevenZip, true));

        Assert.False(set.IsComplete);
        Assert.Contains("002", set.IncompleteReason);
    }

    [Fact]
    public void Resolve_finds_rar_first_volume_from_r00()
    {
        var paths = CreateFiles("work.rar", "work.r00", "work.r01");

        var set = ArchiveVolumeResolver.Resolve(new ArchiveCandidate(paths[1], ArchiveFormat.Rar, true));

        Assert.True(set.IsComplete);
        Assert.Equal(paths[0], set.PrimaryPath);
        Assert.Equal(paths, set.SourcePaths);
    }

    [Fact]
    public void Resolve_creates_canonical_alias_names_for_disguised_7zip_volumes()
    {
        var paths = CreateFiles("work.7z.001.jpg", "work.7z.002.jpg");

        var set = ArchiveVolumeResolver.Resolve(new ArchiveCandidate(paths[0], ArchiveFormat.SevenZip, true));

        Assert.True(set.IsComplete);
        Assert.Equal("work.7z.001", set.CanonicalNames[paths[0]]);
        Assert.Equal("work.7z.002", set.CanonicalNames[paths[1]]);
    }

    [Fact]
    public void Resolve_creates_standard_7zip_names_for_volumes_without_an_archive_extension()
    {
        var paths = CreateFiles("work.001.jpg", "work.002.jpg");

        var set = ArchiveVolumeResolver.Resolve(new ArchiveCandidate(paths[0], ArchiveFormat.SevenZip, true));

        Assert.True(set.IsComplete);
        Assert.Equal("work.7z.001", set.CanonicalNames[paths[0]]);
        Assert.Equal("work.7z.002", set.CanonicalNames[paths[1]]);
    }

    private string[] CreateFiles(params string[] names)
    {
        return names.Select(name =>
        {
            var path = Path.Combine(_directory, name);
            File.WriteAllBytes(path, []);
            return path;
        }).ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
