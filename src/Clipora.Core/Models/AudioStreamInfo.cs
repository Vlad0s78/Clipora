namespace Clipora.Core.Models;

public sealed record AudioStreamInfo(
    int Index,
    string Codec,
    long? BitrateBitsPerSecond,
    int Channels,
    int? SampleRate,
    string? Language,
    bool IsDefault);
