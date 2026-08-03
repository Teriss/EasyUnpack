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

    public Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default) =>
        RunAsync(["l", "-slt", "-bd", "--", archivePath], cancellationToken);

    public Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default) =>
        RunAsync(["t", "-bd", "--", archivePath], cancellationToken);

    public Task<EngineExecutionResult> TestWithPasswordAsync(string archivePath, string password, CancellationToken cancellationToken = default) =>
        RunAsync(["t", "-bd", $"-p{password}", "--", archivePath], cancellationToken);

    public Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        return RunAsync(["x", "-y", "-bd", $"-o{destinationDirectory}", "--", archivePath], cancellationToken);
    }

    public Task<EngineExecutionResult> ExtractWithPasswordAsync(string archivePath, string destinationDirectory, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        return RunAsync(["x", "-y", "-bd", $"-o{destinationDirectory}", $"-p{password}", "--", archivePath], cancellationToken);
    }

    private async Task<EngineExecutionResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        return await ArchiveEngineProcessRunner.RunAsync(Descriptor.ExecutablePath, arguments, cancellationToken).ConfigureAwait(false);
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
