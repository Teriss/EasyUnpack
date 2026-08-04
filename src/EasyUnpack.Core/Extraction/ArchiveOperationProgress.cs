using EasyUnpack.Core.Engines;

namespace EasyUnpack.Core.Extraction;

public enum ArchiveOperationKind
{
    Recognize,
    PrepareInput,
    Validate,
    Password,
    Extract,
    ScanNested,
    Normalize,
    Publish,
}

public enum ArchiveOperationState
{
    Pending,
    Running,
    WaitingForPassword,
    Completed,
    Failed,
    Canceled,
}

public enum ArchiveProgressPrecision
{
    Exact,
    Estimated,
    Indeterminate,
}

public sealed record ArchiveOperationUpdate(
    Guid ArchiveId,
    Guid? ParentArchiveId,
    Guid OperationId,
    ArchiveOperationKind Kind,
    ArchiveOperationState State,
    string ArchiveName,
    string ArchivePath,
    string? EngineName,
    TimeSpan Elapsed,
    long BytesWritten,
    long? TotalBytes,
    int FileCount,
    double? Percent,
    ArchiveProgressPrecision Precision);

public sealed record ArchivePasswordRequest(
    Guid ArchiveId,
    Guid? ParentArchiveId,
    string ArchiveName,
    string ArchivePath,
    string EngineName,
    int AttemptedPasswordCount);
