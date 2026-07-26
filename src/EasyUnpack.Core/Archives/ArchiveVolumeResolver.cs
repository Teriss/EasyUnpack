using System.Text.RegularExpressions;

namespace EasyUnpack.Core.Archives;

public static partial class ArchiveVolumeResolver
{
    public static ArchiveVolumeSet Resolve(ArchiveCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var directory = Path.GetDirectoryName(candidate.Path) ?? throw new InvalidOperationException("Archive source directory is unavailable.");
        var fileName = Path.GetFileName(candidate.Path);

        if (candidate.Format == ArchiveFormat.SevenZip && SevenZipVolumeName().Match(fileName) is { Success: true } sevenZip)
        {
            var prefix = sevenZip.Groups["prefix"].Value;
            var parts = Directory.EnumerateFiles(directory, prefix + ".*", SearchOption.TopDirectoryOnly)
                .Select(path => (Path: path, Match: SevenZipVolumeName().Match(Path.GetFileName(path))))
                .Where(item => item.Match.Success && string.Equals(item.Match.Groups["prefix"].Value, prefix, StringComparison.OrdinalIgnoreCase))
                .Select(item => (item.Path, Index: int.Parse(item.Match.Groups["index"].Value)))
                .OrderBy(item => item.Index)
                .ToArray();
            return CreateNumberedSet(parts, 1);
        }

        if (candidate.Format == ArchiveFormat.SevenZip && DisguisedSevenZipVolumeName().Match(fileName) is { Success: true } disguisedSevenZip)
        {
            var prefix = disguisedSevenZip.Groups["prefix"].Value;
            var disguise = disguisedSevenZip.Groups["disguise"].Value;
            var parts = Directory.EnumerateFiles(directory, prefix + ".*" + disguise, SearchOption.TopDirectoryOnly)
                .Select(path => (Path: path, Match: DisguisedSevenZipVolumeName().Match(Path.GetFileName(path))))
                .Where(item => item.Match.Success &&
                    string.Equals(item.Match.Groups["prefix"].Value, prefix, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.Match.Groups["disguise"].Value, disguise, StringComparison.OrdinalIgnoreCase))
                .Select(item => (item.Path, Index: int.Parse(item.Match.Groups["index"].Value)))
                .OrderBy(item => item.Index)
                .ToArray();
            var set = CreateNumberedSet(parts, 1);
            return set with
            {
                CanonicalNames = parts.ToDictionary(
                    item => item.Path,
                    item => $"{prefix}.{item.Index:D3}",
                    StringComparer.OrdinalIgnoreCase),
            };
        }

        if (candidate.Format == ArchiveFormat.SevenZip && GenericSevenZipVolumeName().Match(fileName) is { Success: true } genericSevenZip)
        {
            var prefix = genericSevenZip.Groups["prefix"].Value;
            var parts = Directory.EnumerateFiles(directory, prefix + ".*", SearchOption.TopDirectoryOnly)
                .Select(path => (Path: path, Match: GenericSevenZipVolumeName().Match(Path.GetFileName(path))))
                .Where(item => item.Match.Success && string.Equals(item.Match.Groups["prefix"].Value, prefix, StringComparison.OrdinalIgnoreCase))
                .Select(item => (item.Path, Index: int.Parse(item.Match.Groups["index"].Value)))
                .OrderBy(item => item.Index)
                .ToArray();
            var set = CreateNumberedSet(parts, 1);
            return set with
            {
                CanonicalNames = parts.ToDictionary(
                    item => item.Path,
                    item => $"{prefix}.7z.{item.Index:D3}",
                    StringComparer.OrdinalIgnoreCase),
            };
        }

        if (candidate.Format == ArchiveFormat.SevenZip && DisguisedGenericSevenZipVolumeName().Match(fileName) is { Success: true } disguisedGenericSevenZip)
        {
            var prefix = disguisedGenericSevenZip.Groups["prefix"].Value;
            var disguise = disguisedGenericSevenZip.Groups["disguise"].Value;
            var parts = Directory.EnumerateFiles(directory, prefix + ".*" + disguise, SearchOption.TopDirectoryOnly)
                .Select(path => (Path: path, Match: DisguisedGenericSevenZipVolumeName().Match(Path.GetFileName(path))))
                .Where(item => item.Match.Success &&
                    string.Equals(item.Match.Groups["prefix"].Value, prefix, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.Match.Groups["disguise"].Value, disguise, StringComparison.OrdinalIgnoreCase))
                .Select(item => (item.Path, Index: int.Parse(item.Match.Groups["index"].Value)))
                .OrderBy(item => item.Index)
                .ToArray();
            var set = CreateNumberedSet(parts, 1);
            return set with
            {
                CanonicalNames = parts.ToDictionary(
                    item => item.Path,
                    item => $"{prefix}.7z.{item.Index:D3}",
                    StringComparer.OrdinalIgnoreCase),
            };
        }

        if (PartVolumeName().Match(fileName) is { Success: true } part)
        {
            var prefix = part.Groups["prefix"].Value;
            var suffix = part.Groups["suffix"].Value;
            var parts = Directory.EnumerateFiles(directory, prefix + ".part*" + suffix, SearchOption.TopDirectoryOnly)
                .Select(path => (Path: path, Match: PartVolumeName().Match(Path.GetFileName(path))))
                .Where(item => item.Match.Success && string.Equals(item.Match.Groups["prefix"].Value, prefix, StringComparison.OrdinalIgnoreCase) && string.Equals(item.Match.Groups["suffix"].Value, suffix, StringComparison.OrdinalIgnoreCase))
                .Select(item => (item.Path, Index: int.Parse(item.Match.Groups["index"].Value)))
                .OrderBy(item => item.Index)
                .ToArray();
            return CreateNumberedSet(parts, 1);
        }

        if (candidate.Format is ArchiveFormat.Rar or ArchiveFormat.Rar5 && RarVolumeName().Match(fileName) is { Success: true } rar)
        {
            var basePath = Path.Combine(directory, rar.Groups["prefix"].Value + ".rar");
            var parts = Directory.EnumerateFiles(directory, rar.Groups["prefix"].Value + ".r*", SearchOption.TopDirectoryOnly)
                .Select(path => (Path: path, Match: RarVolumeName().Match(Path.GetFileName(path))))
                .Where(item => item.Match.Success && string.Equals(item.Match.Groups["prefix"].Value, rar.Groups["prefix"].Value, StringComparison.OrdinalIgnoreCase))
                .Select(item => (item.Path, Index: int.Parse(item.Match.Groups["index"].Value)))
                .OrderBy(item => item.Index)
                .ToArray();
            if (!File.Exists(basePath)) return new ArchiveVolumeSet(candidate.Path, [candidate.Path], false, "The first RAR volume (.rar) is missing.");

            var numbered = CreateNumberedSet(parts, 0);
            return numbered with { PrimaryPath = basePath, SourcePaths = [basePath, .. numbered.SourcePaths] };
        }

        if (candidate.Format == ArchiveFormat.Zip && ZipVolumeName().Match(fileName) is { Success: true } zip)
        {
            var basePath = Path.Combine(directory, zip.Groups["prefix"].Value + ".zip");
            var parts = Directory.EnumerateFiles(directory, zip.Groups["prefix"].Value + ".z*", SearchOption.TopDirectoryOnly)
                .Select(path => (Path: path, Match: ZipVolumeName().Match(Path.GetFileName(path))))
                .Where(item => item.Match.Success && string.Equals(item.Match.Groups["prefix"].Value, zip.Groups["prefix"].Value, StringComparison.OrdinalIgnoreCase))
                .Select(item => (item.Path, Index: int.Parse(item.Match.Groups["index"].Value)))
                .OrderBy(item => item.Index)
                .ToArray();
            if (!File.Exists(basePath)) return new ArchiveVolumeSet(candidate.Path, [candidate.Path], false, "The final ZIP volume (.zip) is missing.");

            var numbered = CreateNumberedSet(parts, 1);
            return numbered with { PrimaryPath = basePath, SourcePaths = [.. numbered.SourcePaths, basePath] };
        }

        return new ArchiveVolumeSet(candidate.Path, [candidate.Path], true);
    }

