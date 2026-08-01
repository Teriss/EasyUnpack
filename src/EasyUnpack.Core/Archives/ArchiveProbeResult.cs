namespace EasyUnpack.Core.Archives;

public sealed record ArchiveProbeResult(
    string Path,
    ArchiveFormat Format,
    bool HasKnownSignature,
    long ArchiveOffset = 0,
    long ArchiveLength = 0)
{
    public bool IsArchive => Format != ArchiveFormat.Unknown;
}
