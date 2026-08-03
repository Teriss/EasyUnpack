namespace EasyUnpack.Core.Engines;

public interface IArchiveEngine
{
    ArchiveEngineDescriptor Descriptor { get; }

    Task<ArchiveRecognitionResult> RecognizeAsync(string archivePath, CancellationToken cancellationToken = default);

    Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, CancellationToken cancellationToken = default);

    Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default);

    Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default);

    Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default);
}
