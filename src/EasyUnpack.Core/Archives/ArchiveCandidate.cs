using EasyUnpack.Core.Naming;

namespace EasyUnpack.Core.Archives;

public sealed record ArchiveCandidate(string Path, ArchiveFormat Format, bool WasDirectlySelected)
{
    public string LogicalName => ArchiveNamingPolicy.GetLogicalName(Path, Format);
}
