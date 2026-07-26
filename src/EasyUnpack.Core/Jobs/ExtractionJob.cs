namespace EasyUnpack.Core.Jobs;

public sealed record ExtractionJob(Guid Id, IReadOnlyList<string> InputPaths)
{
    public ExtractionJobStatus Status { get; internal set; } = ExtractionJobStatus.Queued;
    public string? ErrorMessage { get; internal set; }
}
