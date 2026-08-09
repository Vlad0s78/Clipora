namespace Clipora.Core.Models;

public static class TrimSelectionMath
{
    public const double MinimumSelectionSeconds = 0.1d;

    public static (double Start, double End) Normalize(double start, double end, double duration)
    {
        duration = NormalizeDuration(duration);
        start = ClampPosition(start, duration);
        end = ClampPosition(end, duration);

        if (end < start)
        {
            (start, end) = (end, start);
        }

        double minimum = Math.Min(MinimumSelectionSeconds, duration);
        if (end - start >= minimum)
        {
            return (start, end);
        }

        end = Math.Min(duration, start + minimum);
        start = Math.Max(0d, end - minimum);
        return (start, end);
    }

    public static double ClampStart(double value, double end, double duration)
    {
        duration = NormalizeDuration(duration);
        end = ClampPosition(end, duration);
        double minimum = Math.Min(MinimumSelectionSeconds, duration);
        return Math.Clamp(NormalizeValue(value), 0d, Math.Max(0d, end - minimum));
    }

    public static double ClampEnd(double value, double start, double duration)
    {
        duration = NormalizeDuration(duration);
        start = ClampPosition(start, duration);
        double minimum = Math.Min(MinimumSelectionSeconds, duration);
        return Math.Clamp(NormalizeValue(value), Math.Min(duration, start + minimum), duration);
    }

    public static double ClampPosition(double value, double duration)
    {
        duration = NormalizeDuration(duration);
        return Math.Clamp(NormalizeValue(value), 0d, duration);
    }

    public static double ClampPositionToSelection(double value, double start, double end, double duration)
    {
        (start, end) = Normalize(start, end, duration);
        return Math.Clamp(ClampPosition(value, duration), start, end);
    }

    public static string FormatTimestamp(TimeSpan timestamp)
    {
        timestamp = timestamp < TimeSpan.Zero ? TimeSpan.Zero : timestamp;
        long hours = (long)timestamp.TotalHours;
        return hours > 0
            ? $"{hours:00}:{timestamp.Minutes:00}:{timestamp.Seconds:00}.{timestamp.Milliseconds:000}"
            : $"{timestamp.Minutes:00}:{timestamp.Seconds:00}.{timestamp.Milliseconds:000}";
    }

    public static string FormatRulerLabel(double seconds)
    {
        TimeSpan timestamp = TimeSpan.FromSeconds(Math.Max(0d, NormalizeValue(seconds)));
        long hours = (long)timestamp.TotalHours;
        return hours > 0
            ? $"{hours:00}:{timestamp.Minutes:00}:{timestamp.Seconds:00}"
            : $"{timestamp.Minutes:00}:{timestamp.Seconds:00}";
    }

    private static double NormalizeDuration(double duration)
    {
        return double.IsFinite(duration) && duration > 0d ? duration : 0d;
    }

    private static double NormalizeValue(double value)
    {
        return double.IsFinite(value) ? value : 0d;
    }
}
