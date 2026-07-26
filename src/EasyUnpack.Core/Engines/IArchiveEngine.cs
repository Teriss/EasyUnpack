namespace EasyUnpack.Core.Engines;

public interface IArchiveEngine
{
    ArchiveEngineDescriptor Descriptor { get; }

    Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default);

    Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default);

    Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default);
}
