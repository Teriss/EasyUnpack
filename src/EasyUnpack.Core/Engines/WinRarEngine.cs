using System.Diagnostics;

namespace EasyUnpack.Core.Engines;

public sealed class WinRarEngine : IPasswordArchiveEngine
{
    public WinRarEngine(ArchiveEngineDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Kind != ArchiveEngineKind.WinRar) throw new ArgumentException("WinRAR adapter requires a WinRAR descriptor.", nameof(descriptor));
        Descriptor = descriptor;
    }

    public ArchiveEngineDescriptor Descriptor { get; }

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

    private static string EnsureDirectorySuffix(string path) => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
