using Clipora.Core.Interfaces;
using Clipora.Core.Models;

namespace Clipora.FFmpeg;

public sealed class VideoProcessingService : IVideoProcessingService
{
    private readonly IEncoderDetector _encoderDetector;
    private readonly IFFmpegCommandBuilder _commandBuilder;
    private readonly IFFmpegRunner _runner;
    private readonly IOutputFileService _outputFileService;

    public VideoProcessingService(
        IEncoderDetector encoderDetector,
        IFFmpegCommandBuilder commandBuilder,
        IFFmpegRunner runner,
        IOutputFileService outputFileService)
    {
        _encoderDetector = encoderDetector ?? throw new ArgumentNullException(nameof(encoderDetector));
        _commandBuilder = commandBuilder ?? throw new ArgumentNullException(nameof(commandBuilder));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _outputFileService = outputFileService ?? throw new ArgumentNullException(nameof(outputFileService));
    }

    public async Task<string> ProcessAsync(
        string inputPath,
        EncodeMode mode,
        TimeSpan sourceDuration,
        TrimRange? trimRange = null,
        string? outputDirectory = null,
        IProgress<EncodeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(mode, sourceDuration, trimRange);
        cancellationToken.ThrowIfCancellationRequested();

        OutputFilePlan plan = _outputFileService.CreatePlan(inputPath, mode, outputDirectory);

        try
        {
            EncoderCapability? encoder = mode == EncodeMode.TrimOnly
                ? null
                : await SelectEncoderAsync(cancellationToken).ConfigureAwait(false);

            EncodeJob job = new(
                plan.SourcePath,
                plan.TemporaryPath,
                plan.FinalPath,
                mode,
                encoder,
                trimRange);
            FfmpegCommand command = _commandBuilder.Build(job);
            TimeSpan progressDuration = mode == EncodeMode.Compress
                ? sourceDuration
                : trimRange!.Duration;

            EncodeResult result = await _runner.RunAsync(
                    command,
                    progressDuration,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.IsCancelled)
            {
                throw new OperationCanceledException(
                    "Video processing was cancelled.",
                    cancellationToken);
            }

            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"FFmpeg failed with exit code {result.ExitCode}: {result.StandardError}");
            }

            await _outputFileService.FinalizeAsync(plan, cancellationToken).ConfigureAwait(false);
            return plan.FinalPath;
        }
        finally
        {
            _outputFileService.DeleteTemporaryFile(plan);
        }
    }

    private async Task<EncoderCapability> SelectEncoderAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<EncoderCapability> capabilities =
            await _encoderDetector.DetectAsync(cancellationToken).ConfigureAwait(false);

        return capabilities.FirstOrDefault(capability => capability.IsAvailable)
            ?? throw new InvalidOperationException("No supported video encoder is available.");
    }

    private static void ValidateRequest(
        EncodeMode mode,
        TimeSpan sourceDuration,
        TrimRange? trimRange)
    {
        if (sourceDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceDuration));
        }

        switch (mode)
        {
            case EncodeMode.Compress when trimRange is not null:
                throw new ArgumentException(
                    "A trim range is not valid for compression-only mode.",
                    nameof(trimRange));
            case EncodeMode.TrimOnly or EncodeMode.TrimAndCompress when trimRange is null:
                throw new ArgumentException(
                    "A trim range is required for the selected mode.",
                    nameof(trimRange));
            case EncodeMode.TrimOnly or EncodeMode.TrimAndCompress
                when trimRange!.End > sourceDuration:
                throw new ArgumentOutOfRangeException(
                    nameof(trimRange),
                    "The trim range must be within the source duration.");
            case EncodeMode.TrimOnly:
            case EncodeMode.Compress:
            case EncodeMode.TrimAndCompress:
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown encode mode.");
        }
    }
}
