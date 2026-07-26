namespace EasyUnpack.Core.Engines;

public interface IPasswordArchiveEngine : IArchiveEngine
{
    Task<EngineExecutionResult> TestWithPasswordAsync(string archivePath, string password, CancellationToken cancellationToken = default);

    Task<EngineExecutionResult> ExtractWithPasswordAsync(string archivePath, string destinationDirectory, string password, CancellationToken cancellationToken = default);
}
