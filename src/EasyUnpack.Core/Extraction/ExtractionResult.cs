namespace EasyUnpack.Core.Extraction;

public sealed record ExtractionResult(string OutputDirectory, bool SourceRecycled, string? Warning);
