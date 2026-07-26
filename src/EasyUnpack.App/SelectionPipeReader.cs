using System.IO;
using System.IO.Pipes;
using System.Text;

namespace EasyUnpack.App;

internal static class SelectionPipeReader
{
    private const int MaxSelectionCount = 10_000;
    private const int MaxPathLength = 32_767;

    public static async Task<IReadOnlyList<string>> ReadAsync(string pipeName, CancellationToken cancellationToken = default)
    {
        if (!pipeName.StartsWith("EasyUnpack.Selection.", StringComparison.Ordinal) || pipeName.IndexOfAny(['\\', '/']) >= 0)
        {
            throw new ArgumentException("Invalid selection pipe name.", nameof(pipeName));
        }

        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);

        var count = await ReadUInt32Async(pipe, timeout.Token).ConfigureAwait(false);
        if (count > MaxSelectionCount) throw new InvalidDataException("Selection count exceeds the supported limit.");

        var paths = new List<string>((int)count);
        for (var index = 0; index < count; index++)
        {
            var characterCount = await ReadUInt32Async(pipe, timeout.Token).ConfigureAwait(false);
            if (characterCount == 0 || characterCount > MaxPathLength) throw new InvalidDataException("Invalid selected path length.");

            var bytes = new byte[checked((int)characterCount * sizeof(char))];
            await pipe.ReadExactlyAsync(bytes, timeout.Token).ConfigureAwait(false);
            paths.Add(Encoding.Unicode.GetString(bytes));
        }

        return paths;
    }

    private static async Task<uint> ReadUInt32Async(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new byte[sizeof(uint)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return BitConverter.ToUInt32(bytes);
    }
}
