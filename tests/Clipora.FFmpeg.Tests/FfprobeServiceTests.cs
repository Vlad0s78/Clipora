using System.Diagnostics;
using Clipora.Core.Interfaces;
using Clipora.Core.Tools;

namespace Clipora.FFmpeg.Tests;

public sealed class FfprobeServiceTests
{
    [Fact]
    public void CreateStartInfo_UsesDirectHiddenProcessAndPreservesUnicodePath()
    {
        const string ffprobePath = @"C:\Program Files\Clipora\Tools\ffmpeg\ffprobe.exe";
        const string inputPath = @"D:\Видео с пробелами\быстрый тест.mp4";

        ProcessStartInfo startInfo = FfprobeService.CreateStartInfo(ffprobePath, inputPath);

        Assert.Equal(ffprobePath, startInfo.FileName);
        Assert.Equal(@"C:\Program Files\Clipora\Tools\ffmpeg", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Hidden, startInfo.WindowStyle);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Equal(
            [
                "-hide_banner",
                "-v",
                "error",
                "-print_format",
                "json",
                "-show_format",
                "-show_streams",
                inputPath,
            ],
            startInfo.ArgumentList);
    }

    [Fact]
    public async Task AnalyzeAsync_ThrowsForEmptyPath()
    {
        FfprobeService service = new(new StubToolResolver());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AnalyzeAsync(" ", CancellationToken.None));
    }

    [Fact]
    public async Task AnalyzeAsync_ThrowsForMissingFile()
    {
        FfprobeService service = new(new StubToolResolver());
        string path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.mp4");

        FileNotFoundException exception = await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.AnalyzeAsync(path, CancellationToken.None));

        Assert.Equal(Path.GetFullPath(path), exception.FileName);
    }

    [Fact]
    public async Task AnalyzeAsync_HonorsPreCanceledToken()
    {
        FfprobeService service = new(new StubToolResolver());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.AnalyzeAsync("missing.mp4", cancellation.Token));
    }

    [Fact]
    public async Task WaitForDrainAsync_ReturnsFalseWithinBoundForIncompleteDrain()
    {
        TaskCompletionSource drain = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Stopwatch stopwatch = Stopwatch.StartNew();

        bool completed = await FfprobeService.WaitForDrainAsync(
            drain.Task,
            TimeSpan.FromMilliseconds(50));

        Assert.False(completed);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WaitForDrainAsync_ReturnsTrueForCompletedDrain()
    {
        bool completed = await FfprobeService.WaitForDrainAsync(
            Task.CompletedTask,
            TimeSpan.FromSeconds(1));

        Assert.True(completed);
    }

    private sealed class StubToolResolver : IBundledToolResolver
    {
        public BundledToolPaths Resolve()
        {
            return new BundledToolPaths("ffmpeg.exe", "ffprobe.exe");
        }
    }
}
