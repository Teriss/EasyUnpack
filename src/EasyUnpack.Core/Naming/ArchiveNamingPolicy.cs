using System.Text.RegularExpressions;
using EasyUnpack.Core.Archives;

namespace EasyUnpack.Core.Naming;

public static partial class ArchiveNamingPolicy
{
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z", ".rar", ".zip", ".zipx", ".tar", ".gz", ".gzip", ".bz2", ".xz", ".zst", ".cab", ".arj", ".lzh", ".lha",
    };

    public static string GetLogicalName(string archivePath, ArchiveFormat detectedFormat)
    {
        var fileName = Path.GetFileName(archivePath);
        if (string.IsNullOrWhiteSpace(fileName)) return "解压内容";

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension)) name = fileName;

        if (detectedFormat != ArchiveFormat.Unknown || ArchiveExtensions.Contains(extension))
        {
            name = StripVolumeSuffix(name);
            if (name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
        }

        return string.IsNullOrWhiteSpace(name) ? "解压内容" : name.Trim();
    }

    public static bool IsGenericContainerDirectory(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (NumericName().IsMatch(name) || UuidName().IsMatch(name) || HexName().IsMatch(name) || LetterNumberName().IsMatch(name));

    private static string StripVolumeSuffix(string name)
    {
        var stripped = PartSuffix().Replace(name, string.Empty);
        stripped = RarVolumeSuffix().Replace(stripped, string.Empty);
        return NumericVolumeSuffix().Replace(stripped, string.Empty);
    }

    [GeneratedRegex("^\\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericName();

    [GeneratedRegex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex UuidName();

    [GeneratedRegex("^[0-9a-fA-F]{16,}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexName();

    [GeneratedRegex("^[A-Za-z]{1,8}\\d{4,}$", RegexOptions.CultureInvariant)]
    private static partial Regex LetterNumberName();

    [GeneratedRegex("(?i)\\.part\\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex PartSuffix();

    [GeneratedRegex("(?i)\\.r\\d{2,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex RarVolumeSuffix();

    [GeneratedRegex("(?i)\\.\\d{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericVolumeSuffix();
}
