using Clipora.Core;

namespace Clipora.FFmpeg.Tests;

public sealed class CompressionProfileTests
{
    [Fact]
    public void Profile_HasOneFixedQualityConfigurationForAutomaticFallbacks()
    {
        Assert.Equal("av1_nvenc", CompressionProfile.NvidiaAv1Encoder);
        Assert.Equal("av1_qsv", CompressionProfile.IntelAv1Encoder);
        Assert.Equal("av1_amf", CompressionProfile.AmdAv1Encoder);
        Assert.Equal("hevc_nvenc", CompressionProfile.NvidiaHevcEncoder);
        Assert.Equal("hevc_qsv", CompressionProfile.IntelHevcEncoder);
        Assert.Equal("hevc_amf", CompressionProfile.AmdHevcEncoder);
        Assert.Equal("libsvtav1", CompressionProfile.SoftwareAv1FallbackEncoder);
        Assert.Equal("p3", CompressionProfile.NvidiaAv1Preset);
        Assert.Equal("medium", CompressionProfile.IntelAv1Preset);
        Assert.Equal("balanced", CompressionProfile.AmdAv1QualityPreset);
        Assert.Equal("p5", CompressionProfile.NvidiaHevcPreset);
        Assert.Equal("hq", CompressionProfile.NvidiaHevcTune);
        Assert.Equal("medium", CompressionProfile.IntelHevcPreset);
        Assert.Equal("quality", CompressionProfile.AmdHevcQualityPreset);
        Assert.Equal("10", CompressionProfile.SoftwareAv1FallbackPreset);
        Assert.Equal(35, CompressionProfile.Av1Quality);
        Assert.Equal(30, CompressionProfile.HevcQuality);
        Assert.Equal(40, CompressionProfile.SoftwareAv1FallbackCrf);
    }
}
