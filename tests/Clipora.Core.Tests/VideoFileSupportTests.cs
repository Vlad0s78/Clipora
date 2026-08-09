using Clipora.Core;

namespace Clipora.Core.Tests;

public sealed class VideoFileSupportTests
{
    [Theory]
    [InlineData(@"C:\Видео с пробелами\sample.mp4")]
    [InlineData(@"C:\Video\sample.MKV")]
    [InlineData(@"C:\Video\sample.Mov")]
    public void IsSupportedPath_AcceptsSupportedVideoExtensions(string path)
    {
        Assert.True(VideoFileSupport.IsSupportedPath(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Video\sample")]
    [InlineData(@"C:\Video\sample.avi")]
    public void IsSupportedPath_RejectsUnsupportedPaths(string? path)
    {
        Assert.False(VideoFileSupport.IsSupportedPath(path));
    }
}
