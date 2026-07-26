namespace EasyUnpack.Core.Engines;

public sealed record ArchiveEngineDescriptor(
    ArchiveEngineKind Kind,
    string ExecutablePath,
    string DetectionSource,
    Version? Version = null)
{
    public string DisplayName => Kind switch
    {
        ArchiveEngineKind.SevenZip => "7-Zip",
        ArchiveEngineKind.NanaZip => "NanaZip",
        ArchiveEngineKind.WinRar => "WinRAR",
        ArchiveEngineKind.Bandizip => "Bandizip",
        ArchiveEngineKind.PeaZip => "PeaZip",
        ArchiveEngineKind.WinZip => "WinZip",
        ArchiveEngineKind.HaoZip => "HaoZip",
        ArchiveEngineKind.Zip360 => "360压缩",
        _ => Kind.ToString(),
    };
}
