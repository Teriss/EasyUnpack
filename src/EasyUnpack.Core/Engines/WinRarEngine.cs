namespace EasyUnpack.Core.Engines;

public sealed class WinRarEngine : IPasswordArchiveEngine
{
    private static readonly TimeSpan RecognitionTimeout = TimeSpan.FromSeconds(30);

    public WinRarEngine(ArchiveEngineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Kind != ArchiveEngineKind.WinRar) throw new ArgumentException("WinRAR adapter requires a WinRAR descriptor.", nameof(descriptor));
        Descriptor = descriptor;
    }

    public ArchiveEngineDescriptor Descriptor { get; }

    public Task<ArchiveRecognitionResult> RecognizeAsync(string archivePath, CancellationToken cancellationToken = default) =>
        RecognizeWithTimeoutAsync(["l", "-idq", archivePath], cancellationToken);

    public async Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, CancellationToken cancellationToken = default) =>
        ArchiveEngineResultClassifier.Classify(await TestAsync(archivePath, cancellationToken).ConfigureAwait(false), validation: true);

    public Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default) =>
        RunAsync(["l", "-idq", archivePath], cancellationToken);

    public Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default) =>
        RunAsync(["t", "-idq", archivePath], cancellationToken);

    public Task<EngineExecutionResult> TestWithPasswordAsync(string archivePath, string password, CancellationToken cancellationToken = default) =>
        RunAsync(["t", "-idq", $"-p{password}", archivePath], cancellationToken);

    public Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default) =>
        RunAsync(["x", "-y", "-ibck", "-o+", archivePath, EnsureDirectorySuffix(destinationDirectory)], cancellationToken);

    public Task<EngineExecutionResult> ExtractWithPasswordAsync(string archivePath, string destinationDirectory, string password, CancellationToken cancellationToken = default) =>
        RunAsync(["x", "-y", "-ibck", "-o+", $"-p{password}", archivePath, EnsureDirectorySuffix(destinationDirectory)], cancellationToken);

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

    private static string EnsureDirectorySuffix(string path) => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
