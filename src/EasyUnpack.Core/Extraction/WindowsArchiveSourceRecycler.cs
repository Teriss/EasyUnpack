using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32.SafeHandles;

namespace EasyUnpack.Core.Extraction;

public sealed class WindowsArchiveSourceRecycler : IArchiveSourceRecycler
{
    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const uint DeleteAccess = 0x00010000;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint ShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    public Task RecycleAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The Windows recycle bin is required.");

        var existingPaths = paths
            .Select(Path.GetFullPath)
            .Where(static path => File.Exists(path) || Directory.Exists(path))
            .ToArray();

        // Check every volume before changing any source path. This fails quickly for files that
        // are locked against deletion and avoids leaving a split archive only partly recycled.
        foreach (var fullPath in existingPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCanDelete(fullPath);
        }

        foreach (var fullPath in existingPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var operation = new ShellFileOperation { From = fullPath + "\0\0", Function = FoDelete, Flags = FofSilent | FofNoConfirmation | FofAllowUndo };
            var result = ShFileOperation(ref operation);
            if (result == 0 && !operation.AnyOperationsAborted && !File.Exists(fullPath) && !Directory.Exists(fullPath)) continue;

            // Some Explorer hosts report success before completing the move. The managed Shell API
            // provides a second recycle-bin path and lets us verify that the source disappeared.
            try
            {
                if (File.Exists(fullPath))
                {
                    FileSystem.DeleteFile(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
                else if (Directory.Exists(fullPath))
                {
                    FileSystem.DeleteDirectory(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ExternalException)
            {
                throw new IOException($"Unable to move source archive to the recycle bin: {fullPath}", exception);
            }

            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                throw new IOException($"Unable to move source archive to the recycle bin: {fullPath}");
            }
        }

        return Task.CompletedTask;
    }

    private static void EnsureCanDelete(string path)
    {
        var flags = Directory.Exists(path) ? FileFlagBackupSemantics : 0;
        using var handle = CreateFile(
            path,
            DeleteAccess,
            ShareRead | ShareWrite | ShareDelete,
            IntPtr.Zero,
            OpenExisting,
            flags,
            IntPtr.Zero);
        if (!handle.IsInvalid) return;

        var error = Marshal.GetLastPInvokeError();
        throw new IOException($"Unable to move source archive to the recycle bin: {path}", new Win32Exception(error));
    }

    [DllImport("shell32.dll", EntryPoint = "SHFileOperation", CharSet = CharSet.Unicode)]
    private static extern int ShFileOperation(ref ShellFileOperation operation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileOperation
    {
        public IntPtr Window;
        public uint Function;
        [MarshalAs(UnmanagedType.LPWStr)] public string From;
        [MarshalAs(UnmanagedType.LPWStr)] public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }
}
