using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clipora.Core.Models;

namespace Clipora.FFmpeg;

internal static partial class FfprobeJsonParser
{
    private static readonly string[] HdrTransfers = ["smpte2084", "arib-std-b67"];

    public static VideoFileInfo Parse(string json, string inputPath, long fallbackFileSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentOutOfRangeException.ThrowIfNegative(fallbackFileSize);

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("ffprobe JSON root must be an object.");
            }

            if (!TryGetProperty(root, "format", out JsonElement format) ||
                format.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("ffprobe JSON does not contain a format object.");
            }

            if (!TryGetProperty(root, "streams", out JsonElement streams) ||
                streams.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("ffprobe JSON does not contain a streams array.");
            }

            List<VideoStreamInfo> videoStreams = [];
            List<AudioStreamInfo> audioStreams = [];
            List<SubtitleStreamInfo> subtitleStreams = [];

            foreach (JsonElement stream in streams.EnumerateArray())
            {
                if (stream.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? codecType = ReadString(stream, "codec_type");
                switch (codecType?.ToLowerInvariant())
                {
                    case "video":
                        videoStreams.Add(ParseVideoStream(stream));
                        break;
                    case "audio":
                        audioStreams.Add(ParseAudioStream(stream));
                        break;
                    case "subtitle":
                        subtitleStreams.Add(ParseSubtitleStream(stream));
                        break;
                }
            }

            if (videoStreams.Count == 0)
            {
                throw new InvalidDataException("ffprobe JSON does not contain a video stream.");
            }

            TimeSpan duration = ReadDuration(format, streams);
            long fileSize = ReadPositiveLong(format, "size") ?? fallbackFileSize;
            long? bitrate = ReadPositiveLong(format, "bit_rate") ?? CalculateBitrate(fileSize, duration);

            VideoFileInfo result = new(
                inputPath,
                duration,
                fileSize,
                bitrate,
                videoStreams.AsReadOnly(),
                audioStreams.AsReadOnly(),
                subtitleStreams.AsReadOnly(),
                ReadTags(format),
                null);

            return result with { RotationDegrees = result.PrimaryVideoStream?.RotationDegrees };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("ffprobe returned malformed JSON.", exception);
        }
    }

    private static VideoStreamInfo ParseVideoStream(JsonElement stream)
    {
        string? colorSpace = ReadString(stream, "color_space");
        string? colorTransfer = ReadString(stream, "color_transfer");
        string? colorPrimaries = ReadString(stream, "color_primaries");

        return new VideoStreamInfo(
            ReadInt32(stream, "index") ?? 0,
            ReadString(stream, "codec_name") ?? "unknown",
            ReadString(stream, "profile"),
            ReadInt32(stream, "width") ?? 0,
            ReadInt32(stream, "height") ?? 0,
            ReadFrameRate(stream),
            ReadPositiveLong(stream, "bit_rate"),
            ReadString(stream, "pix_fmt"),
            colorSpace,
            colorTransfer,
            colorPrimaries,
            ReadString(stream, "color_range"),
            IsHdr(stream, colorTransfer),
            ReadDisposition(stream, "default"),
            ReadDisposition(stream, "attached_pic"),
            ReadRotation(stream));
    }

    private static AudioStreamInfo ParseAudioStream(JsonElement stream)
    {
        return new AudioStreamInfo(
            ReadInt32(stream, "index") ?? 0,
            ReadString(stream, "codec_name") ?? "unknown",
            ReadPositiveLong(stream, "bit_rate"),
            ReadInt32(stream, "channels") ?? 0,
            ReadPositiveInt32(stream, "sample_rate"),
            ReadTag(stream, "language"),
            ReadDisposition(stream, "default"));
    }

    private static SubtitleStreamInfo ParseSubtitleStream(JsonElement stream)
    {
        return new SubtitleStreamInfo(
            ReadInt32(stream, "index") ?? 0,
            ReadString(stream, "codec_name") ?? "unknown",
            ReadTag(stream, "language"),
            ReadDisposition(stream, "default"),
            ReadDisposition(stream, "forced"));
    }

    private static TimeSpan ReadDuration(JsonElement format, JsonElement streams)
    {
        double? seconds = ReadPositiveDouble(format, "duration");
        if (seconds is null)
        {
            foreach (JsonElement stream in streams.EnumerateArray())
            {
                double? streamDuration = ReadPositiveDouble(stream, "duration") ??
                    ReadDurationFromTimeBase(stream);

                if (streamDuration is not null && (seconds is null || streamDuration > seconds))
                {
                    seconds = streamDuration;
                }
            }
        }

        if (seconds is null || seconds > TimeSpan.MaxValue.TotalSeconds)
        {
            throw new InvalidDataException("ffprobe JSON does not contain a valid duration.");
        }

        return TimeSpan.FromSeconds(seconds.Value);
    }

    private static double? ReadDurationFromTimeBase(JsonElement stream)
    {
        long? durationTimestamp = ReadInt64(stream, "duration_ts");
        double? timeBase = ReadRational(stream, "time_base");
        if (durationTimestamp is null || timeBase is null)
        {
            return null;
        }

        double duration = durationTimestamp.Value * timeBase.Value;
        return double.IsFinite(duration) && duration > 0 ? duration : null;
    }

    private static double ReadFrameRate(JsonElement stream)
    {
        return ReadPositiveRational(stream, "avg_frame_rate") ??
            ReadPositiveRational(stream, "r_frame_rate") ??
            0;
    }

    private static bool IsHdr(JsonElement stream, string? colorTransfer)
    {
        if (colorTransfer is not null &&
            HdrTransfers.Contains(colorTransfer, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryGetProperty(stream, "side_data_list", out JsonElement sideDataList) ||
            sideDataList.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement sideData in sideDataList.EnumerateArray())
        {
            string? type = ReadString(sideData, "side_data_type");
            if (type is not null &&
                (type.Contains("Mastering display", StringComparison.OrdinalIgnoreCase) ||
                 type.Contains("Content light level", StringComparison.OrdinalIgnoreCase) ||
                 type.Contains("HDR", StringComparison.OrdinalIgnoreCase) ||
                 type.Contains("DOVI", StringComparison.OrdinalIgnoreCase) ||
                 type.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static int? ReadRotation(JsonElement stream)
    {
        if (TryGetProperty(stream, "side_data_list", out JsonElement sideDataList) &&
            sideDataList.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement sideData in sideDataList.EnumerateArray())
            {
                double? rotation = ReadDouble(sideData, "rotation");
                if (rotation is not null)
                {
                    return RoundRotation(rotation.Value);
                }

                string? displayMatrix = ReadString(sideData, "displaymatrix");
                Match match = RotationRegex().Match(displayMatrix ?? string.Empty);
                if (match.Success &&
                    double.TryParse(
                        match.Groups[1].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double matrixRotation))
                {
                    return RoundRotation(matrixRotation);
                }
            }
        }

        if (TryGetProperty(stream, "tags", out JsonElement tags))
        {
            double? tagRotation = ReadDouble(tags, "rotate");
            if (tagRotation is not null)
            {
                return RoundRotation(tagRotation.Value);
            }
        }

        return null;
    }

    private static int? RoundRotation(double rotation)
    {
        if (!double.IsFinite(rotation) || rotation < int.MinValue || rotation > int.MaxValue)
        {
            return null;
        }

        return (int)Math.Round(rotation, MidpointRounding.AwayFromZero);
    }

    private static string? ReadTag(JsonElement stream, string name)
    {
        return TryGetProperty(stream, "tags", out JsonElement tags)
            ? ReadString(tags, name)
            : null;
    }

    private static IReadOnlyDictionary<string, string> ReadTags(JsonElement format)
    {
        Dictionary<string, string> tags = new(StringComparer.OrdinalIgnoreCase);
        if (!TryGetProperty(format, "tags", out JsonElement tagsElement) ||
            tagsElement.ValueKind != JsonValueKind.Object)
        {
            return new ReadOnlyDictionary<string, string>(tags);
        }

        foreach (JsonProperty property in tagsElement.EnumerateObject())
        {
            string? value = ConvertToString(property.Value);
            if (value is not null)
            {
                tags[property.Name] = value;
            }
        }

        return new ReadOnlyDictionary<string, string>(tags);
    }

    private static bool ReadDisposition(JsonElement stream, string name)
    {
        if (!TryGetProperty(stream, "disposition", out JsonElement disposition) ||
            !TryGetProperty(disposition, name, out JsonElement value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt64(out long number) => number != 0,
            JsonValueKind.String when long.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long number) => number != 0,
            _ => false,
        };
    }

    private static long? CalculateBitrate(long size, TimeSpan duration)
    {
        if (size <= 0 || duration <= TimeSpan.Zero)
        {
            return null;
        }

        double bitrate = size * 8d / duration.TotalSeconds;
        return double.IsFinite(bitrate) && bitrate <= long.MaxValue
            ? (long)Math.Round(bitrate, MidpointRounding.AwayFromZero)
            : null;
    }

    private static int? ReadPositiveInt32(JsonElement element, string name)
    {
        int? value = ReadInt32(element, name);
        return value > 0 ? value : null;
    }

    private static long? ReadPositiveLong(JsonElement element, string name)
    {
        long? value = ReadInt64(element, name);
        return value > 0 ? value : null;
    }

    private static double? ReadPositiveDouble(JsonElement element, string name)
    {
        double? value = ReadDouble(element, name);
        return value is > 0 && double.IsFinite(value.Value) ? value : null;
    }

    private static double? ReadPositiveRational(JsonElement element, string name)
    {
        double? value = ReadRational(element, name);
        return value is > 0 && double.IsFinite(value.Value) ? value : null;
    }

    private static int? ReadInt32(JsonElement element, string name)
    {
        long? value = ReadInt64(element, name);
        return value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;
    }

    private static long? ReadInt64(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
            long.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out number)
            ? number
            : null;
    }

    private static double? ReadDouble(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
            double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number)
            ? number
            : null;
    }

    private static double? ReadRational(JsonElement element, string name)
    {
        string? value = ReadString(element, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
        {
            return double.TryParse(
                parts[0],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double number)
                ? number
                : null;
        }

        if (!double.TryParse(
                parts[0],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double numerator) ||
            !double.TryParse(
                parts[1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double denominator) ||
            denominator == 0)
        {
            return null;
        }

        return numerator / denominator;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        return TryGetProperty(element, name, out JsonElement value)
            ? ConvertToString(value)
            : null;
    }

    private static string? ConvertToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null,
        };
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    [GeneratedRegex(@"rotation of\s+(-?\d+(?:\.\d+)?)\s+degrees", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RotationRegex();
}
