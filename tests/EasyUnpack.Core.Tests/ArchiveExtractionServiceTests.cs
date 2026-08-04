using EasyUnpack.Core.Archives;
using EasyUnpack.Core.Engines;
using EasyUnpack.Core.Extraction;
using System.Collections.Concurrent;

namespace EasyUnpack.Core.Tests;

public sealed class ArchiveExtractionServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"EasyUnpackExtractionTests-{Guid.NewGuid():N}");

    public ArchiveExtractionServiceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Extract_reports_written_file_count_and_bytes_while_engine_is_running()
    {
        var archivePath = Path.Combine(_directory, "progress.rar");
        await File.WriteAllBytesAsync(archivePath, Convert.FromHexString("526172211A0700"));
        var reports = new List<ExtractionProgress>();
        var engine = new FakeEngine(async (destination, cancellationToken) =>
        {
            await File.WriteAllTextAsync(Path.Combine(destination, "progress.txt"), "visible progress", cancellationToken);
            await Task.Delay(700, cancellationToken);
        });

        await new ArchiveExtractionService(engine, new RecordingRecycler()).ExtractAsync(
            new ArchiveCandidate(archivePath, ArchiveFormat.Rar, true),
            progress: new InlineProgress<ExtractionProgress>(reports.Add));

        Assert.Contains(reports, report => report.FileCount >= 1 && report.BytesWritten > 0);
        Assert.All(reports, report => Assert.True(report.Elapsed >= TimeSpan.Zero));
    }

    [Fact]
    public async Task Extract_publishes_a_generic_wrapper_under_the_archive_name_then_recycles_source()
    {
        var archivePath = Path.Combine(_directory, "作品.jpg");
        await File.WriteAllBytesAsync(archivePath, Convert.FromHexString("526172211A0700"));
        var candidate = new ArchiveCandidate(archivePath, ArchiveFormat.Rar, true);
        var recycler = new RecordingRecycler();
        var service = new ArchiveExtractionService(new FakeEngine(), recycler);

        var result = await service.ExtractAsync(candidate);

        Assert.True(result.SourceRecycled);
        Assert.Equal(Path.Combine(_directory, "作品"), result.OutputDirectory);
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "file.txt")));
        Assert.Equal([archivePath], recycler.Paths);
    }

    [Fact]
    public async Task Extract_uses_a_numbered_target_when_the_output_name_exists()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "作品"));
        var archivePath = Path.Combine(_directory, "作品.rar");
        await File.WriteAllBytesAsync(archivePath, Convert.FromHexString("526172211A0700"));
        var service = new ArchiveExtractionService(new FakeEngine(), new RecordingRecycler());

        var result = await service.ExtractAsync(new ArchiveCandidate(archivePath, ArchiveFormat.Rar, true));

        Assert.Equal(Path.Combine(_directory, "作品 (2)"), result.OutputDirectory);
    }

    [Fact]
    public async Task Extract_returns_the_published_output_when_source_recycling_fails()
    {
        var archivePath = Path.Combine(_directory, "保留源文件.rar");
        await File.WriteAllBytesAsync(archivePath, Convert.FromHexString("526172211A0700"));
        var service = new ArchiveExtractionService(new FakeEngine(), new FailingRecycler());

        var result = await service.ExtractAsync(new ArchiveCandidate(archivePath, ArchiveFormat.Rar, true));

        Assert.False(result.SourceRecycled);
        Assert.Equal(Path.Combine(_directory, "保留源文件"), result.OutputDirectory);
        Assert.True(Directory.Exists(result.OutputDirectory));
        Assert.True(File.Exists(archivePath));
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public async Task Extract_reports_output_activity_while_engine_is_running()
    {
        var archivePath = Path.Combine(_directory, "progress.rar");
        await File.WriteAllBytesAsync(archivePath, Convert.FromHexString("526172211A0700"));
        var engine = new FakeEngine(async (destination, cancellationToken) =>
        {
            await Task.Delay(700, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(destination, "file.txt"), "content", cancellationToken);
        });
        var reports = new ConcurrentQueue<ExtractionProgress>();
        var progress = new Progress<ExtractionProgress>(reports.Enqueue);

        await new ArchiveExtractionService(engine, new RecordingRecycler()).ExtractAsync(
            new ArchiveCandidate(archivePath, ArchiveFormat.Rar, true), progress: progress);

        Assert.NotEmpty(reports);
        Assert.Contains(reports, report => report.FileCount >= 1 && report.BytesWritten > 0);
        Assert.Contains(reports, report => report.Elapsed >= TimeSpan.Zero);
    }

    [Fact]
    public async Task Extract_materializes_only_the_embedded_archive_range_for_the_engine()
    {
        var archivePath = Path.Combine(_directory, "video.mp4");
        var payload = "embedded archive"u8.ToArray();
        await File.WriteAllBytesAsync(archivePath, [.. "media prefix"u8, .. payload, .. "media trailer"u8]);
        byte[]? engineInput = null;
        var engine = new FakeEngine(inspectArchive: path => engineInput = File.ReadAllBytes(path));
        var candidate = new ArchiveCandidate(archivePath, ArchiveFormat.Zip, true, "media prefix"u8.Length, payload.Length);

        await new ArchiveExtractionService(engine, new RecordingRecycler()).ExtractAsync(candidate);

        Assert.Equal(payload, engineInput);
    }

    [Fact]
    public async Task Extract_reuses_one_embedded_staging_path_for_recognition_validation_and_extraction()
    {
        var archivePath = Path.Combine(_directory, "video-reuse.mp4");
        var payload = "embedded archive"u8.ToArray();
        await File.WriteAllBytesAsync(archivePath, [.. "prefix"u8, .. payload, .. "trailer"u8]);
        var engine = new PathRecordingEngine();

        await new ArchiveExtractionService(engine, new RecordingRecycler()).ExtractAsync(
            new ArchiveCandidate(archivePath, ArchiveFormat.Zip, true, "prefix"u8.Length, payload.Length));

        Assert.Equal(3, engine.Paths.Count);
        Assert.Single(engine.Paths.Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(payload, engine.BytesSeen);
    }

    [Fact]
    public async Task Extract_falls_back_when_the_first_engine_cannot_validate()
    {
        var archivePath = Path.Combine(_directory, "fallback.rar");
        await File.WriteAllBytesAsync(archivePath, Convert.FromHexString("526172211A0700"));
        var first = new StatusEngine(ArchiveEngineKind.SevenZip, ArchiveRecognitionStatus.UnsupportedOrCorrupt);
        var second = new StatusEngine(ArchiveEngineKind.WinRar, ArchiveRecognitionStatus.Recognized);

        var result = await new ArchiveExtractionService([first, second], new RecordingRecycler())
            .ExtractAsync(new ArchiveCandidate(archivePath, ArchiveFormat.Rar, true));

        Assert.Equal(0, first.ExtractionCalls);
        Assert.Equal(1, second.ExtractionCalls);
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "file.txt")));
    }

    [Fact]
    public async Task Extract_preserves_source_when_no_engine_can_validate()
    {
        var archivePath = Path.Combine(_directory, "corrupt.rar");
        await File.WriteAllBytesAsync(archivePath, Convert.FromHexString("526172211A0700"));
        var recycler = new RecordingRecycler();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ArchiveExtractionService(new StatusEngine(ArchiveEngineKind.SevenZip, ArchiveRecognitionStatus.UnsupportedOrCorrupt), recycler)
                .ExtractAsync(new ArchiveCandidate(archivePath, ArchiveFormat.Rar, true)));

        Assert.True(File.Exists(archivePath));
        Assert.Empty(recycler.Paths);
        Assert.False(Directory.Exists(Path.Combine(_directory, "corrupt")));
    }

    [Fact]
    public async Task Extract_collapses_an_arbitrary_deep_single_directory_chain()
    {
        var archivePath = Path.Combine(_directory, "深层目录.rar");
        await File.WriteAllBytesAsync(archivePath, Convert.FromHexString("526172211A0700"));
        var engine = new FakeEngine(async (destination, cancellationToken) =>
        {
            var deepest = Directory.CreateDirectory(Path.Combine(destination, "重复", "重复", "实际内容"));
            await File.WriteAllTextAsync(Path.Combine(deepest.FullName, "video.mp4"), "content", cancellationToken);
        });

        var result = await new ArchiveExtractionService(engine, new RecordingRecycler())
            .ExtractAsync(new ArchiveCandidate(archivePath, ArchiveFormat.Rar, true));

        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "video.mp4")));
        Assert.False(Directory.Exists(Path.Combine(result.OutputDirectory, "重复")));
    }

    [Fact]
    public async Task Extract_recursively_collapses_only_redundant_branch_directories()
    {
        var archivePath = Path.Combine(_directory, "目录结构.rar");
        await File.WriteAllBytesAsync(archivePath, Convert.FromHexString("526172211A0700"));
        var engine = new FakeEngine(async (destination, cancellationToken) =>
        {
            await File.WriteAllTextAsync(Path.Combine(destination, "说明.txt"), "root", cancellationToken);
            var pictureWrapper = Directory.CreateDirectory(Path.Combine(destination, "图片", "无效包装"));
            await File.WriteAllTextAsync(Path.Combine(pictureWrapper.FullName, "cover.jpg"), "image", cancellationToken);
            var collectionA = Directory.CreateDirectory(Path.Combine(destination, "合集", "A"));
            var collectionB = Directory.CreateDirectory(Path.Combine(destination, "合集", "B"));
            await File.WriteAllTextAsync(Path.Combine(collectionA.FullName, "a.txt"), "a", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(collectionB.FullName, "b.txt"), "b", cancellationToken);
        });

        var result = await new ArchiveExtractionService(engine, new RecordingRecycler())
            .ExtractAsync(new ArchiveCandidate(archivePath, ArchiveFormat.Rar, true));

        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "说明.txt")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "图片", "cover.jpg")));
        Assert.False(Directory.Exists(Path.Combine(result.OutputDirectory, "图片", "无效包装")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "合集", "A", "a.txt")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "合集", "B", "b.txt")));
    }

    [Fact]
    public async Task Extract_keeps_a_meaningful_branch_that_contains_both_a_file_and_a_subdirectory()
    {
        var archivePath = Path.Combine(_directory, "混合内容.rar");
        await File.WriteAllBytesAsync(archivePath, Convert.FromHexString("526172211A0700"));
        var engine = new FakeEngine(async (destination, cancellationToken) =>
        {
            await File.WriteAllTextAsync(Path.Combine(destination, "top-level.txt"), "root", cancellationToken);
            var bundle = Directory.CreateDirectory(Path.Combine(destination, "Bundle"));
            await File.WriteAllTextAsync(Path.Combine(bundle.FullName, "readme.txt"), "readme", cancellationToken);
            var deep = Directory.CreateDirectory(Path.Combine(bundle.FullName, "Media", "Wrapper"));
            await File.WriteAllTextAsync(Path.Combine(deep.FullName, "movie.mp4"), "content", cancellationToken);
        });

        var result = await new ArchiveExtractionService(engine, new RecordingRecycler())
            .ExtractAsync(new ArchiveCandidate(archivePath, ArchiveFormat.Rar, true));

        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "top-level.txt")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "Bundle", "readme.txt")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "Bundle", "Media", "movie.mp4")));
        Assert.False(Directory.Exists(Path.Combine(result.OutputDirectory, "Bundle", "Media", "Wrapper")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeEngine : IArchiveEngine
    {
        private readonly Func<string, CancellationToken, Task>? _writeOutput;
        private readonly Action<string>? _inspectArchive;

        public FakeEngine(Func<string, CancellationToken, Task>? writeOutput = null, Action<string>? inspectArchive = null)
        {
            _writeOutput = writeOutput;
            _inspectArchive = inspectArchive;
        }

        public ArchiveEngineDescriptor Descriptor { get; } = new(ArchiveEngineKind.SevenZip, "fake.exe", "test");

        public Task<ArchiveRecognitionResult> RecognizeAsync(string archivePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, ArchiveFormat.Rar));

        public Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, CancellationToken cancellationToken = default)
        {
            _inspectArchive?.Invoke(archivePath);
            return Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, ArchiveFormat.Rar));
        }

        public Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Success());
        public Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Success());
        }

        public async Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            if (_writeOutput is not null)
            {
                await _writeOutput(destinationDirectory, cancellationToken);
                return Success();
            }

            var wrapped = Directory.CreateDirectory(Path.Combine(destinationDirectory, "RI10003"));
            await File.WriteAllTextAsync(Path.Combine(wrapped.FullName, "file.txt"), "content", cancellationToken);
            return Success();
        }

        private static EngineExecutionResult Success() => new(true, 0, string.Empty, string.Empty);
    }

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    private sealed class PathRecordingEngine : IArchiveEngine
    {
        public List<string> Paths { get; } = [];
        public byte[]? BytesSeen { get; private set; }
        public ArchiveEngineDescriptor Descriptor { get; } = new(ArchiveEngineKind.SevenZip, "fake.exe", "test");

        public Task<ArchiveRecognitionResult> RecognizeAsync(string archivePath, CancellationToken cancellationToken = default)
        {
            Paths.Add(archivePath);
            BytesSeen = File.ReadAllBytes(archivePath);
            return Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, ArchiveFormat.Zip));
        }

        public Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, CancellationToken cancellationToken = default)
        {
            Paths.Add(archivePath);
            return Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, ArchiveFormat.Zip));
        }

        public Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Success());
        public Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Success());
        public async Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            Paths.Add(archivePath);
            await File.WriteAllTextAsync(Path.Combine(destinationDirectory, "file.txt"), "content", cancellationToken);
            return Success();
        }

        private static EngineExecutionResult Success() => new(true, 0, string.Empty, string.Empty);
    }

    private sealed class StatusEngine(ArchiveEngineKind kind, ArchiveRecognitionStatus validationStatus) : IArchiveEngine
    {
        public int ExtractionCalls { get; private set; }
        public ArchiveEngineDescriptor Descriptor { get; } = new(kind, "fake.exe", "test");
        public Task<ArchiveRecognitionResult> RecognizeAsync(string archivePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, ArchiveFormat.Rar));
        public Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ArchiveRecognitionResult(validationStatus, ArchiveFormat.Rar));
        public Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Success());
        public Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Success());
        public async Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            ExtractionCalls++;
            await File.WriteAllTextAsync(Path.Combine(destinationDirectory, "file.txt"), "content", cancellationToken);
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

    private sealed class FailingRecycler : IArchiveSourceRecycler
    {
        public Task RecycleAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) =>
            throw new IOException("Recycle test failure.");
    }
}
