using EasyUnpack.Core.Archives;

namespace EasyUnpack.Core.Engines;

public enum ArchiveRecognitionStatus
{
    Recognized,
    PasswordRequired,
    NotArchive,
    UnsupportedOrCorrupt,
}

public sealed record ArchiveRecognitionResult(
    ArchiveRecognitionStatus Status,
    ArchiveFormat Format = ArchiveFormat.Unknown,
    long? TotalUncompressedBytes = null,
    int? EntryCount = null)
{
    public bool CanExtract => Status is ArchiveRecognitionStatus.Recognized or ArchiveRecognitionStatus.PasswordRequired;

    public ArchiveFormat EffectiveFormat => Format == ArchiveFormat.Unknown ? ArchiveFormat.EngineDetected : Format;
}
