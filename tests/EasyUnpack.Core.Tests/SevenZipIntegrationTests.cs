using System.IO.Compression;
using System.Diagnostics;
using System.Security.Cryptography;
using EasyUnpack.Core.Archives;
using EasyUnpack.Core.Engines;
using EasyUnpack.Core.Extraction;

namespace EasyUnpack.Core.Tests;

public sealed class SevenZipIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"EasyUnpack7ZipTests-{Guid.NewGuid():N}");

    public SevenZipIntegrationTests() => Directory.CreateDirectory(_directory);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SevenZip_extracts_a_signature_detected_archive_with_a_disguised_extension()
    {
        var descriptor = ArchiveEngineDiscovery.FindAvailable().FirstOrDefault(engine => engine.Kind == ArchiveEngineKind.SevenZip);
        if (descriptor is null) return;

        var archivePath = Path.Combine(_directory, "资源.jpg");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("RI10003/content.txt");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("content");
        }

        var recycler = new RecordingRecycler();
        var service = new ArchiveExtractionService(new SevenZipEngine(descriptor), recycler);
        var result = await service.ExtractAsync(new ArchiveCandidate(archivePath, ArchiveFormat.Zip, true));

        Assert.True(result.SourceRecycled);
        Assert.Equal(Path.Combine(_directory, "资源"), result.OutputDirectory);
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "content.txt")));
        Assert.Equal([archivePath], recycler.Paths);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SevenZip_extracts_disguised_split_volumes_through_temporary_canonical_names()
    {
        var descriptor = ArchiveEngineDiscovery.FindAvailable().FirstOrDefault(engine => engine.Kind == ArchiveEngineKind.SevenZip);
        if (descriptor is null) return;

        var inputDirectory = Directory.CreateDirectory(Path.Combine(_directory, "input"));
        await File.WriteAllBytesAsync(Path.Combine(inputDirectory.FullName, "content.bin"), RandomNumberGenerator.GetBytes(8_192));
        var archiveBasePath = Path.Combine(_directory, "split.7z");
        await RunProcessAsync(descriptor.ExecutablePath, ["a", "-t7z", "-v1k", archiveBasePath, inputDirectory.FullName]);

        var volumePaths = Directory.EnumerateFiles(_directory, "split.7z.*", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        Assert.True(volumePaths.Length > 1);
        foreach (var volumePath in volumePaths)
        {
            File.Move(volumePath, volumePath + ".jpg");
        }

        var firstVolume = volumePaths[0] + ".jpg";
        var candidate = new ArchiveCandidate(firstVolume, ArchiveSignatureProbe.Probe(firstVolume).Format, true);
        var recycler = new RecordingRecycler();
        var result = await new ArchiveExtractionService(new SevenZipEngine(descriptor), recycler).ExtractAsync(candidate);

        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "content.bin")));
        Assert.False(Directory.Exists(Path.Combine(result.OutputDirectory, "input")));
        Assert.Equal(volumePaths.Select(path => path + ".jpg").ToArray(), recycler.Paths);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SevenZip_extracts_split_volumes_that_lost_their_7zip_extension()
    {
        var descriptor = ArchiveEngineDiscovery.FindAvailable().FirstOrDefault(engine => engine.Kind == ArchiveEngineKind.SevenZip);
        if (descriptor is null) return;

        var inputDirectory = Directory.CreateDirectory(Path.Combine(_directory, "input"));
        await File.WriteAllBytesAsync(Path.Combine(inputDirectory.FullName, "content.bin"), RandomNumberGenerator.GetBytes(8_192));
        var archiveBasePath = Path.Combine(_directory, "alias.7z");
        await RunProcessAsync(descriptor.ExecutablePath, ["a", "-t7z", "-v1k", archiveBasePath, inputDirectory.FullName]);

        var volumePaths = Directory.EnumerateFiles(_directory, "alias.7z.*", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var disguisedPaths = new List<string>();
        foreach (var volumePath in volumePaths)
        {
            var index = Path.GetExtension(volumePath);
            var disguisedPath = Path.Combine(_directory, "alias" + index + ".jpg");
            File.Move(volumePath, disguisedPath);
            disguisedPaths.Add(disguisedPath);
        }

        var firstVolume = disguisedPaths[0];
        var candidate = new ArchiveCandidate(firstVolume, ArchiveSignatureProbe.Probe(firstVolume).Format, true);
        var recycler = new RecordingRecycler();
        var result = await new ArchiveExtractionService(new SevenZipEngine(descriptor), recycler).ExtractAsync(candidate);

        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "content.bin")));
        Assert.False(Directory.Exists(Path.Combine(result.OutputDirectory, "input")));
        Assert.Equal(disguisedPaths, recycler.Paths);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SevenZip_password_probe_does_not_wait_for_interactive_input()
    {
        var descriptor = ArchiveEngineDiscovery.FindAvailable().FirstOrDefault(engine => engine.Kind == ArchiveEngineKind.SevenZip);
        if (descriptor is null) return;

        var contentPath = Path.Combine(_directory, "protected-content.txt");
        await File.WriteAllTextAsync(contentPath, "content");
        var archivePath = Path.Combine(_directory, "protected.zip");
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(18));
        await RunProcessAsync(descriptor.ExecutablePath, ["a", "-tzip", "-mem=AES256", $"-p{password}", archivePath, contentPath]);

        var engine = new SevenZipEngine(descriptor);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var withoutPassword = await engine.TestAsync(archivePath, timeout.Token);
        var withPassword = await engine.TestWithPasswordAsync(archivePath, password, timeout.Token);

        Assert.False(withoutPassword.Succeeded);
        Assert.True(withPassword.Succeeded);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class RecordingRecycler : IArchiveSourceRecycler
    {
        public IReadOnlyList<string> Paths { get; private set; } = [];
        public Task RecycleAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        {
            Paths = paths;
            return Task.CompletedTask;
        }
    }

    private static async Task RunProcessAsync(string executable, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start 7-Zip.");
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"7-Zip failed with exit code {process.ExitCode}.");
    }
}
