using System.Diagnostics;

namespace EasyUnpack.Core.Engines;

/// <summary>Bandizip's documented bz.exe command-line adapter.</summary>
public sealed class BandizipEngine : IPasswordArchiveEngine
{
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
        var startInfo = new ProcessStartInfo
        {
            FileName = Descriptor.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new EngineExecutionResult(process.ExitCode == 0, process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
    }
}
