using System.ComponentModel;
using EasyUnpack.Core.Engines;

namespace EasyUnpack.Core.Archives;

public static class ArchiveCandidateDiscovery
{
    private static readonly HashSet<string> ContainerExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".xlsx", ".pptx", ".apk", ".epub", ".jar", ".appx", ".msix",
    };

    public static IReadOnlyList<ArchiveCandidate> Discover(IEnumerable<string> inputPaths, CancellationToken cancellationToken = default)
        => DiscoverBuiltIn(inputPaths, cancellationToken);

    public static async Task<IReadOnlyList<ArchiveCandidate>> DiscoverAsync(
        IEnumerable<string> inputPaths,
        IReadOnlyList<IArchiveEngine> engines,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        ArgumentNullException.ThrowIfNull(engines);
        var candidates = new List<ArchiveCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var inputPath in inputPaths.Where(static path => !string.IsNullOrWhiteSpace(path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(inputPath)) await TryAddAsync(inputPath, true, engines, candidates, seen, cancellationToken).ConfigureAwait(false);
            else if (Directory.Exists(inputPath))
            {
                foreach (var file in Directory.EnumerateFiles(inputPath, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await TryAddAsync(file, false, engines, candidates, seen, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return candidates;
    }

    private static IReadOnlyList<ArchiveCandidate> DiscoverBuiltIn(IEnumerable<string> inputPaths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        var candidates = new List<ArchiveCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var inputPath in inputPaths.Where(static path => !string.IsNullOrWhiteSpace(path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(inputPath)) TryAddBuiltIn(inputPath, true, candidates, seen);
            else if (Directory.Exists(inputPath))
            {
                foreach (var file in Directory.EnumerateFiles(inputPath, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TryAddBuiltIn(file, false, candidates, seen);
                }
            }
        }

        return candidates;
    }

    private static void TryAddBuiltIn(string path, bool wasDirectlySelected, ICollection<ArchiveCandidate> candidates, ISet<string> seen)
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

    private static async Task TryAddAsync(
        string path,
        bool wasDirectlySelected,
        IReadOnlyList<IArchiveEngine> engines,
        ICollection<ArchiveCandidate> candidates,
        ISet<string> seen,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!wasDirectlySelected && ContainerExtensions.Contains(Path.GetExtension(path))) return;
            var result = ArchiveSignatureProbe.Probe(path);
            if (result.IsArchive)
            {
                AddCandidate(path, result.Format, wasDirectlySelected, result.ArchiveOffset, result.ArchiveLength, null, candidates, seen);
                return;
            }

            if (!wasDirectlySelected) return;
            foreach (var engine in engines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ArchiveRecognitionResult recognition;
                try
                {
                    recognition = await engine.RecognizeAsync(path, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
                {
                    continue;
                }
                if (!recognition.CanExtract) continue;
                AddCandidate(path, recognition.EffectiveFormat, true, 0, 0, engine.Descriptor.Kind, candidates, seen);
                return;
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

    private static void AddCandidate(
        string path,
        ArchiveFormat format,
        bool wasDirectlySelected,
        long archiveOffset,
        long archiveLength,
        ArchiveEngineKind? recognitionEngineKind,
        ICollection<ArchiveCandidate> candidates,
        ISet<string> seen)
    {
        if (seen.Add(Path.GetFullPath(path)))
        {
            candidates.Add(new ArchiveCandidate(path, format, wasDirectlySelected, archiveOffset, archiveLength, recognitionEngineKind));
        }
    }
}
