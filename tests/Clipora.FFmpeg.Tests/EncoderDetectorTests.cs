using System.Diagnostics;
using Clipora.Core;
using Clipora.Core.Interfaces;
using Clipora.Core.Models;
using Clipora.Core.Tools;

namespace Clipora.FFmpeg.Tests;

public sealed class EncoderDetectorTests
{
    [Fact]
    public void PreferredEncoderNames_FollowTheSingleProfileFallbackOrder()
    {
        Assert.Equal(
            [
                CompressionProfile.NvidiaAv1Encoder,
                CompressionProfile.IntelAv1Encoder,
                CompressionProfile.AmdAv1Encoder,
                CompressionProfile.NvidiaHevcEncoder,
                CompressionProfile.IntelHevcEncoder,
                CompressionProfile.AmdHevcEncoder,
                CompressionProfile.SoftwareAv1FallbackEncoder,
            ],
            EncoderDetector.PreferredEncoderNames);
    }

    [Theory]
    [InlineData("av1_nvenc")]
    [InlineData("av1_qsv")]
    [InlineData("av1_amf")]
    [InlineData("hevc_nvenc")]
    [InlineData("hevc_qsv")]
    [InlineData("hevc_amf")]
    [InlineData("libsvtav1")]
    public void CreateStartInfo_UsesDirectHiddenRealEncodeProbe(string encoderName)
    {
        const string ffmpegPath = @"C:\Program Files\Clipora\Tools\ffmpeg\ffmpeg.exe";

        ProcessStartInfo startInfo = EncoderDetector.CreateStartInfo(ffmpegPath, encoderName);

        Assert.Equal(ffmpegPath, startInfo.FileName);
        Assert.Equal(@"C:\Program Files\Clipora\Tools\ffmpeg", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Contains("lavfi", startInfo.ArgumentList);
        Assert.Contains("color=c=black:s=640x360:r=24:d=0.25", startInfo.ArgumentList);
        Assert.Contains(encoderName, startInfo.ArgumentList);
        Assert.Equal("-", startInfo.ArgumentList[^1]);
    }

    [Fact]
    public void CreateStartInfo_NvencProbeUsesTheFixedProfile()
    {
        ProcessStartInfo startInfo = EncoderDetector.CreateStartInfo("ffmpeg.exe", "av1_nvenc");

        AssertContainsOrderedSubsequence(
            startInfo.ArgumentList,
            "-c:v", "av1_nvenc",
            "-preset", "p3",
            "-rc", "vbr",
            "-cq", "35",
            "-b_ref_mode", "middle");
    }

    [Fact]
    public void CreateStartInfo_QsvProbeUsesTheFixedProfile()
    {
        ProcessStartInfo startInfo = EncoderDetector.CreateStartInfo("ffmpeg.exe", "av1_qsv");

        AssertContainsOrderedSubsequence(
            startInfo.ArgumentList,
            "-c:v", "av1_qsv",
            "-preset", "medium",
            "-global_quality", "35");
    }

    [Fact]
    public void CreateStartInfo_AmfProbeUsesTheFixedProfile()
    {
        ProcessStartInfo startInfo = EncoderDetector.CreateStartInfo("ffmpeg.exe", "av1_amf");

        AssertContainsOrderedSubsequence(
            startInfo.ArgumentList,
            "-c:v", "av1_amf",
            "-quality", "balanced",
            "-rc", "qvbr",
            "-qvbr_quality_level", "35");
    }

    [Fact]
    public void CreateStartInfo_HevcNvencProbeUsesTheFixedProfile()
    {
        ProcessStartInfo startInfo = EncoderDetector.CreateStartInfo("ffmpeg.exe", "hevc_nvenc");

        AssertContainsOrderedSubsequence(
            startInfo.ArgumentList,
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
    }

    [Fact]
    public void CreateStartInfo_HevcQsvProbeUsesTheFixedProfile()
    {
        ProcessStartInfo startInfo = EncoderDetector.CreateStartInfo("ffmpeg.exe", "hevc_qsv");

        AssertContainsOrderedSubsequence(
            startInfo.ArgumentList,
            "-c:v", "hevc_qsv",
            "-preset", "medium",
            "-global_quality", "30");
    }

    [Fact]
    public void CreateStartInfo_HevcAmfProbeUsesTheFixedProfile()
    {
        ProcessStartInfo startInfo = EncoderDetector.CreateStartInfo("ffmpeg.exe", "hevc_amf");

        AssertContainsOrderedSubsequence(
            startInfo.ArgumentList,
            "-c:v", "hevc_amf",
            "-quality", "quality",
            "-rc", "qvbr",
            "-qvbr_quality_level", "30");
    }

    [Fact]
    public void CreateStartInfo_SoftwareAv1FallbackUsesPreset10Crf40()
    {
        ProcessStartInfo startInfo = EncoderDetector.CreateStartInfo("ffmpeg.exe", "libsvtav1");

        AssertContainsOrderedSubsequence(
            startInfo.ArgumentList,
            "-c:v", "libsvtav1",
            "-preset", "10",
            "-crf", "40");
    }

    [Fact]
    public async Task DetectAsync_DoesNotCacheAResultWithoutAnyAvailableEncoder()
    {
        CountingToolResolver resolver = new(Path.Combine(
            Path.GetTempPath(),
            $"clipora-missing-ffmpeg-{Guid.NewGuid():N}.exe"));
        EncoderDetector detector = new(resolver);

        IReadOnlyList<EncoderCapability> first = await detector.DetectAsync(CancellationToken.None);
        IReadOnlyList<EncoderCapability> second = await detector.DetectAsync(CancellationToken.None);

        Assert.DoesNotContain(first, capability => capability.IsAvailable);
        Assert.DoesNotContain(second, capability => capability.IsAvailable);
        Assert.Equal(2, resolver.CallCount);
    }

    private sealed class CountingToolResolver : IBundledToolResolver
    {
        private readonly string _ffmpegPath;

        public CountingToolResolver(string ffmpegPath)
        {
            _ffmpegPath = ffmpegPath;
        }

        public int CallCount { get; private set; }

        public BundledToolPaths Resolve()
        {
            CallCount++;
            return new BundledToolPaths(_ffmpegPath, "ffprobe.exe");
        }
    }

    private static void AssertContainsOrderedSubsequence(
        IReadOnlyList<string> actual,
        params string[] expected)
    {
        int actualIndex = 0;

        foreach (string expectedValue in expected)
        {
            while (actualIndex < actual.Count && actual[actualIndex] != expectedValue)
            {
                actualIndex++;
            }

            Assert.True(actualIndex < actual.Count, $"Argument '{expectedValue}' was not found in order.");
            actualIndex++;
        }
    }
}
