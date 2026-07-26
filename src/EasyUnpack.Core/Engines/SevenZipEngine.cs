using System.Diagnostics;

namespace EasyUnpack.Core.Engines;

public sealed class SevenZipEngine : IPasswordArchiveEngine
{
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
        var startInfo = new ProcessStartInfo
        {
            FileName = Descriptor.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        process.StandardInput.Close();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new EngineExecutionResult(process.ExitCode == 0, process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
    }
}
