using System.Diagnostics;
using Clipora.Core.Models;

namespace Clipora.FFmpeg.Tests;

public sealed class FfmpegRunnerTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "Clipora.Runner.Tests",
        "Видео с пробелами",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateStartInfo_UsesDirectHiddenProcessAndArgumentList()
    {
        const string executablePath = @"C:\Program Files\Clipora\ffmpeg\ffmpeg.exe";
        const string sourcePath = @"D:\Видео с пробелами\ролик.mp4";
        const string temporaryPath = @"D:\Видео с пробелами\ролик.tmp.mp4";
        string[] arguments =
        [
            "-progress",
            "pipe:1",
            "-nostats",
            "-i",
            sourcePath,
            temporaryPath,
        ];
        FfmpegCommand command = new(executablePath, arguments, sourcePath, temporaryPath);

        ProcessStartInfo startInfo = FfmpegRunner.CreateStartInfo(command);

        Assert.Equal(executablePath, startInfo.FileName);
        Assert.Equal(@"C:\Program Files\Clipora\ffmpeg", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Hidden, startInfo.WindowStyle);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.Equal(arguments, startInfo.ArgumentList);
    }

    [Fact]
    public async Task RunAsync_ParsesProgressThrottlesReportsAndReturnsSuccess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_temporaryDirectory);
        string temporaryOutputPath = Path.Combine(_temporaryDirectory, "результат кодирования.tmp.mp4");
        FfmpegCommand command = CreateTestHostCommand("progress", temporaryOutputPath);
        ProgressCollector collector = new();
        FfmpegRunner runner = new();

        EncodeResult result = await runner.RunAsync(
            command,
            TimeSpan.FromSeconds(1),
            collector,
            CancellationToken.None);

        IReadOnlyList<EncodeProgress> reports = collector.GetReports();
        Assert.True(result.IsSuccess);
        Assert.False(result.IsCancelled);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(temporaryOutputPath, result.OutputPath);
        Assert.Contains("runner diagnostic", result.StandardError, StringComparison.Ordinal);
        Assert.Equal("encoded", File.ReadAllText(temporaryOutputPath));
        Assert.InRange(reports.Count, 2, 5);
        Assert.Equal(100d, reports[^1].Percent);
        Assert.True(reports[^1].IsComplete);
        Assert.Equal(21, reports[^1].Frame);
        Assert.Equal(800_000, reports[^1].BitrateBitsPerSecond);

        for (int index = 1; index < reports.Count; index++)
        {
            Assert.True(
                reports[index].Elapsed - reports[index - 1].Elapsed >= TimeSpan.FromMilliseconds(90),
                "Progress reports must be separated by approximately 100 ms.");
        }
    }

    [Fact]
    public async Task RunAsync_BoundsFailureDiagnostic()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_temporaryDirectory);
        string temporaryOutputPath = CreateFile("failed.tmp.mp4", "partial output");
        string sourcePath = CreateFile("failed-source.mp4", "immutable source");
        FfmpegCommand command = CreateTestHostCommand("failure", temporaryOutputPath, sourcePath);
        FfmpegRunner runner = new();

        EncodeResult result = await runner.RunAsync(
            command,
            TimeSpan.FromSeconds(1),
            progress: null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(result.IsCancelled);
        Assert.Equal(7, result.ExitCode);
        Assert.Null(result.OutputPath);
        Assert.InRange(result.StandardError.Length, 1, FfmpegRunner.MaximumDiagnosticLength);
        Assert.StartsWith("…", result.StandardError, StringComparison.Ordinal);
        Assert.EndsWith("x", result.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(temporaryOutputPath));
        Assert.Equal("immutable source", File.ReadAllText(sourcePath));
    }

    [Fact]
    public async Task RunAsync_CancellationSendsQAndDeletesOnlyTemporaryOutput()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_temporaryDirectory);
        string temporaryOutputPath = CreateFile("cancelled.tmp.mp4", "partial");
        string finalOutputPath = CreateFile("cancelled.mp4", "final");
        string unrelatedPath = CreateFile("исходник.mp4", "source");
        string startedMarker = Path.Combine(_temporaryDirectory, "graceful-started.marker");
        string qMarker = Path.Combine(_temporaryDirectory, "graceful-q.marker");
        FfmpegCommand command = CreateTestHostCommand(
            "graceful",
            temporaryOutputPath,
            startedMarker: startedMarker,
            qMarker: qMarker);
        FfmpegRunner runner = new(
            gracefulStopTimeout: TimeSpan.FromSeconds(1),
            forcedStopTimeout: TimeSpan.FromSeconds(1));
        using CancellationTokenSource cancellation = new();

        Task<EncodeResult> runTask = runner.RunAsync(
            command,
            TimeSpan.FromSeconds(10),
            progress: null,
            cancellation.Token);
        await WaitForFileAsync(startedMarker);
        cancellation.Cancel();

        EncodeResult result = await runTask;

        Assert.False(result.IsSuccess);
        Assert.True(result.IsCancelled);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.OutputPath);
        Assert.True(File.Exists(qMarker));
        Assert.False(File.Exists(temporaryOutputPath));
        Assert.Equal("final", File.ReadAllText(finalOutputPath));
        Assert.Equal("source", File.ReadAllText(unrelatedPath));
    }

    [Fact]
    public async Task RunAsync_UnresponsiveCancellationKillsProcessTreeWithinBoundedTime()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_temporaryDirectory);
        string temporaryOutputPath = CreateFile("forced-stop.tmp.mp4", "partial");
        string adjacentPath = CreateFile("keep.mp4", "keep");
        string startedMarker = Path.Combine(_temporaryDirectory, "forced-started.marker");
        FfmpegCommand command = CreateTestHostCommand(
            "unresponsive",
            temporaryOutputPath,
            startedMarker: startedMarker);
        FfmpegRunner runner = new(
            gracefulStopTimeout: TimeSpan.FromMilliseconds(150),
            forcedStopTimeout: TimeSpan.FromSeconds(1));
        using CancellationTokenSource cancellation = new();

        Task<EncodeResult> runTask = runner.RunAsync(
            command,
            TimeSpan.FromSeconds(10),
            progress: null,
            cancellation.Token);
        await WaitForFileAsync(startedMarker);
        Stopwatch cancellationStopwatch = Stopwatch.StartNew();
        cancellation.Cancel();

        EncodeResult result = await runTask;
        cancellationStopwatch.Stop();

        Assert.True(result.IsCancelled);
        Assert.True(cancellationStopwatch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.False(File.Exists(temporaryOutputPath));
        Assert.Equal("keep", File.ReadAllText(adjacentPath));
    }

    [Fact]
    public async Task RunAsync_PreCanceledTokenDoesNotStartProcessAndDeletesOnlyTemporaryOutput()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string temporaryOutputPath = CreateFile("pre-cancelled.tmp.mp4", "partial");
        string adjacentPath = CreateFile("pre-cancelled.mp4", "keep");
        FfmpegCommand command = new(
            "missing-ffmpeg-for-pre-cancel.exe",
            [temporaryOutputPath],
            adjacentPath,
            temporaryOutputPath);
        FfmpegRunner runner = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        EncodeResult result = await runner.RunAsync(
            command,
            TimeSpan.FromSeconds(1),
            progress: null,
            cancellation.Token);

        Assert.True(result.IsCancelled);
        Assert.Equal(-1, result.ExitCode);
        Assert.False(File.Exists(temporaryOutputPath));
        Assert.Equal("keep", File.ReadAllText(adjacentPath));
    }

    [Fact]
    public async Task RunAsync_UnsafeCleanupCommandNeverDeletesSource()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string sourcePath = CreateFile("source.tmp.mp4", "immutable source");
        FfmpegCommand command = new(
            "missing-ffmpeg-for-pre-cancel.exe",
            [sourcePath],
            sourcePath,
            sourcePath);
        FfmpegRunner runner = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunAsync(
                command,
                TimeSpan.FromSeconds(1),
                progress: null,
                cancellation.Token));

        Assert.Contains("source path", exception.Message, StringComparison.Ordinal);
        Assert.Equal("immutable source", File.ReadAllText(sourcePath));
    }

    [Fact]
    public async Task RunAsync_LastArgumentMismatchPreservesEveryFile()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string sourcePath = CreateFile("source.mp4", "source");
        string temporaryOutputPath = CreateFile("target.tmp.mp4", "partial");
        string unrelatedPath = CreateFile("unrelated.tmp.mp4", "unrelated");
        FfmpegCommand command = new(
            "missing-ffmpeg-for-pre-cancel.exe",
            [unrelatedPath],
            sourcePath,
            temporaryOutputPath);
        FfmpegRunner runner = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunAsync(
                command,
                TimeSpan.FromSeconds(1),
                progress: null,
                cancellation.Token));

        Assert.Contains("last command argument", exception.Message, StringComparison.Ordinal);
        Assert.Equal("source", File.ReadAllText(sourcePath));
        Assert.Equal("partial", File.ReadAllText(temporaryOutputPath));
        Assert.Equal("unrelated", File.ReadAllText(unrelatedPath));
    }

    [Fact]
    public async Task RunAsync_UnsafeTemporaryNameIsRejectedBeforeProcessStart()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string sourcePath = CreateFile("name-source.mp4", "source");
        string unsafeOutputPath = CreateFile("name-output.mp4", "must stay");
        FfmpegCommand command = new(
            "missing-ffmpeg-that-must-not-start.exe",
            [unsafeOutputPath],
            sourcePath,
            unsafeOutputPath);
        FfmpegRunner runner = new();

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunAsync(
                command,
                TimeSpan.FromSeconds(1),
                progress: null,
                CancellationToken.None));

        Assert.Contains("<name>.tmp<extension>", exception.Message, StringComparison.Ordinal);
        Assert.Equal("source", File.ReadAllText(sourcePath));
        Assert.Equal("must stay", File.ReadAllText(unsafeOutputPath));
    }

    [Fact]
    public async Task RunAsync_StartFailureDeletesSafeTemporaryOutputAndPreservesSource()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string sourcePath = CreateFile("start-source.mp4", "immutable source");
        string temporaryOutputPath = CreateFile("start-output.tmp.mp4", "partial");
        FfmpegCommand command = new(
            "missing-ffmpeg-start-failure.exe",
            [temporaryOutputPath],
            sourcePath,
            temporaryOutputPath);
        FfmpegRunner runner = new();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(
                command,
                TimeSpan.FromSeconds(1),
                progress: null,
                CancellationToken.None));

        Assert.False(File.Exists(temporaryOutputPath));
        Assert.Equal("immutable source", File.ReadAllText(sourcePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private static FfmpegCommand CreateTestHostCommand(
        string mode,
        string temporaryOutputPath,
        string? sourcePath = null,
        string? startedMarker = null,
        string? qMarker = null)
    {
        string executablePath = Path.Combine(AppContext.BaseDirectory, "Clipora.FFmpeg.TestHost.exe");
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("The FFmpeg runner test host was not copied to the test output.", executablePath);
        }

        string directory = Path.GetDirectoryName(temporaryOutputPath)
            ?? throw new InvalidOperationException("Temporary test path has no directory.");
        Directory.CreateDirectory(directory);

        sourcePath ??= Path.Combine(directory, $"source-{Guid.NewGuid():N}.mp4");
        if (!File.Exists(sourcePath))
        {
            File.WriteAllText(sourcePath, "source");
        }

        string[] arguments =
        [
            mode,
            startedMarker ?? "-",
            qMarker ?? "-",
            sourcePath,
            temporaryOutputPath,
        ];

        return new FfmpegCommand(executablePath, arguments, sourcePath, temporaryOutputPath);
    }

    private static async Task WaitForFileAsync(string path)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (!File.Exists(path) && timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(25);
        }

        Assert.True(File.Exists(path), $"The child process did not create '{path}'.");
    }

    private string CreateFile(string fileName, string content)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string path = Path.Combine(_temporaryDirectory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class ProgressCollector : IProgress<EncodeProgress>
    {
        private readonly Lock _syncRoot = new();
        private readonly List<EncodeProgress> _reports = [];

        public void Report(EncodeProgress value)
        {
            lock (_syncRoot)
            {
                _reports.Add(value);
            }
        }

        public IReadOnlyList<EncodeProgress> GetReports()
        {
            lock (_syncRoot)
            {
                return _reports.ToArray();
            }
        }
    }
}
