using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using EasyUnpack.Core.Archives;
using EasyUnpack.Core.Engines;

namespace EasyUnpack.Core.Extraction;

public sealed partial class ArchiveExtractionService
{
    private readonly IReadOnlyList<IArchiveEngine> _engines;
    private readonly IArchiveSourceRecycler _recycler;

    public ArchiveExtractionService(IArchiveEngine engine, IArchiveSourceRecycler recycler) : this([engine], recycler) { }

    public ArchiveExtractionService(IReadOnlyList<IArchiveEngine> engines, IArchiveSourceRecycler recycler)
    {
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentNullException.ThrowIfNull(recycler);
        if (engines.Count == 0) throw new ArgumentException("At least one archive engine is required.", nameof(engines));
        _engines = engines;
        _recycler = recycler;
    }

    public int MaximumNestedDepth { get; init; } = 10;

    public async Task<ExtractionResult> ExtractAsync(
        ArchiveCandidate candidate,
        IReadOnlyList<string>? passwords = null,
        CancellationToken cancellationToken = default,
        IProgress<ExtractionProgress>? progress = null,
        IProgress<ArchiveOperationUpdate>? operationProgress = null,
        Func<ArchivePasswordRequest, CancellationToken, Task<string?>>? passwordProvider = null,
        Action<string>? passwordAccepted = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var sourceDirectory = Path.GetDirectoryName(candidate.Path) ?? throw new InvalidOperationException("Archive source directory is unavailable.");
        var rootArchive = new ArchiveContext(candidate, Guid.NewGuid(), null);
        var operations = new OperationReporter(operationProgress, rootArchive);
        var volumes = ArchiveVolumeResolver.Resolve(candidate);
        if (!volumes.IsComplete) throw new InvalidDataException(volumes.IncompleteReason);

        var workingDirectory = Path.Combine(sourceDirectory, $".easyunpack-{Guid.NewGuid():N}");
        var consumedDirectory = Path.Combine(workingDirectory, "consumed");
        var contentsDirectory = Path.Combine(workingDirectory, "contents");
        var published = false;
        var canceled = false;
        try
        {
            Directory.CreateDirectory(contentsDirectory);
            await ExtractArchiveAsync(rootArchive, volumes, workingDirectory, contentsDirectory, passwords, progress, operations, passwordProvider, passwordAccepted, cancellationToken).ConfigureAwait(false);
            await ProcessNestedArchivesAsync(contentsDirectory, workingDirectory, consumedDirectory, rootArchive, passwords, progress, operations, passwordProvider, passwordAccepted, cancellationToken).ConfigureAwait(false);

            await RunSynchronousOperationAsync(operations, ArchiveOperationKind.Normalize, null, cancellationToken, () => NormalizeOutputTree(contentsDirectory, cancellationToken)).ConfigureAwait(false);
            string targetDirectory = string.Empty;
            await RunSynchronousOperationAsync(operations, ArchiveOperationKind.Publish, null, cancellationToken, () =>
            {
                targetDirectory = ReserveTargetDirectory(sourceDirectory, candidate.LogicalName);
                Directory.Move(contentsDirectory, targetDirectory);
            }).ConfigureAwait(false);
            published = true;
            var recycleConsumedDirectory = consumedDirectory;
            if (Directory.Exists(consumedDirectory))
            {
                recycleConsumedDirectory = Path.Combine(sourceDirectory, $".easyunpack-consumed-{Guid.NewGuid():N}");
                Directory.Move(consumedDirectory, recycleConsumedDirectory);
            }

            try
            {
                var recyclePaths = volumes.SourcePaths.ToList();
                if (Directory.Exists(recycleConsumedDirectory)) recyclePaths.Add(recycleConsumedDirectory);
                await _recycler.RecycleAsync(recyclePaths, cancellationToken).ConfigureAwait(false);
                return new ExtractionResult(targetDirectory, true, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new ExtractionResult(targetDirectory, false, $"Extraction completed, but source archives were not moved to the recycle bin: {exception.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            canceled = true;
            operations.ReportTerminalCancellation();
            PreserveIncompleteDirectory(workingDirectory, sourceDirectory, candidate.LogicalName);
            throw;
        }
        finally
        {
            if (!canceled && Directory.Exists(workingDirectory)) Directory.Delete(workingDirectory, recursive: true);
            if (!published && !canceled && Directory.Exists(consumedDirectory)) Directory.Delete(consumedDirectory, recursive: true);
        }
    }

    private async Task ExtractArchiveAsync(
        ArchiveContext archive,
        ArchiveVolumeSet volumes,
        string workingDirectory,
        string contentsDirectory,
        IReadOnlyList<string>? passwords,
        IProgress<ExtractionProgress>? legacyProgress,
        OperationReporter operations,
        Func<ArchivePasswordRequest, CancellationToken, Task<string?>>? passwordProvider,
        Action<string>? passwordAccepted,
        CancellationToken cancellationToken)
    {
        var needsPreparation = archive.Candidate.ArchiveOffset > 0 || volumes.CanonicalNames.Count > 0;
        string enginePath;
        if (needsPreparation)
        {
            using var prepare = operations.Start(ArchiveOperationKind.PrepareInput);
            enginePath = await PrepareEngineArchivePathAsync(archive.Candidate, volumes, workingDirectory, prepare, cancellationToken).ConfigureAwait(false);
            prepare.Complete();
        }
        else
        {
            enginePath = volumes.PrimaryPath;
        }

        var selection = await SelectEngineAsync(archive, enginePath, passwords, operations, passwordProvider, passwordAccepted, cancellationToken).ConfigureAwait(false);
        using var extract = operations.Start(ArchiveOperationKind.Extract, selection.Engine.Descriptor.DisplayName);
        var engineProgress = new Progress<ArchiveEngineProgress>(value =>
        {
            if (value.Percent is double percent)
            {
                extract.Report(bytes: 0, total: selection.TotalBytes, percent: ClampActivePercent(percent), precision: ArchiveProgressPrecision.Exact);
            }
        });

        EngineExecutionResult result;
        try
        {
            result = await RunExtractionWithProgressAsync(
                contentsDirectory,
                legacyProgress,
                extract,
                selection.TotalBytes,
                selection.Password is null
                    ? selection.Engine.ExtractAsync(enginePath, contentsDirectory, engineProgress, cancellationToken)
                    : ((IPasswordArchiveEngine)selection.Engine).ExtractWithPasswordAsync(enginePath, contentsDirectory, selection.Password, engineProgress, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { extract.Cancel(); throw; }
        catch { extract.Fail(); throw; }
        if (!result.Succeeded) throw new InvalidDataException($"{selection.Engine.Descriptor.DisplayName} could not extract the archive.");
        extract.Complete(percent: 100, precision: ArchiveProgressPrecision.Exact);
    }

    private async Task ProcessNestedArchivesAsync(
        string rootDirectory,
        string workingDirectory,
        string consumedDirectory,
        ArchiveContext rootArchive,
        IReadOnlyList<string>? passwords,
        IProgress<ExtractionProgress>? legacyProgress,
        OperationReporter rootOperations,
        Func<ArchivePasswordRequest, CancellationToken, Task<string?>>? passwordProvider,
        Action<string>? passwordAccepted,
        CancellationToken cancellationToken)
    {
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var archiveRoots = new List<(string Directory, ArchiveContext Archive)> { (rootDirectory, rootArchive) };
        for (var depth = 0; ; depth++)
        {
            using var scan = rootOperations.Start(ArchiveOperationKind.ScanNested);
            var candidates = ArchiveCandidateDiscovery.Discover([rootDirectory], cancellationToken)
                .Where(candidate => processed.Add(Path.GetFullPath(candidate.Path)))
                .ToArray();
            scan.Complete(fileCount: candidates.Length);
            if (candidates.Length == 0) return;
            if (depth >= MaximumNestedDepth) throw new InvalidDataException($"Nested archive depth exceeds the configured limit of {MaximumNestedDepth}.");

            foreach (var nested in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parent = archiveRoots
                    .Where(item => nested.Path.StartsWith(item.Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(item => item.Directory.Length)
                    .Select(item => item.Archive)
                    .FirstOrDefault() ?? rootArchive;
                var nestedArchive = new ArchiveContext(nested, Guid.NewGuid(), parent.Id);
                var nestedOperations = rootOperations.ForArchive(nestedArchive);
                var outputDirectory = await ExtractNestedArchiveAsync(nestedArchive, workingDirectory, consumedDirectory, passwords, legacyProgress, nestedOperations, passwordProvider, passwordAccepted, cancellationToken).ConfigureAwait(false);
                archiveRoots.Add((outputDirectory, nestedArchive));
            }
        }
    }

    private async Task<string> ExtractNestedArchiveAsync(
        ArchiveContext archive,
        string workingDirectory,
        string consumedDirectory,
        IReadOnlyList<string>? passwords,
        IProgress<ExtractionProgress>? legacyProgress,
        OperationReporter operations,
        Func<ArchivePasswordRequest, CancellationToken, Task<string?>>? passwordProvider,
        Action<string>? passwordAccepted,
        CancellationToken cancellationToken)
    {
        var nestedWorkingDirectory = Path.Combine(workingDirectory, $"nested-{Guid.NewGuid():N}");
        var nestedContentsDirectory = Path.Combine(nestedWorkingDirectory, "contents");
        var canceled = false;
        try
        {
            var volumes = ArchiveVolumeResolver.Resolve(archive.Candidate);
            if (!volumes.IsComplete) throw new InvalidDataException(volumes.IncompleteReason);
            Directory.CreateDirectory(nestedContentsDirectory);
            await ExtractArchiveAsync(archive, volumes, nestedWorkingDirectory, nestedContentsDirectory, passwords, legacyProgress, operations, passwordProvider, passwordAccepted, cancellationToken).ConfigureAwait(false);
            await RunSynchronousOperationAsync(operations, ArchiveOperationKind.Normalize, null, cancellationToken, () => NormalizeOutputTree(nestedContentsDirectory, cancellationToken)).ConfigureAwait(false);
            var targetDirectory = ReserveTargetDirectory(Path.GetDirectoryName(archive.Candidate.Path)!, archive.Candidate.LogicalName);
            Directory.CreateDirectory(consumedDirectory);
            foreach (var sourcePath in volumes.SourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(sourcePath, Path.Combine(consumedDirectory, $"{Guid.NewGuid():N}-{Path.GetFileName(sourcePath)}"));
            }
            Directory.Move(nestedContentsDirectory, targetDirectory);
            return targetDirectory;
        }
        catch (OperationCanceledException)
        {
            canceled = true;
            throw;
        }
        finally
        {
            if (!canceled && Directory.Exists(nestedWorkingDirectory)) Directory.Delete(nestedWorkingDirectory, recursive: true);
        }
    }

    private async Task<EngineSelection> SelectEngineAsync(
        ArchiveContext archive,
        string archivePath,
        IReadOnlyList<string>? passwords,
        OperationReporter operations,
        Func<ArchivePasswordRequest, CancellationToken, Task<string?>>? passwordProvider,
        Action<string>? passwordAccepted,
        CancellationToken cancellationToken)
    {
        foreach (var engine in OrderEngines(archive.Candidate.RecognitionEngineKind))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchiveRecognitionResult recognition;
            using (var recognize = operations.Start(ArchiveOperationKind.Recognize, engine.Descriptor.DisplayName))
            {
                try { recognition = await engine.RecognizeAsync(archivePath, cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
                {
                    recognize.Fail();
                    continue;
                }
                if (!recognition.CanExtract) { recognize.Fail(); continue; }
                recognize.Complete();
            }

            ArchiveRecognitionResult validation;
            using (var validate = operations.Start(ArchiveOperationKind.Validate, engine.Descriptor.DisplayName))
            {
                try { validation = await engine.ValidateAsync(archivePath, progress: null, cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
                {
                    validate.Fail();
                    continue;
                }
                if (validation.Status == ArchiveRecognitionStatus.Recognized)
                {
                    validate.Complete();
                    return new EngineSelection(engine, null, validation.TotalUncompressedBytes ?? recognition.TotalUncompressedBytes);
                }
                if (validation.Status != ArchiveRecognitionStatus.PasswordRequired || engine is not IPasswordArchiveEngine passwordEngine)
                {
                    validate.Fail();
                    continue;
                }
                validate.Complete();

                // PasswordRequired confirms this archive. Do not run another engine's full validation.
                // Keep all vault attempts and interactive retries on one operation row.
                using var passwordOperation = operations.Start(ArchiveOperationKind.Password, engine.Descriptor.DisplayName, ArchiveOperationState.Running);
                foreach (var password in passwords ?? [])
                {
                    if (string.IsNullOrEmpty(password)) continue;
                    if ((await passwordEngine.TestWithPasswordAsync(archivePath, password, progress: null, cancellationToken).ConfigureAwait(false)).Succeeded)
                    {
                        passwordOperation.Complete();
                        return new EngineSelection(engine, password, validation.TotalUncompressedBytes ?? recognition.TotalUncompressedBytes);
                    }
                }

                passwordOperation.Waiting();
                if (passwordProvider is null) throw new ArchivePasswordRequiredException(archive.Candidate.Path, 0);
                var previousAttemptFailed = false;
                while (true)
                {
                    var password = await passwordProvider(new ArchivePasswordRequest(archive.Id, archive.ParentId, archive.Candidate.LogicalName, archive.Candidate.Path, engine.Descriptor.DisplayName, 0, previousAttemptFailed), cancellationToken).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(password)) throw new OperationCanceledException(cancellationToken);
                    passwordOperation.Running();
                    if ((await passwordEngine.TestWithPasswordAsync(archivePath, password, progress: null, cancellationToken).ConfigureAwait(false)).Succeeded)
                    {
                        passwordOperation.Complete();
                        passwordAccepted?.Invoke(password);
                        return new EngineSelection(engine, password, validation.TotalUncompressedBytes ?? recognition.TotalUncompressedBytes);
                    }
                    previousAttemptFailed = true;
                    passwordOperation.Waiting();
                }
            }
        }

        throw new InvalidDataException("No installed archive engine could validate the selected archive.");
    }

    private async Task<EngineExecutionResult> RunExtractionWithProgressAsync(
        string extractionDirectory,
        IProgress<ExtractionProgress>? legacyProgress,
        OperationScope operation,
        long? totalBytes,
        Task<EngineExecutionResult> extractionTask,
        CancellationToken cancellationToken)
    {
        using var monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var started = Stopwatch.GetTimestamp();
        var monitorTask = MonitorExtractionDirectoryAsync(extractionDirectory, legacyProgress, operation, totalBytes, started, monitorCancellation.Token);
        try { return await extractionTask.ConfigureAwait(false); }
        finally
        {
            monitorCancellation.Cancel();
            await monitorTask.ConfigureAwait(false);
            ReportExtractionProgress(extractionDirectory, legacyProgress, operation, totalBytes, started);
        }
    }

    private static async Task MonitorExtractionDirectoryAsync(string directory, IProgress<ExtractionProgress>? legacy, OperationScope operation, long? total, long started, CancellationToken cancellationToken)
    {
        ReportExtractionProgress(directory, legacy, operation, total, started);
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            ReportExtractionProgress(directory, legacy, operation, total, started);
        }
    }

    private static void ReportExtractionProgress(string directory, IProgress<ExtractionProgress>? legacy, OperationScope operation, long? total, long started)
    {
        var (count, bytes) = GetDirectoryActivity(directory);
        var elapsed = Stopwatch.GetElapsedTime(started);
        legacy?.Report(new ExtractionProgress(count, bytes, elapsed));
        double? percent = total is > 0 ? ClampActivePercent(bytes * 100d / total.Value) : null;
        operation.Report(count, bytes, total, percent, total is > 0 ? ArchiveProgressPrecision.Estimated : ArchiveProgressPrecision.Indeterminate);
    }

    private static (int Count, long Bytes) GetDirectoryActivity(string directory)
    {
        var count = 0; long bytes = 0;
        try
        {
            if (Directory.Exists(directory)) foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try { bytes += new FileInfo(path).Length; count++; } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return (count, bytes);
    }

    private static double ClampActivePercent(double percent) => Math.Clamp(percent, 0, 99);

    private static async Task RunSynchronousOperationAsync(OperationReporter operations, ArchiveOperationKind kind, string? engine, CancellationToken cancellationToken, Action action)
    {
        using var operation = operations.Start(kind, engine);
        try { await Task.Run(action, cancellationToken).ConfigureAwait(false); operation.Complete(); }
        catch (OperationCanceledException) { operation.Cancel(); throw; }
        catch { operation.Fail(); throw; }
    }

    private IEnumerable<IArchiveEngine> OrderEngines(ArchiveEngineKind? kind) => kind is null ? _engines : _engines.OrderBy(engine => engine.Descriptor.Kind == kind ? 0 : 1);

    private static async Task<string> PrepareEngineArchivePathAsync(ArchiveCandidate candidate, ArchiveVolumeSet volumes, string workingDirectory, OperationScope operation, CancellationToken cancellationToken)
    {
        if (candidate.ArchiveOffset > 0)
        {
            if (volumes.SourcePaths.Count != 1 || !string.Equals(volumes.PrimaryPath, candidate.Path, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("An embedded archive cannot be combined with a multi-volume archive set.");
            var sourceLength = new FileInfo(candidate.Path).Length;
            if (candidate.ArchiveLength <= 0 || candidate.ArchiveOffset >= sourceLength || candidate.ArchiveLength > sourceLength - candidate.ArchiveOffset) throw new InvalidDataException("The embedded archive range is outside the source file.");
            var directory = Path.Combine(workingDirectory, "embedded-archive"); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, GetEmbeddedArchiveName(candidate.Format));
            await using var source = new FileStream(candidate.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            source.Position = candidate.ArchiveOffset;
            await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = GC.AllocateUninitializedArray<byte>(1024 * 1024); var remaining = candidate.ArchiveLength; long copied = 0;
            while (remaining > 0)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("The source file ended before the embedded archive was copied.");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                remaining -= read; copied += read;
                operation.Report(0, copied, candidate.ArchiveLength, ClampActivePercent(copied * 100d / candidate.ArchiveLength), ArchiveProgressPrecision.Exact);
            }
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            return path;
        }
        if (volumes.CanonicalNames.Count == 0) return volumes.PrimaryPath;
        var aliases = Path.Combine(workingDirectory, "volume-aliases"); Directory.CreateDirectory(aliases);
        foreach (var sourcePath in volumes.SourcePaths)
        {
            if (!volumes.CanonicalNames.TryGetValue(sourcePath, out var name)) throw new InvalidDataException("The archive volume alias set is incomplete.");
            CreateHardLink(Path.Combine(aliases, name), sourcePath);
        }
        return Path.Combine(aliases, volumes.CanonicalNames[volumes.PrimaryPath]);
    }

    private static void PreserveIncompleteDirectory(string workingDirectory, string sourceDirectory, string logicalName)
    {
        if (!Directory.Exists(workingDirectory)) return;
        try { Directory.Move(workingDirectory, ReserveTargetDirectory(sourceDirectory, logicalName + " - 未完成")); }
        catch (IOException) { /* Preserve the original hidden staging directory if a rename cannot be made. */ }
        catch (UnauthorizedAccessException) { }
    }

    private static void CreateHardLink(string linkPath, string existingPath)
    {
        if (CreateHardLinkNative(linkPath, existingPath, IntPtr.Zero)) return;
        throw new IOException("Windows could not create a temporary archive-volume hard link.", new Win32Exception(Marshal.GetLastWin32Error()));
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkNative(string fileName, string existingFileName, IntPtr securityAttributes);

    private static string GetEmbeddedArchiveName(ArchiveFormat format) => format switch
    {
        ArchiveFormat.SevenZip => "payload.7z", ArchiveFormat.Rar or ArchiveFormat.Rar5 => "payload.rar", ArchiveFormat.Zip => "payload.zip", ArchiveFormat.Tar => "payload.tar", ArchiveFormat.GZip => "payload.gz", ArchiveFormat.BZip2 => "payload.bz2", ArchiveFormat.Xz => "payload.xz", ArchiveFormat.Zstandard => "payload.zst", ArchiveFormat.Cab => "payload.cab", ArchiveFormat.Arj => "payload.arj", ArchiveFormat.Lzh => "payload.lzh", _ => "payload.bin",
    };

    private static void NormalizeOutputTree(string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (TryGetOnlyOrdinaryChildDirectory(directory, out var child)) LiftChildContents(directory, child, cancellationToken);
        foreach (var childDirectory in Directory.GetDirectories(directory)) { cancellationToken.ThrowIfCancellationRequested(); if (!IsReparsePoint(childDirectory)) NormalizeOutputTree(childDirectory, cancellationToken); }
    }
    private static bool TryGetOnlyOrdinaryChildDirectory(string directory, out string childDirectory)
    {
        var entries = Directory.GetFileSystemEntries(directory);
        if (entries.Length == 1 && Directory.Exists(entries[0]) && !IsReparsePoint(entries[0])) { childDirectory = entries[0]; return true; }
        childDirectory = string.Empty; return false;
    }
    private static void LiftChildContents(string directory, string childDirectory, CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(directory) ?? throw new InvalidOperationException("The extraction directory cannot be a file-system root.");
        var temporary = Path.Combine(parent, $".easyunpack-flatten-{Guid.NewGuid():N}"); Directory.Move(childDirectory, temporary);
        foreach (var entry in Directory.GetFileSystemEntries(temporary))
        {
            cancellationToken.ThrowIfCancellationRequested(); var destination = Path.Combine(directory, Path.GetFileName(entry));
            if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0) Directory.Move(entry, destination); else File.Move(entry, destination);
        }
        Directory.Delete(temporary);
    }
    private static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static string ReserveTargetDirectory(string sourceDirectory, string logicalName)
    {
        var candidate = Path.Combine(sourceDirectory, logicalName);
        for (var index = 2; Directory.Exists(candidate) || File.Exists(candidate); index++) candidate = Path.Combine(sourceDirectory, $"{logicalName} ({index})");
        return candidate;
    }

    private sealed record ArchiveContext(ArchiveCandidate Candidate, Guid Id, Guid? ParentId);
    private sealed record EngineSelection(IArchiveEngine Engine, string? Password, long? TotalBytes);

    private sealed class OperationReporter(IProgress<ArchiveOperationUpdate>? progress, ArchiveContext archive)
    {
        private readonly IProgress<ArchiveOperationUpdate>? _progress = progress;
        private readonly ArchiveContext _archive = archive;
        private OperationScope? _last;
        public OperationReporter ForArchive(ArchiveContext child) => new(_progress, child);
        public OperationScope Start(ArchiveOperationKind kind, string? engine = null, ArchiveOperationState state = ArchiveOperationState.Running)
        {
            var scope = new OperationScope(_progress, _archive, kind, engine, state); _last = scope; return scope;
        }
        public void ReportTerminalCancellation() => _last?.Cancel();
    }

    private sealed class OperationScope : IDisposable
    {
        private readonly IProgress<ArchiveOperationUpdate>? _progress;
        private readonly ArchiveContext _archive;
        private readonly ArchiveOperationKind _kind;
        private readonly string? _engine;
        private readonly Guid _id = Guid.NewGuid();
        private readonly long _started = Stopwatch.GetTimestamp();
        private readonly object _gate = new();
        private ArchiveProgressPrecision _bestPrecision = ArchiveProgressPrecision.Indeterminate;
        private double? _percent;
        private long _bytes;
        private long? _total;
        private int _files;
        private bool _terminal;

        public OperationScope(IProgress<ArchiveOperationUpdate>? progress, ArchiveContext archive, ArchiveOperationKind kind, string? engine, ArchiveOperationState state)
        {
            _progress = progress;
            _archive = archive;
            _kind = kind;
            _engine = engine;
            Send(state, 0, 0, null, null, ArchiveProgressPrecision.Indeterminate, terminal: false);
        }

        public void Running() => Send(ArchiveOperationState.Running, 0, 0, null, null, ArchiveProgressPrecision.Indeterminate, terminal: false);
        public void Waiting() => Send(ArchiveOperationState.WaitingForPassword, 0, 0, null, null, ArchiveProgressPrecision.Indeterminate, terminal: false);
        public void Report(int fileCount = 0, long bytes = 0, long? total = null, double? percent = null, ArchiveProgressPrecision precision = ArchiveProgressPrecision.Indeterminate) =>
            Send(ArchiveOperationState.Running, fileCount, bytes, total, percent, precision, terminal: false);
        public void Complete(int fileCount = 0, long bytes = 0, long? total = null, double? percent = 100, ArchiveProgressPrecision precision = ArchiveProgressPrecision.Exact) =>
            Send(ArchiveOperationState.Completed, fileCount, bytes, total, percent, precision, terminal: true);
        public void Fail() => Send(ArchiveOperationState.Failed, 0, 0, null, null, ArchiveProgressPrecision.Indeterminate, terminal: true);
        public void Cancel() => Send(ArchiveOperationState.Canceled, 0, 0, null, null, ArchiveProgressPrecision.Indeterminate, terminal: true);

        private void Send(ArchiveOperationState state, int files, long bytes, long? total, double? percent, ArchiveProgressPrecision precision, bool terminal)
        {
            ArchiveOperationUpdate update;
            lock (_gate)
            {
                if (_terminal) return;
                if (bytes > _bytes) _bytes = bytes;
                if (files > _files) _files = files;
                if (total is > 0) _total = total;

                if (PrecisionRank(precision) >= PrecisionRank(_bestPrecision))
                {
                    _bestPrecision = precision;
                    if (percent is double value)
                    {
                        var active = terminal && state == ArchiveOperationState.Completed ? 100 : ClampActivePercent(value);
                        _percent = Math.Max(_percent ?? 0, active);
                    }
                }

                if (terminal) _terminal = true;
                var outputPercent = terminal && state == ArchiveOperationState.Completed ? 100 : _percent;
                var outputPrecision = _bestPrecision;
                update = new ArchiveOperationUpdate(_archive.Id, _archive.ParentId, _id, _kind, state, _archive.Candidate.LogicalName, _archive.Candidate.Path, _engine,
                    Stopwatch.GetElapsedTime(_started), _bytes, _total, _files, outputPercent, outputPrecision);
            }
            _progress?.Report(update);
        }

        private static int PrecisionRank(ArchiveProgressPrecision precision) => precision switch
        {
            ArchiveProgressPrecision.Exact => 3,
            ArchiveProgressPrecision.Estimated => 2,
            _ => 1,
        };

        public void Dispose() { if (!_terminal) { } }
    }
}
