namespace Clipora.Core.Models;

public sealed record EncodeProgress(
    double Percent,
    long Frame,
    double FramesPerSecond,
    long? BitrateBitsPerSecond,
    long TotalSizeBytes,
    TimeSpan OutputTime,
    double Speed,
    TimeSpan Elapsed,
    TimeSpan? EstimatedRemaining,
    long? EstimatedFinalSizeBytes,
    bool IsComplete);
