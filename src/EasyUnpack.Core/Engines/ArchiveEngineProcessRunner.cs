using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace EasyUnpack.Core.Engines;

internal static class ArchiveEngineProcessRunner
{
    public static async Task<EngineExecutionResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        Action<string>? outputChunk = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var job = ProcessJob.TryAssign(process);
        process.StandardInput.Close();
        var outputTask = ReadStreamAsync(process.StandardOutput, outputChunk);
        var errorTask = ReadStreamAsync(process.StandardError, outputChunk);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between the state check and termination request.
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return new EngineExecutionResult(
            process.ExitCode == 0,
            process.ExitCode,
            await outputTask.ConfigureAwait(false),
            await errorTask.ConfigureAwait(false));
    }

    private static async Task<string> ReadStreamAsync(StreamReader reader, Action<string>? chunkHandler)
    {
        var buffer = new char[1024];
        var output = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), CancellationToken.None).ConfigureAwait(false)) > 0)
        {
            var chunk = new string(buffer, 0, read);
            output.Append(chunk);
            chunkHandler?.Invoke(chunk);
        }

        return output.ToString();
    }

    private sealed class ProcessJob : IDisposable
    {
        private const uint ExtendedLimitInformationClass = 9;
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private IntPtr _handle;

        private ProcessJob(IntPtr handle) => _handle = handle;

        public static ProcessJob? TryAssign(Process process)
        {
            if (!OperatingSystem.IsWindows()) return null;
            var handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero) return null;

            var info = new JobObjectExtendedLimitInformation();
            info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
            if (!SetInformationJobObject(handle, ExtendedLimitInformationClass, ref info, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()) ||
                !AssignProcessToJobObject(handle, process.Handle))
            {
                CloseHandle(handle);
                return null;
            }

            return new ProcessJob(handle);
        }

        public void Dispose()
        {
            var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (handle != IntPtr.Zero) CloseHandle(handle);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr job, uint infoClass, ref JobObjectExtendedLimitInformation info, uint length);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
