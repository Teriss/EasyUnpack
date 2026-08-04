namespace EasyUnpack.Core.Engines;

public interface IPasswordArchiveEngine : IArchiveEngine
{
    Task<EngineExecutionResult> TestWithPasswordAsync(string archivePath, string password, CancellationToken cancellationToken = default);

    Task<EngineExecutionResult> TestWithPasswordAsync(string archivePath, string password, IProgress<ArchiveEngineProgress>? progress, CancellationToken cancellationToken = default) =>
        TestWithPasswordAsync(archivePath, password, cancellationToken);

    Task<EngineExecutionResult> ExtractWithPasswordAsync(string archivePath, string destinationDirectory, string password, CancellationToken cancellationToken = default);

    Task<EngineExecutionResult> ExtractWithPasswordAsync(string archivePath, string destinationDirectory, string password, IProgress<ArchiveEngineProgress>? progress, CancellationToken cancellationToken = default) =>
        ExtractWithPasswordAsync(archivePath, destinationDirectory, password, cancellationToken);
}
