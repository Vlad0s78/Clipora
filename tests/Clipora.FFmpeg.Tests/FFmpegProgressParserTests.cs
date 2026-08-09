using Clipora.Core.Models;

namespace Clipora.FFmpeg.Tests;

public sealed class FFmpegProgressParserTests
{
    [Fact]
    public void ParseLine_MapsProgressBlockAndCalculatesDerivedValues()
    {
        FFmpegProgressParser parser = new(TimeSpan.FromSeconds(10));

        Assert.Null(parser.ParseLine("frame=120", TimeSpan.FromSeconds(2)));
        Assert.Null(parser.ParseLine("fps=59.94", TimeSpan.FromSeconds(2)));
        Assert.Null(parser.ParseLine("bitrate=2048.5kbits/s", TimeSpan.FromSeconds(2)));
        Assert.Null(parser.ParseLine("total_size=1000000", TimeSpan.FromSeconds(2)));
        Assert.Null(parser.ParseLine("out_time=00:00:05.000000", TimeSpan.FromSeconds(2)));
        Assert.Null(parser.ParseLine("speed=2.00x", TimeSpan.FromSeconds(2)));

        EncodeProgress progress = Assert.IsType<EncodeProgress>(
            parser.ParseLine("progress=continue", TimeSpan.FromSeconds(2)));

        Assert.Equal(50d, progress.Percent, precision: 8);
        Assert.Equal(120, progress.Frame);
        Assert.Equal(59.94d, progress.FramesPerSecond, precision: 8);
        Assert.Equal(2_048_500, progress.BitrateBitsPerSecond);
        Assert.Equal(1_000_000, progress.TotalSizeBytes);
        Assert.Equal(TimeSpan.FromSeconds(5), progress.OutputTime);
        Assert.Equal(2d, progress.Speed);
        Assert.Equal(TimeSpan.FromSeconds(2), progress.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(2.5), progress.EstimatedRemaining);
        Assert.Equal(2_000_000, progress.EstimatedFinalSizeBytes);
        Assert.False(progress.IsComplete);
    }

    [Fact]
    public void ParseLine_EndForcesCompleteProgress()
    {
        FFmpegProgressParser parser = new(TimeSpan.FromSeconds(60));
        parser.ParseLine("frame=10", TimeSpan.Zero);
        parser.ParseLine("total_size=500", TimeSpan.Zero);
        parser.ParseLine("out_time=00:00:03.000000", TimeSpan.Zero);

        EncodeProgress progress = Assert.IsType<EncodeProgress>(
            parser.ParseLine("progress=end", TimeSpan.FromSeconds(4)));

        Assert.Equal(100d, progress.Percent);
        Assert.Equal(TimeSpan.Zero, progress.EstimatedRemaining);
        Assert.Equal(500, progress.EstimatedFinalSizeBytes);
        Assert.True(progress.IsComplete);
    }

    [Fact]
    public void ParseLine_UsesElapsedFallbackForEtaWhenSpeedIsUnavailable()
    {
        FFmpegProgressParser parser = new(TimeSpan.FromSeconds(20));
        parser.ParseLine("out_time=00:00:05.000000", TimeSpan.Zero);
        parser.ParseLine("speed=N/A", TimeSpan.Zero);

        EncodeProgress progress = Assert.IsType<EncodeProgress>(
            parser.ParseLine("progress=continue", TimeSpan.FromSeconds(3)));

        Assert.Equal(TimeSpan.FromSeconds(9), progress.EstimatedRemaining);
    }

    [Fact]
    public void ParseLine_ParsesOutputTimeBeyondTwentyFourHours()
    {
        TimeSpan outputTime = TimeSpan.FromHours(25) + TimeSpan.FromSeconds(1);
        FFmpegProgressParser parser = new(outputTime * 2);
        parser.ParseLine("out_time=25:00:01.000000", TimeSpan.Zero);
        parser.ParseLine("speed=2.00x", TimeSpan.Zero);

        EncodeProgress progress = Assert.IsType<EncodeProgress>(
            parser.ParseLine("progress=continue", TimeSpan.FromHours(1)));

        Assert.Equal(outputTime, progress.OutputTime);
        Assert.Equal(50d, progress.Percent, precision: 8);
        Assert.Equal(outputTime / 2, progress.EstimatedRemaining);
    }

    [Theory]
    [InlineData("out_time_us")]
    [InlineData("out_time_ms")]
    public void ParseLine_ParsesFfmpegMicrosecondOutputTimeCounters(string key)
    {
        TimeSpan outputTime = TimeSpan.FromHours(25) + TimeSpan.FromSeconds(1);
        FFmpegProgressParser parser = new(outputTime * 2);
        parser.ParseLine($"{key}=90001000000", TimeSpan.Zero);
        parser.ParseLine("speed=2.00x", TimeSpan.Zero);

        EncodeProgress progress = Assert.IsType<EncodeProgress>(
            parser.ParseLine("progress=continue", TimeSpan.Zero));

        Assert.Equal(outputTime, progress.OutputTime);
        Assert.Equal(50d, progress.Percent, precision: 8);
        Assert.Equal(outputTime / 2, progress.EstimatedRemaining);
    }

