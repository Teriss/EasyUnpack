namespace EasyUnpack.Core.Archives;

public sealed record ArchiveProbeResult(
    string Path,
    ArchiveFormat Format,
    bool HasKnownSignature)
{
    public bool IsArchive => Format != ArchiveFormat.Unknown;
}
