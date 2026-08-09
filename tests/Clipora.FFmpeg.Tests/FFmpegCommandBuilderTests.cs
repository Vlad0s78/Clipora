using Clipora.Core.Interfaces;
using Clipora.Core.Models;
using Clipora.Core.Tools;

namespace Clipora.FFmpeg.Tests;

public sealed class FFmpegCommandBuilderTests
{
    private const string FfmpegPath = @"C:\Program Files\Clipora\Tools\ffmpeg\ffmpeg.exe";

    private readonly FFmpegCommandBuilder _builder = new(new StubToolResolver());

    [Fact]
    public void Build_TrimOnly_UsesStreamCopyAndPreservesUnicodePathsAsSingleArguments()
    {
        const string inputPath = @"D:\Видео с пробелами\исходник.mp4";
        const string temporaryPath = @"D:\Видео с пробелами\исходник_trimmed.tmp.mp4";
        EncodeJob job = CreateJob(
            inputPath,
            temporaryPath,
            @"D:\Видео с пробелами\исходник_trimmed.mp4",
            EncodeMode.TrimOnly,
            "libsvtav1",
            new TrimRange(TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(10.25)));

        FfmpegCommand command = _builder.Build(job);

        Assert.Equal(FfmpegPath, command.ExecutablePath);
        Assert.Equal(inputPath, command.SourcePath);
        Assert.Equal(temporaryPath, command.TemporaryOutputPath);
        Assert.Equal(command.TemporaryOutputPath, command.Arguments[^1]);
        Assert.Equal(
            [
                "-hide_banner",
                "-progress",
                "pipe:1",
                "-nostats",
                "-n",
                "-ss",
                "2.5",
                "-i",
                inputPath,
                "-t",
                "7.75",
                "-map",
                "0",
                "-map_metadata",
                "0",
                "-map_chapters",
                "0",
                "-c",
                "copy",
                temporaryPath,
            ],
            command.Arguments);
    }

