using System.IO.Compression;
using EasyUnpack.Core.Archives;
using EasyUnpack.Core.Engines;

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

    [Fact]
    public void Discover_carries_the_offset_of_an_appended_zip()
    {
        var path = Path.Combine(_directory, "video.mp4");
        var prefix = "media"u8.ToArray();
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("content.txt").Open());
            writer.Write("content");
        }
        var zip = buffer.ToArray();
        File.WriteAllBytes(path, [.. prefix, .. zip, .. "trailer"u8]);

        var candidate = Assert.Single(ArchiveCandidateDiscovery.Discover([path]));

        Assert.Equal(ArchiveFormat.Zip, candidate.Format);
        Assert.Equal(prefix.Length, candidate.ArchiveOffset);
        Assert.Equal(zip.Length, candidate.ArchiveLength);
    }

    [Fact]
    public async Task DiscoverAsync_uses_engines_only_for_directly_selected_unknown_files()
    {
        var direct = Path.Combine(_directory, "unknown.mp4");
        await File.WriteAllTextAsync(direct, "engine-owned format");
        var engine = new RecognitionEngine(ArchiveRecognitionStatus.Recognized, ArchiveFormat.EngineDetected);

        var directCandidates = await ArchiveCandidateDiscovery.DiscoverAsync([direct], [engine]);

        var candidate = Assert.Single(directCandidates);
        Assert.Equal(ArchiveFormat.EngineDetected, candidate.Format);
        Assert.Equal(engine.Descriptor.Kind, candidate.RecognitionEngineKind);
        Assert.Equal(1, engine.RecognitionCalls);

        engine.Reset();
        Assert.Empty(await ArchiveCandidateDiscovery.DiscoverAsync([_directory], [engine]));
        Assert.Equal(0, engine.RecognitionCalls);
    }

    [Fact]
    public async Task DiscoverAsync_falls_back_to_the_next_engine_in_order()
    {
        var path = Path.Combine(_directory, "unknown.data");
        await File.WriteAllTextAsync(path, "engine-owned format");
        var first = new RecognitionEngine(ArchiveRecognitionStatus.NotArchive, ArchiveFormat.Unknown, ArchiveEngineKind.SevenZip);
        var second = new RecognitionEngine(ArchiveRecognitionStatus.PasswordRequired, ArchiveFormat.Rar, ArchiveEngineKind.WinRar);

        var candidate = Assert.Single(await ArchiveCandidateDiscovery.DiscoverAsync([path], [first, second]));

        Assert.Equal(1, first.RecognitionCalls);
        Assert.Equal(1, second.RecognitionCalls);
        Assert.Equal(ArchiveFormat.Rar, candidate.Format);
        Assert.Equal(ArchiveEngineKind.WinRar, candidate.RecognitionEngineKind);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class RecognitionEngine(
        ArchiveRecognitionStatus status,
        ArchiveFormat format,
        ArchiveEngineKind kind = ArchiveEngineKind.SevenZip) : IArchiveEngine
    {
        public int RecognitionCalls { get; private set; }
        public ArchiveEngineDescriptor Descriptor { get; } = new(kind, "fake.exe", "test");

        public void Reset() => RecognitionCalls = 0;

        public Task<ArchiveRecognitionResult> RecognizeAsync(string archivePath, CancellationToken cancellationToken = default)
        {
            RecognitionCalls++;
            return Task.FromResult(new ArchiveRecognitionResult(status, format));
        }

        public Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ArchiveRecognitionResult(status, format));
        public Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
