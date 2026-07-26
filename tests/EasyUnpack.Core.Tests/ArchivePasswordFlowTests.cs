using EasyUnpack.Core.Archives;
using EasyUnpack.Core.Engines;
using EasyUnpack.Core.Extraction;

namespace EasyUnpack.Core.Tests;

public sealed class ArchivePasswordFlowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"EasyUnpackPasswordFlow-{Guid.NewGuid():N}");

    public ArchivePasswordFlowTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Extract_uses_the_first_password_that_validates()
    {
        var archivePath = Path.Combine(_directory, "archive.rar");
        await File.WriteAllBytesAsync(archivePath, Convert.FromHexString("526172211A0700"));
        var engine = new PasswordEngine("right");
        var service = new ArchiveExtractionService(engine, new NoopRecycler());

        await service.ExtractAsync(new ArchiveCandidate(archivePath, ArchiveFormat.Rar, true), ["wrong", "right"]);

        Assert.Equal("right", engine.ExtractionPassword);
    }

    [Fact]
    public async Task Extract_reports_password_requirement_without_cleaning_source()
    {
        var archivePath = Path.Combine(_directory, "archive.rar");
        await File.WriteAllBytesAsync(archivePath, Convert.FromHexString("526172211A0700"));
        var service = new ArchiveExtractionService(new PasswordEngine("right"), new NoopRecycler());

        var exception = await Assert.ThrowsAsync<ArchivePasswordRequiredException>(() => service.ExtractAsync(new ArchiveCandidate(archivePath, ArchiveFormat.Rar, true), ["wrong"]));

        Assert.Equal(archivePath, exception.ArchivePath);
        Assert.True(File.Exists(archivePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class PasswordEngine(string correctPassword) : IPasswordArchiveEngine
    {
        public string? ExtractionPassword { get; private set; }
        public ArchiveEngineDescriptor Descriptor { get; } = new(ArchiveEngineKind.SevenZip, "fake.exe", "test");
        public Task<EngineExecutionResult> ListAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Failure());
        public Task<EngineExecutionResult> TestAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(Failure());
        public Task<EngineExecutionResult> TestWithPasswordAsync(string archivePath, string password, CancellationToken cancellationToken = default) => Task.FromResult(password == correctPassword ? Success() : Failure());

        public Task<EngineExecutionResult> ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default) => Task.FromResult(Failure());

        public async Task<EngineExecutionResult> ExtractWithPasswordAsync(string archivePath, string destinationDirectory, string password, CancellationToken cancellationToken = default)
        {
            ExtractionPassword = password;
            await File.WriteAllTextAsync(Path.Combine(destinationDirectory, "file.txt"), "content", cancellationToken);
            return Success();
        }

        private static EngineExecutionResult Success() => new(true, 0, string.Empty, string.Empty);
        private static EngineExecutionResult Failure() => new(false, 2, string.Empty, string.Empty);
    }

    private sealed class NoopRecycler : IArchiveSourceRecycler
    {
        public Task RecycleAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
