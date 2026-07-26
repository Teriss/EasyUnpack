namespace EasyUnpack.Core.Jobs;

public enum ExtractionJobStatus
{
    Queued,
    Scanning,
    WaitingForPassword,
    Extracting,
    ProcessingNestedArchives,
    Publishing,
    Succeeded,
    Failed,
    Cancelled,
}
