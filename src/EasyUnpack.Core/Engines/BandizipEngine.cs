namespace EasyUnpack.Core.Engines;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>Bandizip's documented bz.exe command-line adapter.</summary>
public sealed class BandizipEngine : IPasswordArchiveEngine
{
    private static readonly TimeSpan RecognitionTimeout = TimeSpan.FromSeconds(30);

    public BandizipEngine(ArchiveEngineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Kind != ArchiveEngineKind.Bandizip)
        {
            throw new ArgumentException("Bandizip adapter requires a Bandizip descriptor.", nameof(descriptor));
        }

        Descriptor = descriptor;
    }

    public ArchiveEngineDescriptor Descriptor { get; }

    public Task<ArchiveRecognitionResult> RecognizeAsync(string archivePath, CancellationToken cancellationToken = default) =>
        RecognizeWithTimeoutAsync(["l", archivePath], cancellationToken);

    public async Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, CancellationToken cancellationToken = default) =>
        ArchiveEngineResultClassifier.Classify(await TestAsync(archivePath, cancellationToken).ConfigureAwait(false), validation: true);

    public Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default) =>
        RunAsync(["l", archivePath], cancellationToken);

    public Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default) =>
        RunAsync(["t", archivePath], cancellationToken);

    public Task<EngineExecutionResult> TestWithPasswordAsync(string archivePath, string password, CancellationToken cancellationToken = default) =>
        RunAsync(["t", $"-p:{password}", archivePath], cancellationToken);

    public Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default) =>
        RunAsync(["x", $"-o:{destinationDirectory}", archivePath], cancellationToken);

    public Task<EngineExecutionResult> ExtractWithPasswordAsync(string archivePath, string destinationDirectory, string password, CancellationToken cancellationToken = default) =>
        RunAsync(["x", $"-o:{destinationDirectory}", $"-p:{password}", archivePath], cancellationToken);

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
            var recognition = ArchiveEngineResultClassifier.Classify(result, validation: false);
            var total = TryParseListedUncompressedBytes(result.StandardOutput);
            return recognition with { TotalUncompressedBytes = total };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ArchiveRecognitionResult(ArchiveRecognitionStatus.UnsupportedOrCorrupt);
        }
    }

    internal static long? TryParseListedUncompressedBytes(string output)
    {
        // bz.exe's table starts each file line with the uncompressed Size column.
        long total = 0;
        var entries = 0;
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Regex.Match(line, @"^(?<size>[0-9][0-9,]*)\s+[0-9][0-9,]*\s+");
            if (!match.Success || !long.TryParse(match.Groups["size"].Value.Replace(",", string.Empty), NumberStyles.None, CultureInfo.InvariantCulture, out var size)) continue;
            try { total = checked(total + size); entries++; } catch (OverflowException) { return null; }
        }
        return entries == 0 ? null : total;
    }
}
