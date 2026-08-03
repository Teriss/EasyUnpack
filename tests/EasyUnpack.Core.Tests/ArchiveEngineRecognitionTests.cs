using System.Diagnostics;
using EasyUnpack.Core.Archives;
using EasyUnpack.Core.Engines;

namespace EasyUnpack.Core.Tests;

public sealed class ArchiveEngineRecognitionTests
{
    [Fact]
    public void Classifier_reports_recognized_format()
    {
        var result = ArchiveEngineResultClassifier.Classify(
            new EngineExecutionResult(true, 0, "Type = 7z", string.Empty),
            validation: false);

        Assert.Equal(ArchiveRecognitionStatus.Recognized, result.Status);
        Assert.Equal(ArchiveFormat.SevenZip, result.Format);
    }

    [Fact]
    public void Classifier_reports_password_requirement()
    {
        var result = ArchiveEngineResultClassifier.Classify(
            new EngineExecutionResult(false, 2, string.Empty, "Cannot open encrypted archive. Wrong password?"),
            validation: true);

        Assert.Equal(ArchiveRecognitionStatus.PasswordRequired, result.Status);
    }

    [Fact]
    public void Classifier_reports_non_archive()
    {
        var result = ArchiveEngineResultClassifier.Classify(
            new EngineExecutionResult(false, 2, string.Empty, "Cannot open the file as archive"),
            validation: false);

        Assert.Equal(ArchiveRecognitionStatus.NotArchive, result.Status);
    }

    [Fact]
    public void Classifier_does_not_report_corruption_as_a_password_error()
    {
        var result = ArchiveEngineResultClassifier.Classify(
            new EngineExecutionResult(false, 2, string.Empty, "CRC Failed and Headers Error"),
            validation: true);

        Assert.Equal(ArchiveRecognitionStatus.UnsupportedOrCorrupt, result.Status);
    }

    [Fact]
    public async Task Process_runner_cancels_and_terminates_a_long_running_process()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArchiveEngineProcessRunner.RunAsync(
            "powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"],
            cancellation.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
    }
}
