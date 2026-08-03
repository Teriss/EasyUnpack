using EasyUnpack.Core.Archives;
using EasyUnpack.Core.Engines;
using EasyUnpack.Core.Extraction;

namespace EasyUnpack.Core.Tests;

public sealed class NestedArchiveExtractionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"EasyUnpackNestedTests-{Guid.NewGuid():N}");

    public NestedArchiveExtractionTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Extract_recursively_replaces_nested_archives_and_recycles_them_with_the_outer_source()
    {
        var archivePath = Path.Combine(_directory, "work.rar");
        await File.WriteAllBytesAsync(archivePath, Convert.FromHexString("526172211A0700"));
        var recycler = new RecordingRecycler();
        var service = new ArchiveExtractionService(new NestedEngine(), recycler);

        var result = await service.ExtractAsync(new ArchiveCandidate(archivePath, ArchiveFormat.Rar, true));

        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "content.txt")));
        Assert.False(Directory.Exists(Path.Combine(result.OutputDirectory, "inner")));
        Assert.Equal(2, recycler.Paths.Count);
        Assert.Equal(archivePath, recycler.Paths[0]);
        Assert.True(Directory.Exists(recycler.Paths[1]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class NestedEngine : IArchiveEngine
    {
        public ArchiveEngineDescriptor Descriptor { get; } = new(ArchiveEngineKind.SevenZip, "fake.exe", "test");
        public Task<ArchiveRecognitionResult> RecognizeAsync(string archivePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, ArchiveFormat.Zip));
        public Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, ArchiveFormat.Zip));
        public Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Success());
        public Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Success());

        public async Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            if (Path.GetFileName(archivePath) == "work.rar")
            {
                await File.WriteAllBytesAsync(Path.Combine(destinationDirectory, "inner.jpg"), Convert.FromHexString("504B0304"), cancellationToken);
            }
            else
            {
                var contentDirectory = Directory.CreateDirectory(Path.Combine(destinationDirectory, "RI10003"));
                await File.WriteAllTextAsync(Path.Combine(contentDirectory.FullName, "content.txt"), "content", cancellationToken);
            }

            return Success();
        }

        private static EngineExecutionResult Success() => new(true, 0, string.Empty, string.Empty);
    }

    private sealed class RecordingRecycler : IArchiveSourceRecycler
    {
        public IReadOnlyList<string> Paths { get; private set; } = [];
        public Task RecycleAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        {
            Paths = paths;
            return Task.CompletedTask;
        }
    }
}
