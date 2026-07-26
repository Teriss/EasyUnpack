namespace EasyUnpack.Core.Archives;

public sealed record ArchiveVolumeSet(
    string PrimaryPath,
    IReadOnlyList<string> SourcePaths,
    bool IsComplete,
    string? IncompleteReason = null)
{
    // Only populated when renamed split volumes need canonical names for an engine.
    public IReadOnlyDictionary<string, string> CanonicalNames { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
