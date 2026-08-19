using Clipora.Core.Interfaces;
using Clipora.Core.Models;

namespace Clipora.FFmpeg.Tests;

public sealed class VideoProcessingServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "Clipora.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ProcessAsync_Compress_SelectsFirstAvailableEncoderAndFinalizesCopy()
    {
        string sourcePath = CreateSource();
        string outputDirectory = Path.Combine(_temporaryDirectory, "output");
        EncoderCapability selectedEncoder = new("hevc_nvenc", true, true, null);
        RecordingEncoderDetector detector = new(
        [
            new EncoderCapability("av1_nvenc", false, true, "unavailable"),
            selectedEncoder,
            new EncoderCapability("libsvtav1", true, false, null),
        ]);
        RecordingCommandBuilder builder = new();
        RecordingRunner runner = SuccessfulRunner("compressed");
        VideoProcessingService service = CreateService(detector, builder, runner);

        string finalPath = await service.ProcessAsync(
            sourcePath,
            EncodeMode.Compress,
            TimeSpan.FromMinutes(2),
            outputDirectory: outputDirectory);

        Assert.Equal(Path.Combine(outputDirectory, "source_compressed.mp4"), finalPath);
        Assert.Equal("compressed", File.ReadAllText(finalPath));
        Assert.Equal("original", File.ReadAllText(sourcePath));
        Assert.False(File.Exists(builder.Job!.TemporaryOutputPath));
        Assert.Equal(selectedEncoder, builder.Job.Encoder);
        Assert.Null(builder.Job.TrimRange);
        Assert.Equal(TimeSpan.FromMinutes(2), runner.SourceDuration);
        Assert.Equal(1, detector.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_TrimAndCompress_UsesTrimDuration()
    {
        string sourcePath = CreateSource();
        TrimRange range = new(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(37));
        RecordingEncoderDetector detector = AvailableDetector();
        RecordingCommandBuilder builder = new();
        RecordingRunner runner = SuccessfulRunner("trimmed and compressed");
        VideoProcessingService service = CreateService(detector, builder, runner);

        string finalPath = await service.ProcessAsync(
            sourcePath,
            EncodeMode.TrimAndCompress,
            TimeSpan.FromMinutes(1),
            range);

        Assert.EndsWith("source_trimmed_compressed.mp4", finalPath, StringComparison.Ordinal);
        Assert.Equal("trimmed and compressed", File.ReadAllText(finalPath));
        Assert.Equal(range, builder.Job!.TrimRange);
        Assert.NotNull(builder.Job.Encoder);
        Assert.Equal(range.Duration, runner.SourceDuration);
        Assert.Equal(1, detector.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_TrimOnly_SkipsEncoderDetectionAndUsesTrimDuration()
    {
        string sourcePath = CreateSource();
        TrimRange range = new(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(11));
        RecordingEncoderDetector detector = new([], throwWhenCalled: true);
        RecordingCommandBuilder builder = new();
        RecordingRunner runner = SuccessfulRunner("trimmed");
        VideoProcessingService service = CreateService(detector, builder, runner);

        string finalPath = await service.ProcessAsync(
            sourcePath,
            EncodeMode.TrimOnly,
            TimeSpan.FromSeconds(20),
            range);

        Assert.EndsWith("source_trimmed.mp4", finalPath, StringComparison.Ordinal);
        Assert.Equal("trimmed", File.ReadAllText(finalPath));
        Assert.Null(builder.Job!.Encoder);
        Assert.Equal(range.Duration, runner.SourceDuration);
        Assert.Equal(0, detector.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_CancelledRunDeletesTemporaryOutput()
    {
        string sourcePath = CreateSource();
        RecordingCommandBuilder builder = new();
        RecordingRunner runner = new()
        {
            Handler = command =>
            {
                File.WriteAllText(command.TemporaryOutputPath, "partial");
                return new EncodeResult(false, true, -1, null, string.Empty, TimeSpan.Zero);
            },
        };
        VideoProcessingService service = CreateService(AvailableDetector(), builder, runner);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ProcessAsync(
                sourcePath,
                EncodeMode.Compress,
                TimeSpan.FromSeconds(20)));

        Assert.False(File.Exists(builder.Job!.TemporaryOutputPath));
        Assert.False(File.Exists(builder.Job.FinalOutputPath));
        Assert.Equal("original", File.ReadAllText(sourcePath));
    }

    [Fact]
    public async Task ProcessAsync_NoAvailableEncoderDoesNotBuildOrRun()
    {
        string sourcePath = CreateSource();
        RecordingEncoderDetector detector = new(
        [
            new EncoderCapability("av1_nvenc", false, true, "unavailable"),
            new EncoderCapability("libsvtav1", false, false, "unavailable"),
        ]);
        RecordingCommandBuilder builder = new();
        RecordingRunner runner = new();
        VideoProcessingService service = CreateService(detector, builder, runner);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProcessAsync(
                sourcePath,
                EncodeMode.Compress,
                TimeSpan.FromSeconds(20)));

        Assert.Contains("No supported video encoder", exception.Message, StringComparison.Ordinal);
        Assert.Null(builder.Job);
        Assert.Equal(0, runner.CallCount);
        Assert.Equal("original", File.ReadAllText(sourcePath));
        Assert.False(File.Exists(Path.Combine(_temporaryDirectory, "source_compressed.tmp.mp4")));
    }

    [Fact]
    public async Task ProcessAsync_FinalizationFailureDeletesTemporaryAndPreservesExistingFinal()
    {
        string sourcePath = CreateSource();
        RecordingCommandBuilder builder = new();
        RecordingRunner runner = new()
        {
            Handler = command =>
            {
                File.WriteAllText(command.TemporaryOutputPath, "new output");
                File.WriteAllText(builder.Job!.FinalOutputPath, "existing output");
                return new EncodeResult(
                    true,
                    false,
                    0,
                    command.TemporaryOutputPath,
                    string.Empty,
                    TimeSpan.FromSeconds(1));
            },
        };
        VideoProcessingService service = CreateService(AvailableDetector(), builder, runner);

        await Assert.ThrowsAsync<IOException>(
            () => service.ProcessAsync(
                sourcePath,
                EncodeMode.Compress,
                TimeSpan.FromSeconds(20)));

        Assert.False(File.Exists(builder.Job!.TemporaryOutputPath));
        Assert.Equal("existing output", File.ReadAllText(builder.Job.FinalOutputPath));
        Assert.Equal("original", File.ReadAllText(sourcePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private static VideoProcessingService CreateService(
        IEncoderDetector detector,
        IFFmpegCommandBuilder builder,
        IFFmpegRunner runner)
    {
        return new VideoProcessingService(detector, builder, runner, new OutputFileService());
    }

    private static RecordingEncoderDetector AvailableDetector()
    {
        return new RecordingEncoderDetector(
            [new EncoderCapability("libsvtav1", true, false, null)]);
    }

    private static RecordingRunner SuccessfulRunner(string outputContents)
    {
        return new RecordingRunner
        {
            Handler = command =>
            {
                File.WriteAllText(command.TemporaryOutputPath, outputContents);
                return new EncodeResult(
                    true,
                    false,
                    0,
                    command.TemporaryOutputPath,
                    string.Empty,
                    TimeSpan.FromSeconds(1));
            },
        };
    }

    private string CreateSource()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string path = Path.Combine(_temporaryDirectory, "source.mp4");
        File.WriteAllText(path, "original");
        return path;
    }

    private sealed class RecordingEncoderDetector : IEncoderDetector
    {
        private readonly IReadOnlyList<EncoderCapability> _capabilities;
        private readonly bool _throwWhenCalled;

        public RecordingEncoderDetector(
            IReadOnlyList<EncoderCapability> capabilities,
            bool throwWhenCalled = false)
        {
            _capabilities = capabilities;
            _throwWhenCalled = throwWhenCalled;
        }

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<EncoderCapability>> DetectAsync(
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (_throwWhenCalled)
            {
                throw new InvalidOperationException("Encoder detection was not expected.");
            }

            return Task.FromResult(_capabilities);
        }
    }

    private sealed class RecordingCommandBuilder : IFFmpegCommandBuilder
    {
        public EncodeJob? Job { get; private set; }

        public FfmpegCommand Build(EncodeJob job)
        {
            Job = job;
            return new FfmpegCommand(
                "ffmpeg.exe",
                [job.TemporaryOutputPath],
                job.InputPath,
                job.TemporaryOutputPath);
        }
    }

    private sealed class RecordingRunner : IFFmpegRunner
    {
        public Func<FfmpegCommand, EncodeResult>? Handler { get; init; }

        public int CallCount { get; private set; }

        public TimeSpan SourceDuration { get; private set; }

        public Task<EncodeResult> RunAsync(
            FfmpegCommand command,
            TimeSpan sourceDuration,
            IProgress<EncodeProgress>? progress,
            CancellationToken cancellationToken)
        {
            CallCount++;
            SourceDuration = sourceDuration;
            EncodeResult result = Handler?.Invoke(command)
                ?? throw new InvalidOperationException("Runner invocation was not expected.");
            return Task.FromResult(result);
        }
    }
}
