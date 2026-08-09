using System.Text.Json;

namespace Clipora.FFmpeg.Tests;

public sealed class FfprobeJsonParserTests
{
    [Fact]
    public void Parse_MapsVideoAudioSubtitleRotationAndHdr()
    {
        const string json = """
            {
              "streams": [
                {
                  "index": 0,
                  "codec_name": "h264",
                  "profile": "High",
                  "codec_type": "video",
                  "width": 1280,
                  "height": 1024,
                  "pix_fmt": "yuv420p10le",
                  "color_space": "bt2020nc",
                  "color_transfer": "smpte2084",
                  "color_primaries": "bt2020",
                  "color_range": "tv",
                  "avg_frame_rate": "120/1",
                  "r_frame_rate": "120/1",
                  "bit_rate": "2048000",
                  "disposition": { "default": 1, "attached_pic": 0 },
                  "side_data_list": [
                    { "side_data_type": "Mastering display metadata" },
                    { "side_data_type": "Display Matrix", "rotation": -90 }
                  ]
                },
                {
                  "index": 1,
                  "codec_name": "aac",
                  "codec_type": "audio",
                  "sample_rate": "48000",
                  "channels": 2,
                  "bit_rate": "192000",
                  "disposition": { "default": 1 },
                  "tags": { "language": "rus" }
                },
                {
                  "index": 2,
                  "codec_name": "subrip",
                  "codec_type": "subtitle",
                  "disposition": { "default": 0, "forced": 1 },
                  "tags": { "language": "eng" }
                }
              ],
              "format": {
                "duration": "54.634000",
                "size": "147651538",
                "bit_rate": "21619838",
                "tags": {
                  "title": "Тестовый ролик",
                  "encoder": "Clipora test"
                }
              }
            }
            """;
        const string inputPath = @"C:\Видео с пробелами\тестовый ролик.mkv";

        var result = FfprobeJsonParser.Parse(json, inputPath, fallbackFileSize: 1);

        Assert.Equal(inputPath, result.Path);
        Assert.Equal(TimeSpan.FromSeconds(54.634), result.Duration);
        Assert.Equal(147651538, result.FileSizeBytes);
        Assert.Equal(21619838, result.BitrateBitsPerSecond);
        Assert.Equal(-90, result.RotationDegrees);
        Assert.Equal("Тестовый ролик", result.Metadata["TITLE"]);

        var video = Assert.Single(result.VideoStreams);
        Assert.Equal(0, video.Index);
        Assert.Equal("h264", video.Codec);
        Assert.Equal("High", video.Profile);
        Assert.Equal(1280, video.Width);
        Assert.Equal(1024, video.Height);
        Assert.Equal(120, video.FramesPerSecond);
        Assert.Equal(2048000, video.BitrateBitsPerSecond);
        Assert.Equal("yuv420p10le", video.PixelFormat);
        Assert.Equal("bt2020nc", video.ColorSpace);
        Assert.Equal("smpte2084", video.ColorTransfer);
        Assert.Equal("bt2020", video.ColorPrimaries);
        Assert.Equal("tv", video.ColorRange);
        Assert.True(video.IsHdr);
        Assert.True(video.IsDefault);
        Assert.False(video.IsAttachedPicture);
        Assert.Equal(-90, video.RotationDegrees);
        Assert.Equal(1024, video.DisplayWidth);
        Assert.Equal(1280, video.DisplayHeight);

        var audio = Assert.Single(result.AudioStreams);
        Assert.Equal("aac", audio.Codec);
        Assert.Equal(192000, audio.BitrateBitsPerSecond);
        Assert.Equal(2, audio.Channels);
        Assert.Equal(48000, audio.SampleRate);
        Assert.Equal("rus", audio.Language);
        Assert.True(audio.IsDefault);

        var subtitle = Assert.Single(result.SubtitleStreams);
        Assert.Equal("subrip", subtitle.Codec);
        Assert.Equal("eng", subtitle.Language);
        Assert.False(subtitle.IsDefault);
        Assert.True(subtitle.IsForced);
    }

