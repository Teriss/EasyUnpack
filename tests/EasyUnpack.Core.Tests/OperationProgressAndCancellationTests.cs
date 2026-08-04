using System.Collections.Concurrent;
using EasyUnpack.Core.Archives;
using EasyUnpack.Core.Engines;
using EasyUnpack.Core.Extraction;

namespace EasyUnpack.Core.Tests;

public sealed class OperationProgressAndCancellationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"EasyUnpackOperations-{Guid.NewGuid():N}");

    public OperationProgressAndCancellationTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Password_requirement_stops_fallback_and_resumes_in_place()
    {
        var path = await CreateArchiveAsync("protected.zip", "504B0304");
        var protectedEngine = new ProtectedEngine("right");
        var fallback = new CountingEngine();
        var prompts = 0;

        var result = await new ArchiveExtractionService([protectedEngine, fallback], new RecordingRecycler()).ExtractAsync(
            new ArchiveCandidate(path, ArchiveFormat.Zip, true),
            operationProgress: new Progress<ArchiveOperationUpdate>(_ => { }),
            passwordProvider: (_, _) => { prompts++; return Task.FromResult<string?>("right"); });

        Assert.Equal(1, prompts);
        Assert.Equal(0, fallback.RecognizeCalls);
        Assert.Equal(1, protectedEngine.ExtractCalls);
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "content.txt")));
    }

    [Fact]
    public async Task Nested_archives_publish_distinct_parented_operation_progress()
    {
        var path = await CreateArchiveAsync("outer.rar", "526172211A0700");
        var updates = new ConcurrentQueue<ArchiveOperationUpdate>();
        var service = new ArchiveExtractionService(new NestedEngine(), new RecordingRecycler());

        await service.ExtractAsync(new ArchiveCandidate(path, ArchiveFormat.Rar, true), operationProgress: new Progress<ArchiveOperationUpdate>(updates.Enqueue));

        var extracts = updates.Where(update => update.Kind == ArchiveOperationKind.Extract && update.State == ArchiveOperationState.Completed).ToArray();
        Assert.Equal(2, extracts.Length);
        Assert.NotEqual(extracts[0].ArchiveId, extracts[1].ArchiveId);
        Assert.Contains(extracts, update => update.ParentArchiveId == extracts[0].ArchiveId || update.ParentArchiveId == extracts[1].ArchiveId);
        Assert.All(extracts, update => Assert.Equal(100, update.Percent));
    }

    [Fact]
    public async Task Cancellation_preserves_partial_work_and_never_recycles_source()
    {
        var path = await CreateArchiveAsync("cancel.rar", "526172211A0700");
        var recycler = new RecordingRecycler();
        using var cancellation = new CancellationTokenSource();
        var service = new ArchiveExtractionService(new BlockingEngine(), recycler);

        var task = service.ExtractAsync(new ArchiveCandidate(path, ArchiveFormat.Rar, true), cancellationToken: cancellation.Token);
        await Task.Delay(100);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        Assert.True(File.Exists(path));
        Assert.Empty(recycler.Paths);
        var incomplete = Directory.GetDirectories(_directory, "cancel - 未完成*");
        Assert.Single(incomplete);
        Assert.True(File.Exists(Path.Combine(incomplete[0], "contents", "partial.bin")));
    }

    [Fact]
    public void Seven_zip_parser_handles_carriage_return_fragmented_percentages()
    {
        var values = new List<double>();
        var parser = new SevenZipEngine.ProgressParser(new InlineProgress<ArchiveEngineProgress>(update =>
        {
            if (update.Percent is double percent) values.Add(percent);
        }));
        parser.Push(" 4");
        parser.Push("2%\r99");
        parser.Push("%\r");
        parser.Complete();

        Assert.Equal([42d, 99d], values);
    }

    [Fact]
    public void Bandizip_listing_size_parser_returns_uncompressed_total()
    {
        var total = BandizipEngine.TryParseListedUncompressedBytes("Size       CompSize  Name\r\n1,024      512       one.bin\r\n2048       1024      two.bin");
        Assert.Equal(3072, total);
    }

    private async Task<string> CreateArchiveAsync(string name, string bytes)
    {
        var path = Path.Combine(_directory, name);
        await File.WriteAllBytesAsync(path, Convert.FromHexString(bytes));
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class CountingEngine : IArchiveEngine
    {
        public int RecognizeCalls { get; private set; }
        public ArchiveEngineDescriptor Descriptor { get; } = new(ArchiveEngineKind.Bandizip, "fake.exe", "fallback");
        public Task<ArchiveRecognitionResult> RecognizeAsync(string archivePath, CancellationToken cancellationToken = default) { RecognizeCalls++; return Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, ArchiveFormat.Zip)); }
        public Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, ArchiveFormat.Zip));
        public Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Success());
        public Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Success());
        public Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default) => Task.FromResult(Success());
    }

    private sealed class ProtectedEngine(string password) : IPasswordArchiveEngine
    {
        public int ExtractCalls { get; private set; }
        public ArchiveEngineDescriptor Descriptor { get; } = new(ArchiveEngineKind.SevenZip, "fake.exe", "protected");
        public Task<ArchiveRecognitionResult> RecognizeAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, ArchiveFormat.Zip));
        public Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.PasswordRequired, ArchiveFormat.Zip));
        public Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Failure());
        public Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Failure());
        public Task<EngineExecutionResult> TestWithPasswordAsync(string archivePath, string value, CancellationToken cancellationToken = default) => Task.FromResult(value == password ? Success() : Failure());
        public Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default) => Task.FromResult(Failure());
        public async Task<EngineExecutionResult> ExtractWithPasswordAsync(string archivePath, string destinationDirectory, string value, CancellationToken cancellationToken = default) { ExtractCalls++; await File.WriteAllTextAsync(Path.Combine(destinationDirectory, "content.txt"), "content", cancellationToken); return Success(); }
    }

    private sealed class NestedEngine : IArchiveEngine
    {
        public ArchiveEngineDescriptor Descriptor { get; } = new(ArchiveEngineKind.SevenZip, "fake.exe", "nested");
        public Task<ArchiveRecognitionResult> RecognizeAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, ArchiveFormat.Zip));
        public Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, ArchiveFormat.Zip));
        public Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Success());
        public Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Success());
        public async Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            if (Path.GetFileName(archivePath) == "outer.rar") await File.WriteAllBytesAsync(Path.Combine(destinationDirectory, "inner.zip"), Convert.FromHexString("504B0304"), cancellationToken);
            else await File.WriteAllTextAsync(Path.Combine(destinationDirectory, "content.txt"), "content", cancellationToken);
            return Success();
        }
    }

    private sealed class BlockingEngine : IArchiveEngine
    {
        public ArchiveEngineDescriptor Descriptor { get; } = new(ArchiveEngineKind.SevenZip, "fake.exe", "blocking");
        public Task<ArchiveRecognitionResult> RecognizeAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, ArchiveFormat.Rar));
        public Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, ArchiveFormat.Rar));
        public Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Success());
        public Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Success());
        public async Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            await File.WriteAllTextAsync(Path.Combine(destinationDirectory, "partial.bin"), "partial", cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Success();
        }
    }

    private sealed class RecordingRecycler : IArchiveSourceRecycler
    {
        public IReadOnlyList<string> Paths { get; private set; } = [];
        public Task RecycleAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) { Paths = paths; return Task.CompletedTask; }
    }

    private static EngineExecutionResult Success() => new(true, 0, string.Empty, string.Empty);
    private static EngineExecutionResult Failure() => new(false, 2, string.Empty, string.Empty);
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