    [Fact]
    public void Build_TrimOnly_DoesNotRequireEncoder()
    {
        EncodeJob job = new(
            @"D:\video\input.mp4",
            @"D:\video\input_trimmed.tmp.mp4",
            @"D:\video\input_trimmed.mp4",
            EncodeMode.TrimOnly,
            Encoder: null,
            new TrimRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)));

        FfmpegCommand command = _builder.Build(job);

        AssertOrderedSequence(command.Arguments, "-c", "copy");
    }

    [Fact]
    public void Build_CompressWithNvenc_UsesTheSingleFixedAv1Profile()
    {
        EncodeJob job = CreateJob(
            @"D:\Видео\input.mp4",
            @"D:\Видео\input_compressed.tmp.mp4",
            @"D:\Видео\input_compressed.mp4",
            EncodeMode.Compress,
            "av1_nvenc");

        FfmpegCommand command = _builder.Build(job);

        AssertOrderedSequence(
            command.Arguments,
            "-c:v",
            "av1_nvenc",
            "-preset",
            "p3",
            "-rc",
            "vbr",
            "-cq",
            "35",
            "-b_ref_mode",
            "middle");
        AssertOrderedSequence(command.Arguments, "-c:a", "copy");
        Assert.Equal(@"D:\Видео\input_compressed.tmp.mp4", command.Arguments[^1]);
    }

    [Fact]
    public void Build_TrimAndCompressWithHevcNvenc_UsesTheFixedProfile()
    {
        EncodeJob job = CreateJob(
            @"D:\Видео\input.mkv",
            @"D:\Видео\input_trimmed_compressed.tmp.mkv",
            @"D:\Видео\input_trimmed_compressed.mkv",
            EncodeMode.TrimAndCompress,
            "hevc_nvenc",
            new TrimRange(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8.5)));

        FfmpegCommand command = _builder.Build(job);

        AssertOrderedSequence(command.Arguments, "-ss", "3", "-i", @"D:\Видео\input.mkv", "-t", "5.5");
        AssertOrderedSequence(
            command.Arguments,
            "-c:v", "hevc_nvenc",
            "-preset", "p5",
            "-tune", "hq",
            "-rc", "vbr",
            "-cq", "30",
            "-b:v", "0",
            "-multipass", "qres",
            "-spatial-aq", "1",
            "-temporal-aq", "1",
            "-b_ref_mode", "middle");
        Assert.Equal(1, command.Arguments.Count(argument => argument == "-i"));
        Assert.DoesNotContain("-tag:v", command.Arguments);
    }

    [Fact]
    public void Build_CompressWithSoftwareAv1Fallback_UsesPreset10Crf40()
    {
        EncodeJob job = CreateJob(
            @"D:\video\input.mkv",
            @"D:\video\input_compressed.tmp.mp4",
            @"D:\video\input_compressed.mp4",
            EncodeMode.Compress,
            "libsvtav1");

        FfmpegCommand command = _builder.Build(job);

        AssertOrderedSequence(command.Arguments, "-c:v", "libsvtav1", "-preset", "10", "-crf", "40");
        AssertOrderedSequence(command.Arguments, "-c:a", "aac", "-b:a", "128k");
        AssertOrderedSequence(command.Arguments, "-map_metadata", "0", "-map_chapters", "0");
    }

    [Fact]
    public void Build_Compression_MapsOnePrimaryVideoAndSkipsAttachedPictures()
    {
        EncodeJob job = CreateJob(
            @"D:\video\input.mp4",
            @"D:\video\input_compressed.tmp.mp4",
            @"D:\video\input_compressed.mp4",
            EncodeMode.Compress,
            "libsvtav1");

        FfmpegCommand command = _builder.Build(job);

        AssertOrderedSequence(command.Arguments, "-map", "0:V:0", "-map", "0:a:0?");
        List<string> mappedStreams = [];
        for (int index = 0; index < command.Arguments.Count - 1; index++)
        {
            if (command.Arguments[index] == "-map")
            {
                mappedStreams.Add(command.Arguments[index + 1]);
            }
        }

        Assert.Equal(["0:V:0", "0:a:0?"], mappedStreams);
        Assert.DoesNotContain("0:v:0", command.Arguments);
    }

    [Theory]
    [InlineData("av1_qsv", "-global_quality", "35")]
    [InlineData("av1_amf", "-qvbr_quality_level", "35")]
    public void Build_HardwareAv1FallbacksUseTheSameFixedQuality(
        string encoderName,
        string qualityOption,
        string qualityValue)
    {
        EncodeJob job = CreateJob(
            @"D:\video\input.mp4",
            @"D:\video\input_compressed.tmp.mp4",
            @"D:\video\input_compressed.mp4",
            EncodeMode.Compress,
            encoderName);

        FfmpegCommand command = _builder.Build(job);

        AssertOrderedSequence(command.Arguments, "-c:v", encoderName);
        AssertOrderedSequence(command.Arguments, qualityOption, qualityValue);
    }

    [Theory]
    [InlineData("hevc_qsv", "-preset", "medium", "-global_quality", "30")]
    [InlineData("hevc_amf", "-quality", "quality", "-qvbr_quality_level", "30")]
    public void Build_HardwareHevcFallbacksUseVendorSpecificFixedQuality(
        string encoderName,
        string presetOption,
        string presetValue,
        string qualityOption,
        string qualityValue)
    {
        EncodeJob job = CreateJob(
            @"D:\video\input.mkv",
            @"D:\video\input_compressed.tmp.mkv",
            @"D:\video\input_compressed.mkv",
            EncodeMode.Compress,
            encoderName);

        FfmpegCommand command = _builder.Build(job);

        AssertOrderedSequence(command.Arguments, "-c:v", encoderName);
        AssertOrderedSequence(command.Arguments, presetOption, presetValue);
        AssertOrderedSequence(command.Arguments, qualityOption, qualityValue);
        Assert.DoesNotContain("-tag:v", command.Arguments);
    }

    [Theory]
    [InlineData(@"D:\video\output.tmp.mp4", @"D:\video\output.mp4")]
    [InlineData(@"D:\video\output.tmp.mov", @"D:\video\output.mov")]
    public void Build_HevcMp4FamilyOutput_AddsHvc1Tag(
        string temporaryPath,
        string finalPath)
    {
        EncodeJob job = CreateJob(
            @"D:\video\input.mkv",
            temporaryPath,
            finalPath,
            EncodeMode.Compress,
            "hevc_nvenc");

        FfmpegCommand command = _builder.Build(job);

        AssertOrderedSequence(command.Arguments, "-tag:v", "hvc1");
    }

    [Fact]
    public void Build_DifferentContainer_UsesAutomaticAacFallback()
    {
        EncodeJob job = CreateJob(
            @"D:\video\input.mkv",
            @"D:\video\output.tmp.mp4",
            @"D:\video\output.mp4",
            EncodeMode.Compress,
            "libsvtav1");

        FfmpegCommand command = _builder.Build(job);

        AssertOrderedSequence(command.Arguments, "-c:a", "aac", "-b:a", "128k");
    }

    [Fact]
    public void Build_RejectsSourceOverwrite()
    {
        EncodeJob job = CreateJob(
            @"D:\video\input.mp4",
            @"D:\video\input_compressed.tmp.mp4",
            @"D:\video\input.mp4",
            EncodeMode.Compress,
            "libsvtav1");

        Assert.Throws<InvalidOperationException>(() => _builder.Build(job));
    }

    [Fact]
    public void Build_RejectsUnavailableEncoderForCompression()
    {
        EncodeJob job = CreateJob(
            @"D:\video\input.mp4",
            @"D:\video\input_compressed.tmp.mp4",
            @"D:\video\input_compressed.mp4",
            EncodeMode.Compress,
            "hevc_nvenc",
            encoderAvailable: false);

        Assert.Throws<InvalidOperationException>(() => _builder.Build(job));
    }

    [Theory]
    [InlineData("libx264")]
    [InlineData("unsupported_encoder")]
    public void Build_RejectsEncoderOutsideAutomaticFallbackChain(string encoderName)
    {
        EncodeJob job = CreateJob(
            @"D:\video\input.mp4",
            @"D:\video\input_compressed.tmp.mp4",
            @"D:\video\input_compressed.mp4",
            EncodeMode.Compress,
            encoderName);

        Assert.Throws<NotSupportedException>(() => _builder.Build(job));
    }

    private static EncodeJob CreateJob(
        string inputPath,
        string temporaryPath,
        string finalPath,
        EncodeMode mode,
        string encoderName,
        TrimRange? trimRange = null,
        bool encoderAvailable = true)
    {
        return new EncodeJob(
            inputPath,
            temporaryPath,
            finalPath,
            mode,
            new EncoderCapability(encoderName, encoderAvailable, false, null),
            trimRange);
    }

    private static void AssertOrderedSequence(
        IReadOnlyList<string> arguments,
        params string[] expected)
    {
        for (int startIndex = 0; startIndex <= arguments.Count - expected.Length; startIndex++)
        {
            if (expected.SequenceEqual(arguments.Skip(startIndex).Take(expected.Length)))
            {
                return;
            }
        }

        Assert.Fail($"Expected sequence was not found: {string.Join(" ", expected)}");
    }

    private sealed class StubToolResolver : IBundledToolResolver
    {
        public BundledToolPaths Resolve()
        {
            return new BundledToolPaths(FfmpegPath, "ffprobe.exe");
        }
    }
}
