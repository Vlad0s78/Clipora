namespace Clipora.Core.Models;

public sealed record EncoderCapability(
    string EncoderName,
    bool IsAvailable,
    bool IsHardwareAccelerated,
    string? Diagnostic);
