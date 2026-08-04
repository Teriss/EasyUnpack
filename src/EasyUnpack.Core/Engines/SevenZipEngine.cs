namespace EasyUnpack.Core.Engines;

public sealed class SevenZipEngine : IPasswordArchiveEngine
{
    private static readonly TimeSpan RecognitionTimeout = TimeSpan.FromSeconds(30);

    public SevenZipEngine(ArchiveEngineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Kind is not (ArchiveEngineKind.SevenZip or ArchiveEngineKind.NanaZip))
        {
            throw new ArgumentException("7-Zip adapter only supports 7-Zip compatible engines.", nameof(descriptor));
        }

        Descriptor = descriptor;
    }

    public ArchiveEngineDescriptor Descriptor { get; }

    public Task<ArchiveRecognitionResult> RecognizeAsync(string archivePath, CancellationToken cancellationToken = default) =>
        RecognizeWithTimeoutAsync(["l", "-slt", "-bd", "--", archivePath], cancellationToken);

    public async Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, CancellationToken cancellationToken = default) =>
        ArchiveEngineResultClassifier.Classify(await TestAsync(archivePath, cancellationToken).ConfigureAwait(false), validation: true);

    public async Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, IProgress<ArchiveEngineProgress>? progress, CancellationToken cancellationToken = default) =>
        ArchiveEngineResultClassifier.Classify(await TestAsync(archivePath, progress, cancellationToken).ConfigureAwait(false), validation: true);

    public Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default) =>
        RunAsync(["l", "-slt", "-bd", "--", archivePath], cancellationToken);

    public Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default) =>
        RunAsync(["t", "-bd", "--", archivePath], cancellationToken);

    public Task<EngineExecutionResult> TestAsync(string archivePath, IProgress<ArchiveEngineProgress>? progress, CancellationToken cancellationToken = default) =>
        RunWithProgressAsync(["t", "-bsp1", "--", archivePath], progress, cancellationToken);

    public Task<EngineExecutionResult> TestWithPasswordAsync(string archivePath, string password, CancellationToken cancellationToken = default) =>
        RunAsync(["t", "-bd", $"-p{password}", "--", archivePath], cancellationToken);

    public Task<EngineExecutionResult> TestWithPasswordAsync(string archivePath, string password, IProgress<ArchiveEngineProgress>? progress, CancellationToken cancellationToken = default) =>
        RunWithProgressAsync(["t", "-bsp1", $"-p{password}", "--", archivePath], progress, cancellationToken);

    public Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        return RunAsync(["x", "-y", "-bd", $"-o{destinationDirectory}", "--", archivePath], cancellationToken);
    }

    public Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, IProgress<ArchiveEngineProgress>? progress, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        return RunWithProgressAsync(["x", "-y", "-bsp1", $"-o{destinationDirectory}", "--", archivePath], progress, cancellationToken);
    }

    public Task<EngineExecutionResult> ExtractWithPasswordAsync(string archivePath, string destinationDirectory, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        return RunAsync(["x", "-y", "-bd", $"-o{destinationDirectory}", $"-p{password}", "--", archivePath], cancellationToken);
    }

    public Task<EngineExecutionResult> ExtractWithPasswordAsync(string archivePath, string destinationDirectory, string password, IProgress<ArchiveEngineProgress>? progress, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        return RunWithProgressAsync(["x", "-y", "-bsp1", $"-o{destinationDirectory}", $"-p{password}", "--", archivePath], progress, cancellationToken);
    }

    private async Task<EngineExecutionResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        return await ArchiveEngineProcessRunner.RunAsync(Descriptor.ExecutablePath, arguments, cancellationToken).ConfigureAwait(false);
    }

    private async Task<EngineExecutionResult> RunWithProgressAsync(IReadOnlyList<string> arguments, IProgress<ArchiveEngineProgress>? progress, CancellationToken cancellationToken)
    {
        var parser = new ProgressParser(progress);
        var result = await ArchiveEngineProcessRunner.RunAsync(Descriptor.ExecutablePath, arguments, cancellationToken, parser.Push).ConfigureAwait(false);
        parser.Complete();
        return result;
    }

    internal sealed class ProgressParser(IProgress<ArchiveEngineProgress>? progress)
    {
        private string _remainder = string.Empty;

        public void Push(string chunk)
        {
            var text = _remainder + chunk;
            var last = -1;
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] != '%') continue;
                var start = index - 1;
                while (start >= 0 && char.IsDigit(text[start])) start--;
                start++;
                if (start == index || index - start > 3) continue;
                if (int.TryParse(text.AsSpan(start, index - start), out var percent) && percent is >= 0 and <= 100)
                {
                    progress?.Report(new ArchiveEngineProgress(percent));
                    last = index;
                }
            }

            var tail = last >= 0 ? text[(last + 1)..] : text;
            _remainder = tail.Length > 8 ? tail[^8..] : tail;
        }

        public void Complete() => _remainder = string.Empty;
    }

    private async Task<ArchiveRecognitionResult> RecognizeWithTimeoutAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(RecognitionTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var result = await RunAsync(arguments, linked.Token).ConfigureAwait(false);
            return ArchiveEngineResultClassifier.Classify(result, validation: false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ArchiveRecognitionResult(ArchiveRecognitionStatus.UnsupportedOrCorrupt);
        }
    }
}
