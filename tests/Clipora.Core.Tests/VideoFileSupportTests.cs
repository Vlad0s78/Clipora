using Clipora.Core;

namespace Clipora.Core.Tests;

public sealed class VideoFileSupportTests
{
    [Theory]
    [InlineData(@"C:\Видео с пробелами\sample.mp4")]
    [InlineData(@"C:\Video\sample.MKV")]
    [InlineData(@"C:\Video\sample.Mov")]
    [InlineData(@"C:\Video\sample.avi")]
    [InlineData(@"C:\Video\sample.WebM")]
    public void IsSupportedPath_AcceptsSupportedVideoExtensions(string path)
    {
        Assert.True(VideoFileSupport.IsSupportedPath(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Video\sample")]
    [InlineData(@"C:\Video\sample.wmv")]
    [InlineData(@"C:\Video\sample.txt")]
    public void IsSupportedPath_RejectsUnsupportedPaths(string? path)
    {
        Assert.False(VideoFileSupport.IsSupportedPath(path));
    }
}
