namespace EasyUnpack.Core.Engines;

public interface IArchiveEngine
{
    ArchiveEngineDescriptor Descriptor { get; }

    Task<ArchiveRecognitionResult> RecognizeAsync(string archivePath, CancellationToken cancellationToken = default);

    Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, CancellationToken cancellationToken = default);

    Task<ArchiveRecognitionResult> ValidateAsync(string archivePath, IProgress<ArchiveEngineProgress>? progress, CancellationToken cancellationToken = default) =>
        ValidateAsync(archivePath, cancellationToken);

    Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default);

    Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default);

    Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default);

    Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, IProgress<ArchiveEngineProgress>? progress, CancellationToken cancellationToken = default) =>
        ExtractAsync(archivePath, destinationDirectory, cancellationToken);
}