    [Fact]
    public void ParseLine_ParsesMaximumRepresentableOutputTimes()
    {
        FFmpegProgressParser parser = new(TimeSpan.MaxValue);
        parser.ParseLine("out_time=256204778:48:05.4775807", TimeSpan.Zero);

        EncodeProgress progress = Assert.IsType<EncodeProgress>(
            parser.ParseLine("progress=continue", TimeSpan.Zero));

        Assert.Equal(TimeSpan.MaxValue, progress.OutputTime);

        parser.ParseLine("out_time_us=922337203685477580", TimeSpan.Zero);
        progress = Assert.IsType<EncodeProgress>(
            parser.ParseLine("progress=continue", TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromTicks(9_223_372_036_854_775_800), progress.OutputTime);
    }

    [Theory]
    [InlineData("out_time=25:60:00")]
    [InlineData("out_time=25:00:60")]
    [InlineData("out_time=25:00")]
    [InlineData("out_time=25:00:01.extra")]
    [InlineData("out_time=256204778:48:05.4775808")]
    [InlineData("out_time_us=-1")]
    [InlineData("out_time_us=922337203685477581")]
    [InlineData("out_time_ms=N/A")]
    public void ParseLine_IgnoresMalformedOutputTimes(string line)
    {
        FFmpegProgressParser parser = new(TimeSpan.FromHours(30));
        parser.ParseLine("out_time=00:00:01.000000", TimeSpan.Zero);
        parser.ParseLine(line, TimeSpan.Zero);

        EncodeProgress progress = Assert.IsType<EncodeProgress>(
            parser.ParseLine("progress=continue", TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromSeconds(1), progress.OutputTime);
    }

    [Fact]
    public void ParseLine_ClampsNegativeOutputTimeToZero()
    {
        FFmpegProgressParser parser = new(TimeSpan.FromHours(30));
        parser.ParseLine("out_time=00:00:01.000000", TimeSpan.Zero);
        parser.ParseLine("out_time=-25:00:01.000000", TimeSpan.Zero);

        EncodeProgress progress = Assert.IsType<EncodeProgress>(
            parser.ParseLine("progress=continue", TimeSpan.Zero));

        Assert.Equal(TimeSpan.Zero, progress.OutputTime);
    }

    [Fact]
    public void ParseLine_ToleratesUnknownMalformedAndUnavailableValues()
    {
        FFmpegProgressParser parser = new(TimeSpan.FromSeconds(10));

        Assert.Null(parser.ParseLine(null, TimeSpan.Zero));
        Assert.Null(parser.ParseLine("malformed", TimeSpan.Zero));
        Assert.Null(parser.ParseLine("unknown=value", TimeSpan.Zero));
        parser.ParseLine("frame=-5", TimeSpan.Zero);
        parser.ParseLine("fps=N/A", TimeSpan.Zero);
        parser.ParseLine("bitrate=N/A", TimeSpan.Zero);
        parser.ParseLine("total_size=-1", TimeSpan.Zero);
        parser.ParseLine("out_time=-00:00:02.000000", TimeSpan.Zero);
        parser.ParseLine("speed=N/A", TimeSpan.Zero);

        EncodeProgress progress = Assert.IsType<EncodeProgress>(
            parser.ParseLine("progress=continue", TimeSpan.FromSeconds(-1)));

        Assert.Equal(0, progress.Frame);
        Assert.Equal(0d, progress.FramesPerSecond);
        Assert.Null(progress.BitrateBitsPerSecond);
        Assert.Equal(0, progress.TotalSizeBytes);
        Assert.Equal(TimeSpan.Zero, progress.OutputTime);
        Assert.Equal(0d, progress.Speed);
        Assert.Equal(TimeSpan.Zero, progress.Elapsed);
        Assert.Null(progress.EstimatedRemaining);
        Assert.Null(progress.EstimatedFinalSizeBytes);
    }

    [Theory]
    [InlineData("125bits/s", 125L)]
    [InlineData("1.5kbits/s", 1_500L)]
    [InlineData("2.25Mbits/s", 2_250_000L)]
    public void ParseLine_ParsesSupportedBitrateUnits(string value, long expected)
    {
        FFmpegProgressParser parser = new(TimeSpan.FromSeconds(1));
        parser.ParseLine($"bitrate={value}", TimeSpan.Zero);

        EncodeProgress progress = Assert.IsType<EncodeProgress>(
            parser.ParseLine("progress=continue", TimeSpan.Zero));

        Assert.Equal(expected, progress.BitrateBitsPerSecond);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FFmpegProgressParser(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FFmpegProgressParser(TimeSpan.FromSeconds(-1)));
    }
}