    [Fact]
    public void Parse_SelectsDefaultNonAttachedVideoAndDefaultAudio()
    {
        const string json = """
            {
              "streams": [
                {
                  "index": 0,
                  "codec_name": "mjpeg",
                  "codec_type": "video",
                  "width": 600,
                  "height": 600,
                  "disposition": { "default": 1, "attached_pic": 1 }
                },
                {
                  "index": 1,
                  "codec_name": "h264",
                  "codec_type": "video",
                  "width": 1280,
                  "height": 720,
                  "disposition": { "default": 0, "attached_pic": 0 },
                  "tags": { "rotate": "180" }
                },
                {
                  "index": 2,
                  "codec_name": "hevc",
                  "codec_type": "video",
                  "width": 1920,
                  "height": 1080,
                  "disposition": { "default": 1, "attached_pic": 0 },
                  "side_data_list": [
                    { "side_data_type": "Display Matrix", "rotation": 90 }
                  ]
                },
                {
                  "index": 3,
                  "codec_name": "aac",
                  "codec_type": "audio",
                  "disposition": { "default": 0 }
                },
                {
                  "index": 4,
                  "codec_name": "opus",
                  "codec_type": "audio",
                  "disposition": { "default": 1 }
                }
              ],
              "format": { "duration": "5.0" }
            }
            """;

        var result = FfprobeJsonParser.Parse(json, "streams.mkv", fallbackFileSize: 100);

        Assert.True(result.VideoStreams[0].IsAttachedPicture);
        Assert.Equal(2, result.PrimaryVideoStream?.Index);
        Assert.Equal(4, result.PrimaryAudioStream?.Index);
        Assert.Equal(90, result.RotationDegrees);
        Assert.Equal(1080, result.PrimaryVideoStream?.DisplayWidth);
        Assert.Equal(1920, result.PrimaryVideoStream?.DisplayHeight);
    }

    [Fact]
    public void Parse_UsesFallbacksForFrameRateFileSizeAndBitrate()
    {
        const string json = """
            {
              "streams": [
                {
                  "index": "4",
                  "codec_name": "hevc",
                  "codec_type": "video",
                  "width": "3840",
                  "height": "2160",
                  "avg_frame_rate": "0/0",
                  "r_frame_rate": "30000/1001",
                  "color_transfer": "arib-std-b67",
                  "tags": { "rotate": "90" }
                }
              ],
              "format": { "duration": 10.0 }
            }
            """;

        var result = FfprobeJsonParser.Parse(json, "sample.mov", fallbackFileSize: 1000);

        Assert.Equal(1000, result.FileSizeBytes);
        Assert.Equal(800, result.BitrateBitsPerSecond);
        Assert.Equal(90, result.RotationDegrees);
        var video = Assert.Single(result.VideoStreams);
        Assert.Equal(30000d / 1001d, video.FramesPerSecond, precision: 10);
        Assert.True(video.IsHdr);
    }

    [Fact]
    public void Parse_UsesStreamTimeBaseWhenFormatDurationIsMissing()
    {
        const string json = """
            {
              "streams": [
                {
                  "index": 0,
                  "codec_name": "vp9",
                  "codec_type": "video",
                  "duration_ts": "3003",
                  "time_base": "1/1000"
                }
              ],
              "format": {}
            }
            """;

        var result = FfprobeJsonParser.Parse(json, "sample.webm", fallbackFileSize: 500);

        Assert.Equal(TimeSpan.FromSeconds(3.003), result.Duration);
    }

    [Fact]
    public void Parse_ThrowsInformativeExceptionForMalformedJson()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => FfprobeJsonParser.Parse("{ malformed", "sample.mp4", fallbackFileSize: 10));

        Assert.Contains("malformed JSON", exception.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    [Fact]
    public void Parse_ThrowsWhenVideoStreamIsMissing()
    {
        const string json = """
            {
              "streams": [
                { "index": 0, "codec_name": "aac", "codec_type": "audio" }
              ],
              "format": { "duration": "1.0" }
            }
            """;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => FfprobeJsonParser.Parse(json, "audio.m4a", fallbackFileSize: 10));

        Assert.Contains("video stream", exception.Message, StringComparison.Ordinal);
    }
}
