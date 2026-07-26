using EasyUnpack.Core.Archives;

namespace EasyUnpack.Core.Tests;

public sealed class ArchiveCandidateDiscoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"EasyUnpackTests-{Guid.NewGuid():N}");

    public ArchiveCandidateDiscoveryTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Discover_finds_disguised_archives_in_selected_folder()
    {
        var disguisedArchive = Path.Combine(_directory, "作品.jpg");
        File.WriteAllBytes(disguisedArchive, Convert.FromHexString("526172211A0700"));
        File.WriteAllBytes(Path.Combine(_directory, "image.jpg"), "not an archive"u8.ToArray());

        var candidates = ArchiveCandidateDiscovery.Discover([_directory]);

        var candidate = Assert.Single(candidates);
        Assert.Equal(disguisedArchive, candidate.Path);
        Assert.Equal("作品", candidate.LogicalName);
        Assert.False(candidate.WasDirectlySelected);
    }

    [Fact]
    public void Discover_skips_zip_containers_unless_directly_selected()
    {
        var document = Path.Combine(_directory, "document.docx");
        File.WriteAllBytes(document, Convert.FromHexString("504B0304"));

        Assert.Empty(ArchiveCandidateDiscovery.Discover([_directory]));
        Assert.Single(ArchiveCandidateDiscovery.Discover([document]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
