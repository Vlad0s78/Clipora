namespace Clipora.Core.Models;

public sealed record SubtitleStreamInfo(
    int Index,
    string Codec,
    string? Language,
    bool IsDefault,
    bool IsForced);
