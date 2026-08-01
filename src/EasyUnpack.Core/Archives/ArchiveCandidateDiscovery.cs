namespace EasyUnpack.Core.Archives;

public static class ArchiveCandidateDiscovery
{
    private static readonly HashSet<string> ContainerExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".xlsx", ".pptx", ".apk", ".epub", ".jar", ".appx", ".msix",
    };

    public static IReadOnlyList<ArchiveCandidate> Discover(IEnumerable<string> inputPaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        var candidates = new List<ArchiveCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var inputPath in inputPaths.Where(static path => !string.IsNullOrWhiteSpace(path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(inputPath)) TryAdd(inputPath, true, candidates, seen);
            else if (Directory.Exists(inputPath))
            {
                foreach (var file in Directory.EnumerateFiles(inputPath, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TryAdd(file, false, candidates, seen);
                }
            }
        }

        return candidates;
    }

    private static void TryAdd(string path, bool wasDirectlySelected, ICollection<ArchiveCandidate> candidates, ISet<string> seen)
    {
        try
        {
            if (!wasDirectlySelected && ContainerExtensions.Contains(Path.GetExtension(path))) return;
            var result = ArchiveSignatureProbe.Probe(path);
            if (result.IsArchive && seen.Add(Path.GetFullPath(path)))
            {
                candidates.Add(new ArchiveCandidate(path, result.Format, wasDirectlySelected, result.ArchiveOffset, result.ArchiveLength));
            }
        }
        catch (IOException)
        {
            // Downloads and files held by another process are retried by a later invocation.
        }
        catch (UnauthorizedAccessException)
        {
            // A recursive scan should not fail because one child directory cannot be read.
        }
    }
}
