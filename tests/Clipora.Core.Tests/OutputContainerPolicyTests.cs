using Clipora.Core;
using Clipora.Core.Models;

namespace Clipora.Core.Tests;

public sealed class OutputContainerPolicyTests
{
    [Theory]
    [InlineData(".mp4")]
    [InlineData(".mkv")]
    [InlineData(".MOV")]
    public void ResolveExtension_KeepsContainersThatHoldReEncodedVideo(string extension)
    {
        Assert.Equal(extension, OutputContainerPolicy.ResolveExtension(extension, EncodeMode.Compress));
        Assert.Equal(extension, OutputContainerPolicy.ResolveExtension(extension, EncodeMode.TrimAndCompress));
    }

    [Theory]
    [InlineData(".avi")]
    [InlineData(".webm")]
    public void ResolveExtension_FallsBackToMp4WhenContainerCannotHoldReEncodedVideo(string extension)
    {
        Assert.Equal(".mp4", OutputContainerPolicy.ResolveExtension(extension, EncodeMode.Compress));
        Assert.Equal(".mp4", OutputContainerPolicy.ResolveExtension(extension, EncodeMode.TrimAndCompress));
    }

    [Theory]
    [InlineData(".avi")]
    [InlineData(".webm")]
    [InlineData(".mkv")]
    public void ResolveExtension_KeepsSourceContainerWhenStreamsAreCopied(string extension)
    {
        Assert.Equal(extension, OutputContainerPolicy.ResolveExtension(extension, EncodeMode.TrimOnly));
    }
}
