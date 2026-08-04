namespace EasyUnpack.Core.Engines;

/// <summary>Adapter-owned progress information. Raw tool output never crosses this boundary.</summary>
public sealed record ArchiveEngineProgress(double? Percent = null, long? TotalUncompressedBytes = null, int? EntryCount = null);
