using Clipora.Core.Models;

namespace Clipora.Core.Tests;

public sealed class VideoFileInfoTests
{
    [Fact]
    public void PrimaryStreams_PreferDefaultNonAttachedVideoAndDefaultAudio()
    {
        VideoStreamInfo attachedPicture = CreateVideo(0, isDefault: true, isAttachedPicture: true);
        VideoStreamInfo firstVideo = CreateVideo(1, isDefault: false, isAttachedPicture: false);
        VideoStreamInfo defaultVideo = CreateVideo(2, isDefault: true, isAttachedPicture: false);
        AudioStreamInfo firstAudio = new(3, "aac", null, 2, 48000, null, false);
        AudioStreamInfo defaultAudio = new(4, "opus", null, 2, 48000, null, true);
        VideoFileInfo file = CreateFile(
            [attachedPicture, firstVideo, defaultVideo],
            [firstAudio, defaultAudio]);

        Assert.Same(defaultVideo, file.PrimaryVideoStream);
        Assert.Same(defaultAudio, file.PrimaryAudioStream);
    }

    [Fact]
    public void PrimaryVideoStream_FallsBackToFirstNonAttachedVideo()
    {
        VideoStreamInfo attachedPicture = CreateVideo(0, isDefault: true, isAttachedPicture: true);
        VideoStreamInfo firstVideo = CreateVideo(1, isDefault: false, isAttachedPicture: false);
        VideoFileInfo file = CreateFile([attachedPicture, firstVideo], []);

        Assert.Same(firstVideo, file.PrimaryVideoStream);
    }

    [Fact]
    public void PrimaryVideoStream_ReturnsNullWhenOnlyAttachedPicturesExist()
    {
        VideoStreamInfo attachedPicture = CreateVideo(0, isDefault: true, isAttachedPicture: true);
        VideoFileInfo file = CreateFile([attachedPicture], []);

        Assert.Null(file.PrimaryVideoStream);
    }

    [Theory]
    [InlineData(-90, 1080, 1920)]
    [InlineData(90, 1080, 1920)]
    [InlineData(270, 1080, 1920)]
    [InlineData(180, 1920, 1080)]
    [InlineData(null, 1920, 1080)]
    public void DisplayDimensions_AccountForRotation(int? rotation, int expectedWidth, int expectedHeight)
    {
        VideoStreamInfo video = CreateVideo(
            0,
            isDefault: true,
            isAttachedPicture: false,
            rotation);

        Assert.Equal(expectedWidth, video.DisplayWidth);
        Assert.Equal(expectedHeight, video.DisplayHeight);
    }

    private static VideoFileInfo CreateFile(
        IReadOnlyList<VideoStreamInfo> videoStreams,
        IReadOnlyList<AudioStreamInfo> audioStreams)
    {
        return new VideoFileInfo(
            "sample.mkv",
            TimeSpan.FromSeconds(1),
            100,
            null,
            videoStreams,
            audioStreams,
            [],
            new Dictionary<string, string>(),
            null);
    }

    private static VideoStreamInfo CreateVideo(
        int index,
        bool isDefault,
        bool isAttachedPicture,
        int? rotation = null)
    {
        return new VideoStreamInfo(
            index,
            "h264",
            null,
            1920,
            1080,
            30,
            null,
            "yuv420p",
            "bt709",
            "bt709",
            "bt709",
            "tv",
            false,
            isDefault,
            isAttachedPicture,
            rotation);
    }
}