    private static ArchiveVolumeSet CreateNumberedSet((string Path, int Index)[] parts, int firstIndex)
    {
        if (parts.Length == 0) throw new InvalidOperationException("The selected archive volume could not be resolved.");
        var expected = firstIndex;
        foreach (var part in parts)
        {
            if (part.Index != expected)
            {
                return new ArchiveVolumeSet(parts[0].Path, parts.Select(part => part.Path).ToArray(), false, $"Archive volume {expected:D3} is missing.");
            }
            expected++;
        }

        return new ArchiveVolumeSet(parts[0].Path, parts.Select(part => part.Path).ToArray(), true);
    }

    [GeneratedRegex("^(?<prefix>.+\\.7z)\\.(?<index>\\d{3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SevenZipVolumeName();

    [GeneratedRegex("^(?<prefix>.+\\.7z)\\.(?<index>\\d{3})(?<disguise>\\.[^.]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DisguisedSevenZipVolumeName();

    [GeneratedRegex("^(?<prefix>.+)\\.(?<index>\\d{3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GenericSevenZipVolumeName();

    [GeneratedRegex("^(?<prefix>.+)\\.(?<index>\\d{3})(?<disguise>\\.[^.]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DisguisedGenericSevenZipVolumeName();

    [GeneratedRegex("^(?<prefix>.+)\\.part(?<index>\\d+)(?<suffix>\\.[^.]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartVolumeName();

    [GeneratedRegex("^(?<prefix>.+)\\.r(?<index>\\d{2,3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RarVolumeName();

    [GeneratedRegex("^(?<prefix>.+)\\.z(?<index>\\d{2,3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ZipVolumeName();
}
