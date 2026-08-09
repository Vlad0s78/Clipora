using System.Globalization;
using Clipora.Core;
using Clipora.Core.Interfaces;
using Clipora.Core.Models;

namespace Clipora.FFmpeg;

public sealed class FFmpegCommandBuilder : IFFmpegCommandBuilder
{
    private const string AacBitrate = "128k";

    private readonly IBundledToolResolver _toolResolver;

    public FFmpegCommandBuilder(IBundledToolResolver toolResolver)
    {
        _toolResolver = toolResolver ?? throw new ArgumentNullException(nameof(toolResolver));
    }

    public FfmpegCommand Build(EncodeJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        ValidateJob(job);

        string inputPath = Path.GetFullPath(job.InputPath);
        string temporaryOutputPath = Path.GetFullPath(job.TemporaryOutputPath);

        List<string> arguments =
        [
            "-hide_banner",
            "-progress",
            "pipe:1",
            "-nostats",
            "-n",
        ];

        if (job.Mode is EncodeMode.TrimOnly or EncodeMode.TrimAndCompress)
        {
            arguments.Add("-ss");
            arguments.Add(FormatTime(job.TrimRange!.Start));
        }

        arguments.Add("-i");
        arguments.Add(inputPath);

        if (job.Mode is EncodeMode.TrimOnly or EncodeMode.TrimAndCompress)
        {
            arguments.Add("-t");
            arguments.Add(FormatTime(job.TrimRange!.Duration));
        }

        AddStreamMapping(arguments, job.Mode);
        AddMetadataArguments(arguments);

        if (job.Mode == EncodeMode.TrimOnly)
        {
            arguments.Add("-c");
            arguments.Add("copy");
        }
        else
        {
            AddVideoEncoderArguments(arguments, job.Encoder!);
            AddContainerCompatibilityArguments(arguments, job.Encoder!, job.FinalOutputPath);
            AddAudioArguments(arguments, job);
        }

        arguments.Add(temporaryOutputPath);

        string executablePath = _toolResolver.Resolve().FfmpegPath;
        return new FfmpegCommand(
            executablePath,
            arguments.AsReadOnly(),
            inputPath,
            temporaryOutputPath);
    }

    private static void AddStreamMapping(List<string> arguments, EncodeMode mode)
    {
        arguments.Add("-map");
        if (mode == EncodeMode.TrimOnly)
        {
            arguments.Add("0");
            return;
        }

        arguments.Add("0:V:0");
        arguments.Add("-map");
        arguments.Add("0:a:0?");
    }

    private static void AddVideoEncoderArguments(
        List<string> arguments,
        EncoderCapability encoder)
    {
        arguments.Add("-c:v");
        arguments.Add(encoder.EncoderName);

        switch (encoder.EncoderName.ToLowerInvariant())
        {
            case CompressionProfile.NvidiaAv1Encoder:
                arguments.Add("-preset");
                arguments.Add(CompressionProfile.NvidiaAv1Preset);
                arguments.Add("-rc");
                arguments.Add("vbr");
                arguments.Add("-cq");
                arguments.Add(FormatInteger(CompressionProfile.Av1Quality));
                arguments.Add("-b_ref_mode");
                arguments.Add("middle");
                break;

            case CompressionProfile.IntelAv1Encoder:
                arguments.Add("-preset");
                arguments.Add(CompressionProfile.IntelAv1Preset);
                arguments.Add("-global_quality");
                arguments.Add(FormatInteger(CompressionProfile.Av1Quality));
                break;

            case CompressionProfile.AmdAv1Encoder:
                arguments.Add("-quality");
                arguments.Add(CompressionProfile.AmdAv1QualityPreset);
                arguments.Add("-rc");
                arguments.Add("qvbr");
                arguments.Add("-qvbr_quality_level");
                arguments.Add(FormatInteger(CompressionProfile.Av1Quality));
                break;

            case CompressionProfile.NvidiaHevcEncoder:
                arguments.Add("-preset");
                arguments.Add(CompressionProfile.NvidiaHevcPreset);
                arguments.Add("-tune");
                arguments.Add(CompressionProfile.NvidiaHevcTune);
                arguments.Add("-rc");
                arguments.Add("vbr");
                arguments.Add("-cq");
                arguments.Add(FormatInteger(CompressionProfile.HevcQuality));
                arguments.Add("-b:v");
                arguments.Add("0");
                arguments.Add("-multipass");
                arguments.Add("qres");
                arguments.Add("-spatial-aq");
                arguments.Add("1");
                arguments.Add("-temporal-aq");
                arguments.Add("1");
                arguments.Add("-b_ref_mode");
                arguments.Add("middle");
                break;

            case CompressionProfile.IntelHevcEncoder:
                arguments.Add("-preset");
                arguments.Add(CompressionProfile.IntelHevcPreset);
                arguments.Add("-global_quality");
                arguments.Add(FormatInteger(CompressionProfile.HevcQuality));
                break;

            case CompressionProfile.AmdHevcEncoder:
                arguments.Add("-quality");
                arguments.Add(CompressionProfile.AmdHevcQualityPreset);
                arguments.Add("-rc");
                arguments.Add("qvbr");
                arguments.Add("-qvbr_quality_level");
                arguments.Add(FormatInteger(CompressionProfile.HevcQuality));
                break;

            case CompressionProfile.SoftwareAv1FallbackEncoder:
                arguments.Add("-preset");
                arguments.Add(CompressionProfile.SoftwareAv1FallbackPreset);
                arguments.Add("-crf");
                arguments.Add(FormatInteger(CompressionProfile.SoftwareAv1FallbackCrf));
                break;

            default:
                throw new NotSupportedException(
                    $"The video encoder '{encoder.EncoderName}' is not supported.");
        }
    }

