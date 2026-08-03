using EasyUnpack.Core.Naming;
using EasyUnpack.Core.Engines;

namespace EasyUnpack.Core.Archives;

public sealed record ArchiveCandidate(
    string Path,
    ArchiveFormat Format,
    bool WasDirectlySelected,
    long ArchiveOffset = 0,
    long ArchiveLength = 0,
    ArchiveEngineKind? RecognitionEngineKind = null)
{
    public string LogicalName => ArchiveNamingPolicy.GetLogicalName(Path, Format);
}
