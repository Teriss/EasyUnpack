using System.Diagnostics;

namespace EasyUnpack.Core.Engines;

public static class ArchiveEngineDiscovery
{
    private static readonly EngineDefinition[] Definitions =
    [
        new(ArchiveEngineKind.SevenZip, "EASYUNPACK_7ZIP", ["7z.exe", "7zz.exe"], [@"C:\\Program Files\\7-Zip\\7z.exe", @"C:\\Program Files\\7-Zip\\7zz.exe"]),
        new(ArchiveEngineKind.NanaZip, "EASYUNPACK_NANAZIP", ["NanaZipC.exe"], [@"C:\\Program Files\\NanaZip\\NanaZipC.exe"]),
        new(ArchiveEngineKind.WinRar, "EASYUNPACK_WINRAR", ["WinRAR.exe", "UnRAR.exe"], [@"C:\\Program Files\\WinRAR\\WinRAR.exe", @"C:\\Program Files\\WinRAR\\UnRAR.exe"]),
        new(ArchiveEngineKind.Bandizip, "EASYUNPACK_BANDIZIP", ["bz.exe", "Bandizip.exe"], [@"C:\\Program Files\\Bandizip\\bz.exe", @"C:\\Program Files\\Bandizip\\Bandizip.exe"]),
        new(ArchiveEngineKind.PeaZip, "EASYUNPACK_PEAZIP", ["peazip.exe", "pea.exe"], [@"C:\\Program Files\\PeaZip\\peazip.exe", @"C:\\Program Files\\PeaZip\\pea.exe"]),
        new(ArchiveEngineKind.WinZip, "EASYUNPACK_WINZIP", ["wzunzip.exe"], [@"C:\\Program Files\\WinZip\\wzunzip.exe"]),
        new(ArchiveEngineKind.HaoZip, "EASYUNPACK_HAOZIP", ["HaoZipC.exe"], [@"C:\\Program Files\\HaoZip\\HaoZipC.exe"]),
        new(ArchiveEngineKind.Zip360, "EASYUNPACK_360ZIP", ["360zip.exe"], [@"C:\\Program Files\\360\\360zip\\360zip.exe"]),
    ];

    public static IReadOnlyList<ArchiveEngineDescriptor> FindAvailable(IReadOnlyDictionary<ArchiveEngineKind, string>? configuredPaths = null)
    {
        var result = new List<ArchiveEngineDescriptor>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in Definitions)
        {
            foreach (var candidate in FindCandidates(definition, configuredPaths))
            {
                if (!File.Exists(candidate.Path) || !seen.Add(candidate.Path)) continue;
                result.Add(new ArchiveEngineDescriptor(definition.Kind, candidate.Path, candidate.Source, GetVersion(candidate.Path)));
                break;
            }
        }

        AddPeaZipBundledSevenZip(result, seen);

        return result;
    }

    public static ArchiveEngineDescriptor? CreateManualDescriptor(ArchiveEngineKind kind, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        return new ArchiveEngineDescriptor(kind, Path.GetFullPath(path), "Manual configuration", GetVersion(path));
    }

    private static IEnumerable<(string Path, string Source)> FindCandidates(EngineDefinition definition, IReadOnlyDictionary<ArchiveEngineKind, string>? configuredPaths)
    {
        if (configuredPaths is not null && configuredPaths.TryGetValue(definition.Kind, out var manualPath) && !string.IsNullOrWhiteSpace(manualPath))
        {
            yield return (manualPath, "Manual configuration");
        }

        var configured = Environment.GetEnvironmentVariable(definition.EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured)) yield return (configured, $"Environment variable {definition.EnvironmentVariable}");

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var executable in definition.ExecutableNames) yield return (Path.Combine(directory.Trim(), executable), "PATH");
        }

        foreach (var path in definition.DefaultPaths) yield return (path, "Default installation directory");
    }

    private static Version? GetVersion(string path)
    {
        var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
        return Version.TryParse(version, out var parsed) ? parsed : null;
    }

    private static void AddPeaZipBundledSevenZip(ICollection<ArchiveEngineDescriptor> result, ISet<string> seen)
    {
        foreach (var peazip in result.Where(descriptor => descriptor.Kind == ArchiveEngineKind.PeaZip).ToArray())
        {
            var installationDirectory = Path.GetDirectoryName(peazip.ExecutablePath);
            if (string.IsNullOrEmpty(installationDirectory)) continue;

            foreach (var executable in new[] { "7z.exe", "7zz.exe" })
            {
                var backendPath = Path.Combine(installationDirectory, "res", "bin", "7z", executable);
                if (!File.Exists(backendPath) || !seen.Add(backendPath)) continue;
                result.Add(new ArchiveEngineDescriptor(ArchiveEngineKind.SevenZip, backendPath, "PeaZip bundled 7-Zip backend", GetVersion(backendPath)));
                break;
            }
        }
    }

    private sealed record EngineDefinition(ArchiveEngineKind Kind, string EnvironmentVariable, string[] ExecutableNames, string[] DefaultPaths);
}
