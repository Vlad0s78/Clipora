namespace Clipora.Core;

public static class CompressionProfile
{
    public const string NvidiaAv1Encoder = "av1_nvenc";

    public const string IntelAv1Encoder = "av1_qsv";

    public const string AmdAv1Encoder = "av1_amf";

    public const string NvidiaHevcEncoder = "hevc_nvenc";

    public const string IntelHevcEncoder = "hevc_qsv";

    public const string AmdHevcEncoder = "hevc_amf";

    public const string SoftwareAv1FallbackEncoder = "libsvtav1";

    public const string NvidiaAv1Preset = "p3";

    public const string IntelAv1Preset = "medium";

    public const string AmdAv1QualityPreset = "balanced";

    public const string NvidiaHevcPreset = "p5";

    public const string NvidiaHevcTune = "hq";

    public const string IntelHevcPreset = "medium";

    public const string AmdHevcQualityPreset = "quality";

    public const string SoftwareAv1FallbackPreset = "10";

    public const int Av1Quality = 35;

    public const int HevcQuality = 30;

    public const int SoftwareAv1FallbackCrf = 40;
}
