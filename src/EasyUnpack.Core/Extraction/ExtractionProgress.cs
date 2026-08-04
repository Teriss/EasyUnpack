namespace EasyUnpack.Core.Extraction;

public sealed record ExtractionProgress(int FileCount, long BytesWritten, TimeSpan Elapsed);