    private static void AddContainerCompatibilityArguments(
        List<string> arguments,
        EncoderCapability encoder,
        string outputPath)
    {
        string encoderName = encoder.EncoderName.ToLowerInvariant();
        bool isHevc = encoderName is CompressionProfile.NvidiaHevcEncoder or
            CompressionProfile.IntelHevcEncoder or CompressionProfile.AmdHevcEncoder;
        string outputExtension = Path.GetExtension(outputPath);
        bool usesMp4FamilyContainer = outputExtension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
            outputExtension.Equals(".mov", StringComparison.OrdinalIgnoreCase);

        if (isHevc && usesMp4FamilyContainer)
        {
            arguments.Add("-tag:v");
            arguments.Add("hvc1");
        }
    }

    private static void AddAudioArguments(List<string> arguments, EncodeJob job)
    {
        arguments.Add("-c:a");
        if (CanCopyAudio(job))
        {
            arguments.Add("copy");
            return;
        }

        arguments.Add("aac");
        arguments.Add("-b:a");
        arguments.Add(AacBitrate);
    }

    private static bool CanCopyAudio(EncodeJob job)
    {

        string inputExtension = Path.GetExtension(job.InputPath);
        string outputExtension = Path.GetExtension(job.FinalOutputPath);

        return outputExtension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
            inputExtension.Equals(outputExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddMetadataArguments(List<string> arguments)
    {
        arguments.Add("-map_metadata");
        arguments.Add("0");
        arguments.Add("-map_chapters");
        arguments.Add("0");
    }

    private static void ValidateJob(EncodeJob job)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(job.InputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.TemporaryOutputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.FinalOutputPath);

        if (PathsEqual(job.InputPath, job.TemporaryOutputPath) ||
            PathsEqual(job.InputPath, job.FinalOutputPath))
        {
            throw new InvalidOperationException("The source file cannot be used as an output file.");
        }

        if (PathsEqual(job.TemporaryOutputPath, job.FinalOutputPath))
        {
            throw new InvalidOperationException("Temporary and final output paths must be different.");
        }

        bool requiresTrim = job.Mode is EncodeMode.TrimOnly or EncodeMode.TrimAndCompress;
        if (requiresTrim && job.TrimRange is null)
        {
            throw new ArgumentException("A trim range is required for the selected encode mode.", nameof(job));
        }

        if (!requiresTrim && job.TrimRange is not null)
        {
            throw new ArgumentException("A trim range is not valid for compression-only mode.", nameof(job));
        }

        if (job.Mode != EncodeMode.TrimOnly && job.Encoder is null)
        {
            throw new ArgumentException(
                "An encoder is required for compression.",
                nameof(job));
        }

        if (job.Mode != EncodeMode.TrimOnly && !job.Encoder!.IsAvailable)
        {
            throw new InvalidOperationException(
                $"The video encoder '{job.Encoder.EncoderName}' is not available.");
        }
    }

    private static bool PathsEqual(string first, string second)
    {
        return string.Equals(
            Path.GetFullPath(first),
            Path.GetFullPath(second),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatTime(TimeSpan value)
    {
        return value.TotalSeconds.ToString("0.#######", CultureInfo.InvariantCulture);
    }

    private static string FormatInteger(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}


