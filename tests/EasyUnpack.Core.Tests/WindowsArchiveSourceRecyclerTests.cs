using System.Diagnostics;
using EasyUnpack.Core.Extraction;

namespace EasyUnpack.Core.Tests;

public sealed class WindowsArchiveSourceRecyclerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"EasyUnpackRecyclerTests-{Guid.NewGuid():N}");

    public WindowsArchiveSourceRecyclerTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Locked_source_fails_quickly_without_entering_the_shell_recycle_operation()
    {
        if (!OperatingSystem.IsWindows()) return;

        var path = Path.Combine(_directory, "locked.zip");
        await File.WriteAllTextAsync(path, "test");
        await using var lockStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<IOException>(() => new WindowsArchiveSourceRecycler().RecycleAsync([path]));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
        Assert.True(File.Exists(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
