using EasyUnpack.Core.Archives;

namespace EasyUnpack.Core.Engines;

internal static class ArchiveEngineResultClassifier
{
    private static readonly string[] PasswordMarkers =
    [
        "wrong password", "password is incorrect", "password required", "enter password",
        "encrypted archive", "encrypted file", "incorrect password",
    ];

    private static readonly string[] NotArchiveMarkers =
    [
        "cannot open the file as archive", "can not open the file as archive", "is not archive",
        "not an archive", "unknown archive format", "no archive found",
    ];

    public static ArchiveRecognitionResult Classify(EngineExecutionResult result, bool validation)
    {
        if (result.Succeeded) return new ArchiveRecognitionResult(ArchiveRecognitionStatus.Recognized, DetectFormat(result.StandardOutput));

        var output = string.Concat(result.StandardOutput, "\n", result.StandardError);
        if (ContainsAny(output, PasswordMarkers))
        {
            return new ArchiveRecognitionResult(ArchiveRecognitionStatus.PasswordRequired, DetectFormat(output));
        }

        if (ContainsAny(output, NotArchiveMarkers))
        {
            return new ArchiveRecognitionResult(ArchiveRecognitionStatus.NotArchive);
        }

        return new ArchiveRecognitionResult(ArchiveRecognitionStatus.UnsupportedOrCorrupt,
            validation ? DetectFormat(output) : ArchiveFormat.Unknown);
    }

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static ArchiveFormat DetectFormat(string output)
    {
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('=');
            if (separator < 0 || !line[..separator].Trim().Equals("Type", StringComparison.OrdinalIgnoreCase)) continue;
            return MapFormat(line[(separator + 1)..].Trim());
        }

        return ArchiveFormat.Unknown;
    }

    private static ArchiveFormat MapFormat(string type) => type.ToUpperInvariant() switch
    {
        "7Z" => ArchiveFormat.SevenZip,
        "RAR" => ArchiveFormat.Rar,
        "RAR5" => ArchiveFormat.Rar5,
        "ZIP" or "ZIPX" => ArchiveFormat.Zip,
        "TAR" => ArchiveFormat.Tar,
        "GZIP" or "GZ" => ArchiveFormat.GZip,
        "BZIP2" or "BZ2" => ArchiveFormat.BZip2,
        "XZ" => ArchiveFormat.Xz,
        "ZSTD" or "ZSTANDARD" => ArchiveFormat.Zstandard,
        "CAB" => ArchiveFormat.Cab,
        "ARJ" => ArchiveFormat.Arj,
        "LZH" or "LHA" => ArchiveFormat.Lzh,
        _ => ArchiveFormat.EngineDetected,
    };
}
