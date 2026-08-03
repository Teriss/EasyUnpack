using EasyUnpack.Core.Archives;
using EasyUnpack.Core.Engines;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace EasyUnpack.Core.Extraction;

public sealed partial class ArchiveExtractionService
{
    private readonly IReadOnlyList<IArchiveEngine> _engines;
    private readonly IArchiveSourceRecycler _recycler;

    public ArchiveExtractionService(IArchiveEngine engine, IArchiveSourceRecycler recycler)
        : this([engine], recycler)
    {
    }

    public ArchiveExtractionService(IReadOnlyList<IArchiveEngine> engines, IArchiveSourceRecycler recycler)
    {
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentNullException.ThrowIfNull(recycler);
        if (engines.Count == 0) throw new ArgumentException("At least one archive engine is required.", nameof(engines));
        _engines = engines;
        _recycler = recycler;
    }

    public int MaximumNestedDepth { get; init; } = 10;

    public async Task<ExtractionResult> ExtractAsync(ArchiveCandidate candidate, IReadOnlyList<string>? passwords = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var sourceDirectory = Path.GetDirectoryName(candidate.Path) ?? throw new InvalidOperationException("Archive source directory is unavailable.");
        var volumes = ArchiveVolumeResolver.Resolve(candidate);
        if (!volumes.IsComplete) throw new InvalidDataException(volumes.IncompleteReason);
        var workingDirectory = Path.Combine(sourceDirectory, $".easyunpack-{Guid.NewGuid():N}");
        var consumedDirectory = Path.Combine(sourceDirectory, $".easyunpack-consumed-{Guid.NewGuid():N}");
        var extractionDirectory = Path.Combine(workingDirectory, "contents");
        var published = false;

        try
        {
            Directory.CreateDirectory(extractionDirectory);
            var engineArchivePath = await PrepareEngineArchivePathAsync(candidate, volumes, workingDirectory, cancellationToken).ConfigureAwait(false);
            var selection = await SelectEngineAsync(candidate, engineArchivePath, passwords, cancellationToken).ConfigureAwait(false);
            var extraction = selection.Password is null
                ? await selection.Engine.ExtractAsync(engineArchivePath, extractionDirectory, cancellationToken).ConfigureAwait(false)
                : await ((IPasswordArchiveEngine)selection.Engine).ExtractWithPasswordAsync(engineArchivePath, extractionDirectory, selection.Password, cancellationToken).ConfigureAwait(false);
            if (!extraction.Succeeded) throw new InvalidDataException($"{selection.Engine.Descriptor.DisplayName} could not extract the archive.");

            await ProcessNestedArchivesAsync(extractionDirectory, workingDirectory, consumedDirectory, passwords, cancellationToken).ConfigureAwait(false);
            NormalizeOutputTree(extractionDirectory, cancellationToken);
            var targetDirectory = ReserveTargetDirectory(sourceDirectory, candidate.LogicalName);
            Directory.Move(extractionDirectory, targetDirectory);
            published = true;

            try
            {
                var recyclePaths = volumes.SourcePaths.ToList();
                if (Directory.Exists(consumedDirectory)) recyclePaths.Add(consumedDirectory);
                await _recycler.RecycleAsync(recyclePaths, cancellationToken).ConfigureAwait(false);
                return new ExtractionResult(targetDirectory, true, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new ExtractionResult(targetDirectory, false, $"Extraction completed, but source archives were not moved to the recycle bin: {exception.Message}");
            }
        }
        finally
        {
            if (Directory.Exists(workingDirectory)) Directory.Delete(workingDirectory, recursive: true);
            if (!published && Directory.Exists(consumedDirectory)) Directory.Delete(consumedDirectory, recursive: true);
        }
    }

    private async Task ProcessNestedArchivesAsync(string rootDirectory, string workingDirectory, string consumedDirectory, IReadOnlyList<string>? passwords, CancellationToken cancellationToken)
    {
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var depth = 0; ; depth++)
        {
            var candidates = ArchiveCandidateDiscovery.Discover([rootDirectory], cancellationToken)
                .Where(candidate => processed.Add(Path.GetFullPath(candidate.Path)))
                .ToArray();
            if (candidates.Length == 0) return;
            if (depth >= MaximumNestedDepth) throw new InvalidDataException($"Nested archive depth exceeds the configured limit of {MaximumNestedDepth}.");

            foreach (var nested in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ExtractNestedArchiveAsync(nested, workingDirectory, consumedDirectory, passwords, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ExtractNestedArchiveAsync(ArchiveCandidate candidate, string workingDirectory, string consumedDirectory, IReadOnlyList<string>? passwords, CancellationToken cancellationToken)
    {
        var nestedWorkingDirectory = Path.Combine(workingDirectory, $"nested-{Guid.NewGuid():N}");
        var nestedContentsDirectory = Path.Combine(nestedWorkingDirectory, "contents");
        try
        {
            var volumes = ArchiveVolumeResolver.Resolve(candidate);
            if (!volumes.IsComplete) throw new InvalidDataException(volumes.IncompleteReason);
            Directory.CreateDirectory(nestedContentsDirectory);
            var engineArchivePath = await PrepareEngineArchivePathAsync(candidate, volumes, nestedWorkingDirectory, cancellationToken).ConfigureAwait(false);
            var selection = await SelectEngineAsync(candidate, engineArchivePath, passwords, cancellationToken).ConfigureAwait(false);
            var extraction = selection.Password is null
                ? await selection.Engine.ExtractAsync(engineArchivePath, nestedContentsDirectory, cancellationToken).ConfigureAwait(false)
                : await ((IPasswordArchiveEngine)selection.Engine).ExtractWithPasswordAsync(engineArchivePath, nestedContentsDirectory, selection.Password, cancellationToken).ConfigureAwait(false);
            if (!extraction.Succeeded) throw new InvalidDataException($"{selection.Engine.Descriptor.DisplayName} could not extract nested archive {candidate.Path}.");

            NormalizeOutputTree(nestedContentsDirectory, cancellationToken);
            var targetDirectory = ReserveTargetDirectory(Path.GetDirectoryName(candidate.Path)!, candidate.LogicalName);
            Directory.CreateDirectory(consumedDirectory);
            foreach (var sourcePath in volumes.SourcePaths)
            {
                File.Move(sourcePath, Path.Combine(consumedDirectory, $"{Guid.NewGuid():N}-{Path.GetFileName(sourcePath)}"));
            }
            Directory.Move(nestedContentsDirectory, targetDirectory);
        }
        finally
        {
            if (Directory.Exists(nestedWorkingDirectory)) Directory.Delete(nestedWorkingDirectory, recursive: true);
        }
    }

    private async Task<EngineSelection> SelectEngineAsync(
        ArchiveCandidate candidate,
        string archivePath,
        IReadOnlyList<string>? passwords,
        CancellationToken cancellationToken)
    {
        var passwordRequired = false;
        var attemptedPasswords = 0;
        foreach (var engine in OrderEngines(candidate.RecognitionEngineKind))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchiveRecognitionResult recognition;
            try
            {
                recognition = await engine.RecognizeAsync(archivePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
            {
                continue;
            }
            if (!recognition.CanExtract) continue;

            ArchiveRecognitionResult validation;
            try
            {
                validation = await engine.ValidateAsync(archivePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
            {
                continue;
            }
            if (validation.Status == ArchiveRecognitionStatus.Recognized) return new EngineSelection(engine, null);
            if (validation.Status != ArchiveRecognitionStatus.PasswordRequired || engine is not IPasswordArchiveEngine passwordEngine) continue;

            passwordRequired = true;
            foreach (var password in passwords ?? [])
            {
                if (string.IsNullOrEmpty(password)) continue;
                attemptedPasswords++;
                var result = await passwordEngine.TestWithPasswordAsync(archivePath, password, cancellationToken).ConfigureAwait(false);
                if (result.Succeeded) return new EngineSelection(engine, password);
            }
        }

        if (passwordRequired)
        {
            throw new ArchivePasswordRequiredException(candidate.Path, attemptedPasswords);
        }

        throw new InvalidDataException("No installed archive engine could validate the selected archive.");
    }

    private IEnumerable<IArchiveEngine> OrderEngines(ArchiveEngineKind? recognitionEngineKind) =>
        recognitionEngineKind is null
            ? _engines
            : _engines.OrderBy(engine => engine.Descriptor.Kind == recognitionEngineKind ? 0 : 1);

    private static async Task<string> PrepareEngineArchivePathAsync(
        ArchiveCandidate candidate,
        ArchiveVolumeSet volumes,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (candidate.ArchiveOffset > 0)
        {
            if (volumes.SourcePaths.Count != 1 || !string.Equals(volumes.PrimaryPath, candidate.Path, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("An embedded archive cannot be combined with a multi-volume archive set.");
            }

            var sourceLength = new FileInfo(candidate.Path).Length;
            if (candidate.ArchiveLength <= 0 ||
                candidate.ArchiveOffset >= sourceLength ||
                candidate.ArchiveLength > sourceLength - candidate.ArchiveOffset)
            {
                throw new InvalidDataException("The embedded archive range is outside the source file.");
            }

            var embeddedDirectory = Path.Combine(workingDirectory, "embedded-archive");
            Directory.CreateDirectory(embeddedDirectory);
            var embeddedPath = Path.Combine(embeddedDirectory, GetEmbeddedArchiveName(candidate.Format));
            await using var source = new FileStream(
                candidate.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            source.Position = candidate.ArchiveOffset;
            await using var destination = new FileStream(
                embeddedPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = GC.AllocateUninitializedArray<byte>(1024 * 1024);
            var remaining = candidate.ArchiveLength;
            while (remaining > 0)
            {
                var count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("The source file ended before the embedded archive was copied.");
                await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                remaining -= count;
            }
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            return embeddedPath;
        }

        if (volumes.CanonicalNames.Count == 0) return volumes.PrimaryPath;

        var aliasDirectory = Path.Combine(workingDirectory, "volume-aliases");
        Directory.CreateDirectory(aliasDirectory);
        foreach (var sourcePath in volumes.SourcePaths)
        {
            if (!volumes.CanonicalNames.TryGetValue(sourcePath, out var canonicalName))
            {
                throw new InvalidDataException("The archive volume alias set is incomplete.");
            }

            var aliasPath = Path.Combine(aliasDirectory, canonicalName);
            try
            {
                CreateHardLink(aliasPath, sourcePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                throw new InvalidOperationException("EasyUnpack could not create safe temporary names for the renamed archive volumes.", exception);
            }
        }

        return Path.Combine(aliasDirectory, volumes.CanonicalNames[volumes.PrimaryPath]);
    }

    private static void CreateHardLink(string linkPath, string existingPath)
    {
        if (CreateHardLinkNative(linkPath, existingPath, IntPtr.Zero)) return;
        throw new IOException("Windows could not create a temporary archive-volume hard link.", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkNative(string fileName, string existingFileName, IntPtr securityAttributes);

    private static string GetEmbeddedArchiveName(ArchiveFormat format) => format switch
    {
        ArchiveFormat.SevenZip => "payload.7z",
        ArchiveFormat.Rar or ArchiveFormat.Rar5 => "payload.rar",
        ArchiveFormat.Zip => "payload.zip",
        ArchiveFormat.Tar => "payload.tar",
        ArchiveFormat.GZip => "payload.gz",
        ArchiveFormat.BZip2 => "payload.bz2",
        ArchiveFormat.Xz => "payload.xz",
        ArchiveFormat.Zstandard => "payload.zst",
        ArchiveFormat.Cab => "payload.cab",
        ArchiveFormat.Arj => "payload.arj",
        ArchiveFormat.Lzh => "payload.lzh",
        _ => "payload.bin",
    };

    private static void NormalizeOutputTree(string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (TryGetOnlyOrdinaryChildDirectory(directory, out var onlyChild))
        {
            LiftChildContents(directory, onlyChild, cancellationToken);
        }

        foreach (var childDirectory in Directory.GetDirectories(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReparsePoint(childDirectory)) continue;
            NormalizeOutputTree(childDirectory, cancellationToken);
        }
    }

    private static bool TryGetOnlyOrdinaryChildDirectory(string directory, out string childDirectory)
    {
        var entries = Directory.GetFileSystemEntries(directory);
        if (entries.Length == 1 && Directory.Exists(entries[0]) && !IsReparsePoint(entries[0]))
        {
            childDirectory = entries[0];
            return true;
        }

        childDirectory = string.Empty;
        return false;
    }

    private static void LiftChildContents(string directory, string childDirectory, CancellationToken cancellationToken)
    {
        var parentDirectory = Path.GetDirectoryName(directory)
            ?? throw new InvalidOperationException("The extraction directory cannot be a file-system root.");
        string temporaryDirectory;
        do
        {
            temporaryDirectory = Path.Combine(parentDirectory, $".easyunpack-flatten-{Guid.NewGuid():N}");
        }
        while (Directory.Exists(temporaryDirectory) || File.Exists(temporaryDirectory));

        Directory.Move(childDirectory, temporaryDirectory);
        foreach (var entry in Directory.GetFileSystemEntries(temporaryDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(directory, Path.GetFileName(entry));
            if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
            {
                Directory.Move(entry, destination);
            }
            else
            {
                File.Move(entry, destination);
            }
        }
        Directory.Delete(temporaryDirectory);
    }

    private static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string ReserveTargetDirectory(string sourceDirectory, string logicalName)
    {
        var candidate = Path.Combine(sourceDirectory, logicalName);
        for (var index = 2; Directory.Exists(candidate) || File.Exists(candidate); ++index)
        {
            candidate = Path.Combine(sourceDirectory, $"{logicalName} ({index})");
        }

        return candidate;
    }

    private sealed record EngineSelection(IArchiveEngine Engine, string? Password);
}
