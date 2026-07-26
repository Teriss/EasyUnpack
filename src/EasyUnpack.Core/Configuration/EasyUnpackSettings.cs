using EasyUnpack.Core.Engines;

namespace EasyUnpack.Core.Configuration;

public sealed record EasyUnpackSettings
{
    public Dictionary<ArchiveEngineKind, string> EnginePaths { get; init; } = [];
    public ArchiveEngineKind? PreferredEngine { get; init; }
}
