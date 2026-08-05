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
    public async Task Password_vault_mismatches_share_one_waiting_operation_without_failures()
    {
        var path = await CreateArchiveAsync("protected.zip", "504B0304");
        var engine = new ProtectedEngine("right");
        var updates = new List<ArchiveOperationUpdate>();
        using var cancellation = new CancellationTokenSource();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = new ArchiveExtractionService(engine, new RecordingRecycler()).ExtractAsync(
            new ArchiveCandidate(path, ArchiveFormat.Zip, true),
            passwords: ["wrong", "still-wrong"],
            cancellationToken: cancellation.Token,
            operationProgress: new InlineProgress<ArchiveOperationUpdate>(update =>
            {
                updates.Add(update);
                if (update.Kind == ArchiveOperationKind.Password && update.State == ArchiveOperationState.WaitingForPassword) waiting.TrySetResult();
            }),
            passwordProvider: async (_, token) =>
            {
                await Task.WhenAny(waiting.Task, Task.Delay(Timeout.InfiniteTimeSpan, token));
                token.ThrowIfCancellationRequested();
                return null;
            });

        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        var passwordUpdates = updates.Where(update => update.Kind == ArchiveOperationKind.Password).ToArray();
        Assert.NotEmpty(passwordUpdates);
        Assert.Single(passwordUpdates.Select(update => update.OperationId).Distinct());
        Assert.DoesNotContain(passwordUpdates, update => update.State == ArchiveOperationState.Failed);
        Assert.Contains(passwordUpdates, update => update.State == ArchiveOperationState.WaitingForPassword);
    }

    [Fact]
    public async Task Wrong_interactive_password_retries_the_same_operation()
    {
        var path = await CreateArchiveAsync("retry.zip", "504B0304");
        var engine = new ProtectedEngine("right");
        var updates = new List<ArchiveOperationUpdate>();
        var attempts = 0;
        await new ArchiveExtractionService(engine, new RecordingRecycler()).ExtractAsync(
            new ArchiveCandidate(path, ArchiveFormat.Zip, true),
            operationProgress: new InlineProgress<ArchiveOperationUpdate>(updates.Add),
            passwordProvider: (_, _) => Task.FromResult<string?>(++attempts == 1 ? "wrong" : "right"));

        var passwordUpdates = updates.Where(update => update.Kind == ArchiveOperationKind.Password).ToArray();
        Assert.Single(passwordUpdates.Select(update => update.OperationId).Distinct());
        Assert.DoesNotContain(passwordUpdates, update => update.State == ArchiveOperationState.Failed);
        Assert.Equal(2, passwordUpdates.Count(update => update.State == ArchiveOperationState.WaitingForPassword));
        Assert.Equal(ArchiveOperationState.Completed, passwordUpdates[^1].State);
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
    public async Task Exact_progress_does_not_downgrade_when_directory_monitor_reports_estimates()
    {
        var path = await CreateArchiveAsync("progress.zip", "504B0304");
        var updates = new List<ArchiveOperationUpdate>();
        await new ArchiveExtractionService(new ProgressEngine(), new RecordingRecycler()).ExtractAsync(
            new ArchiveCandidate(path, ArchiveFormat.Zip, true),
            operationProgress: new InlineProgress<ArchiveOperationUpdate>(updates.Add));

        var extracts = updates.Where(update => update.Kind == ArchiveOperationKind.Extract).ToArray();
        var firstExact = Array.FindIndex(extracts, update => update.Precision == ArchiveProgressPrecision.Exact);
        Assert.True(firstExact >= 0);
        Assert.All(extracts.Skip(firstExact), update => Assert.Equal(ArchiveProgressPrecision.Exact, update.Precision));
        var percents = extracts.Skip(firstExact).Where(update => update.Percent is not null).Select(update => update.Percent!.Value).ToArray();
        Assert.Equal(percents.OrderBy(value => value), percents);
        Assert.Equal(100, extracts[^1].Percent);
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

    private sealed class ProgressEngine : IArchiveEngine
    {
        public ArchiveEngineDescriptor Descriptor { get; } = new(ArchiveEngineKind.SevenZip, "fake.exe", "progress");
        public Task<ArchiveRecognitionResult> RecognizeAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, ArchiveFormat.Zip));
        public Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, ArchiveFormat.Zip, 1000));
        public Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Success());
        public Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Success());
        public Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default) => ExtractAsync(archivePath, destinationDirectory, null, cancellationToken);
        public async Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, IProgress<ArchiveEngineProgress>? progress, CancellationToken cancellationToken = default)
        {
            progress?.Report(new ArchiveEngineProgress(10));
            progress?.Report(new ArchiveEngineProgress(40));
            await File.WriteAllBytesAsync(Path.Combine(destinationDirectory, "content.bin"), new byte[1024], cancellationToken);
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
