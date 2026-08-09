namespace Clipora.Core.Models;

public sealed record VideoStreamInfo(
    int Index,
    string Codec,
    string? Profile,
    int Width,
    int Height,
    double FramesPerSecond,
    long? BitrateBitsPerSecond,
    string? PixelFormat,
    string? ColorSpace,
    string? ColorTransfer,
    string? ColorPrimaries,
    string? ColorRange,
    bool IsHdr,
    bool IsDefault,
    bool IsAttachedPicture,
    int? RotationDegrees)
{
    public int DisplayWidth => HasQuarterTurnRotation ? Height : Width;

    public int DisplayHeight => HasQuarterTurnRotation ? Width : Height;

    private bool HasQuarterTurnRotation =>
        RotationDegrees is int rotation && Math.Abs(rotation % 180) == 90;
}
