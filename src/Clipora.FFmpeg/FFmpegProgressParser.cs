using System.Globalization;
using Clipora.Core.Models;

namespace Clipora.FFmpeg;

internal sealed class FFmpegProgressParser
{
    private readonly TimeSpan _sourceDuration;

    private long _frame;
    private double _framesPerSecond;
    private long? _bitrateBitsPerSecond;
    private long _totalSizeBytes;
    private TimeSpan _outputTime;
    private double _speed;

    public FFmpegProgressParser(TimeSpan sourceDuration)
    {
        if (sourceDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDuration),
                sourceDuration,
                "Source duration must be greater than zero.");
        }

        _sourceDuration = sourceDuration;
    }

    public EncodeProgress? ParseLine(string? line, TimeSpan elapsed)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        int separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            return null;
        }

        string key = line[..separatorIndex].Trim();
        string value = line[(separatorIndex + 1)..].Trim();

        switch (key)
        {
            case "frame":
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long frame))
                {
                    _frame = Math.Max(0, frame);
                }

                break;

            case "fps":
                _framesPerSecond = ParseNonNegativeDouble(value);
                break;

            case "bitrate":
                _bitrateBitsPerSecond = ParseBitrate(value);
                break;

            case "total_size":
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long size))
                {
                    _totalSizeBytes = Math.Max(0, size);
                }

                break;

            case "out_time":
            {
                if (TryParseOutputTime(value, out TimeSpan outputTime))
                {
                    _outputTime = outputTime;
                }

                break;
            }

            case "out_time_us":
            case "out_time_ms":
            {
                if (TryParseOutputTimeMicroseconds(value, out TimeSpan outputTime))
                {
                    _outputTime = outputTime;
                }

                break;
            }

            case "speed":
                _speed = ParseSpeed(value);
                break;

            case "progress":
                return CreateProgress(
                    elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed,
                    value.Equals("end", StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private EncodeProgress CreateProgress(TimeSpan elapsed, bool isComplete)
    {
        double durationRatio = _outputTime.TotalSeconds / _sourceDuration.TotalSeconds;
        double boundedRatio = Math.Clamp(durationRatio, 0d, 1d);
        double percent = isComplete ? 100d : boundedRatio * 100d;

        TimeSpan? estimatedRemaining = CalculateEstimatedRemaining(
            elapsed,
            boundedRatio,
            isComplete);
        long? estimatedFinalSize = CalculateEstimatedFinalSize(boundedRatio, isComplete);

        return new EncodeProgress(
            percent,
            _frame,
            _framesPerSecond,
            _bitrateBitsPerSecond,
            _totalSizeBytes,
            _outputTime,
            _speed,
            elapsed,
            estimatedRemaining,
            estimatedFinalSize,
            isComplete);
    }

    private TimeSpan? CalculateEstimatedRemaining(
        TimeSpan elapsed,
        double durationRatio,
        bool isComplete)
    {
        if (isComplete)
        {
            return TimeSpan.Zero;
        }

        if (_speed > 0d)
        {
            double remainingSeconds = Math.Max(
                0d,
                (_sourceDuration - _outputTime).TotalSeconds / _speed);

            return FromSecondsBounded(remainingSeconds);
        }

        if (durationRatio <= 0d || elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        double estimatedSeconds = elapsed.TotalSeconds * (1d - durationRatio) / durationRatio;
        return FromSecondsBounded(estimatedSeconds);
    }

    private long? CalculateEstimatedFinalSize(double durationRatio, bool isComplete)
    {
        if (_totalSizeBytes <= 0)
        {
            return null;
        }

        if (isComplete || durationRatio >= 1d)
        {
            return _totalSizeBytes;
        }

        if (durationRatio <= 0d)
        {
            return null;
        }

        double estimatedSize = Math.Ceiling(_totalSizeBytes / durationRatio);
        return estimatedSize >= long.MaxValue ? long.MaxValue : (long)estimatedSize;
    }

    private static double ParseNonNegativeDouble(string value)
    {
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed) ||
            !double.IsFinite(parsed))
        {
            return 0d;
        }

        return Math.Max(0d, parsed);
    }

    private static double ParseSpeed(string value)
    {
        string normalized = value.EndsWith('x') ? value[..^1] : value;
        return ParseNonNegativeDouble(normalized);
    }

    private static long? ParseBitrate(string value)
    {
        const string bitsSuffix = "bits/s";
        const string kilobitsSuffix = "kbits/s";
        const string megabitsSuffix = "Mbits/s";

        double multiplier;
        string numericPart;

        if (value.EndsWith(kilobitsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1_000d;
            numericPart = value[..^kilobitsSuffix.Length];
        }
        else if (value.EndsWith(megabitsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1_000_000d;
            numericPart = value[..^megabitsSuffix.Length];
        }
        else if (value.EndsWith(bitsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1d;
            numericPart = value[..^bitsSuffix.Length];
        }
        else
        {
            return null;
        }

        if (!double.TryParse(
                numericPart.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double bitrate) ||
            !double.IsFinite(bitrate) ||
            bitrate < 0d)
        {
            return null;
        }

        double bitsPerSecond = bitrate * multiplier;
        return bitsPerSecond >= long.MaxValue
            ? long.MaxValue
            : (long)Math.Round(bitsPerSecond, MidpointRounding.AwayFromZero);
    }

    private static bool TryParseOutputTime(string value, out TimeSpan outputTime)
    {
        outputTime = TimeSpan.Zero;

        ReadOnlySpan<char> valueSpan = value.AsSpan().Trim();
        bool isNegative = valueSpan.StartsWith('-');
        if (isNegative)
        {
            valueSpan = valueSpan[1..];
        }

        int firstSeparatorIndex = valueSpan.IndexOf(':');
        if (firstSeparatorIndex <= 0)
        {
            return false;
        }

        int secondSeparatorOffset = valueSpan[(firstSeparatorIndex + 1)..].IndexOf(':');
        if (secondSeparatorOffset <= 0)
        {
            return false;
        }

        int secondSeparatorIndex = firstSeparatorIndex + secondSeparatorOffset + 1;
        ReadOnlySpan<char> hoursSpan = valueSpan[..firstSeparatorIndex];
        ReadOnlySpan<char> minutesSpan = valueSpan[(firstSeparatorIndex + 1)..secondSeparatorIndex];
        ReadOnlySpan<char> secondsSpan = valueSpan[(secondSeparatorIndex + 1)..];

        if (secondsSpan.Contains(':') ||
            !long.TryParse(
                hoursSpan,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long hours) ||
            !int.TryParse(
                minutesSpan,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int minutes) ||
            !decimal.TryParse(
                secondsSpan,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out decimal seconds) ||
            minutes is < 0 or > 59 ||
            seconds is < 0m or >= 60m ||
            hours > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerHour)
        {
            return false;
        }

        decimal totalTicks =
            (hours * (decimal)TimeSpan.TicksPerHour) +
            (minutes * (decimal)TimeSpan.TicksPerMinute) +
            (seconds * TimeSpan.TicksPerSecond);

        if (totalTicks > TimeSpan.MaxValue.Ticks)
        {
            return false;
        }

        if (!isNegative)
        {
            outputTime = TimeSpan.FromTicks(decimal.ToInt64(decimal.Truncate(totalTicks)));
        }

        return true;
    }

    private static bool TryParseOutputTimeMicroseconds(string value, out TimeSpan outputTime)
    {
        outputTime = TimeSpan.Zero;

        if (!long.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long microseconds) ||
            microseconds > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMicrosecond)
        {
            return false;
        }

        outputTime = TimeSpan.FromTicks(microseconds * TimeSpan.TicksPerMicrosecond);
        return true;
    }

    private static TimeSpan? FromSecondsBounded(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0d)
        {
            return null;
        }

        if (seconds >= TimeSpan.MaxValue.TotalSeconds)
        {
            return TimeSpan.MaxValue;
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
