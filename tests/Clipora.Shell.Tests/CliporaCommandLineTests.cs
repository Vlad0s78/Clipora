using Clipora.Shell;

namespace Clipora.Shell.Tests;

public sealed class CliporaCommandLineTests
{
    [Fact]
    public void TryParse_NoArguments_ReturnsEmptyRequest()
    {
        bool parsed = CliporaCommandLine.TryParse([], out CliporaLaunchRequest request, out CliporaCommandLineError error);

        Assert.True(parsed);
        Assert.Equal(CliporaLaunchRequest.Empty, request);
        Assert.Equal(CliporaCommandLineError.None, error);
    }

    [Fact]
    public void TryParse_VideoPathWithSpacesAndCyrillic_ReturnsOpenRequest()
    {
        string path = Path.Combine(Path.GetTempPath(), "Папка с пробелами", "видео.mp4");

        bool parsed = CliporaCommandLine.TryParse([path], out CliporaLaunchRequest request, out CliporaCommandLineError error);

        Assert.True(parsed);
        Assert.Equal(CliporaLaunchAction.Open, request.Action);
        Assert.Equal(Path.GetFullPath(path), request.VideoPath);
        Assert.Equal(CliporaCommandLineError.None, error);
    }

    [Fact]
    public void TryParse_CompressOption_IsCaseInsensitiveAndPreservesLiteralPercent()
    {
        string path = Path.Combine(Path.GetTempPath(), "100% готово", "видео.mov");

        bool parsed = CliporaCommandLine.TryParse(
            ["--COMPRESS", path],
            out CliporaLaunchRequest request,
            out CliporaCommandLineError error);

        Assert.True(parsed);
        Assert.Equal(CliporaLaunchAction.Compress, request.Action);
        Assert.Equal(Path.GetFullPath(path), request.VideoPath);
        Assert.Equal(CliporaCommandLineError.None, error);
    }

    public static TheoryData<string[]> InvalidArguments => new()
    {
        new[] { "--compress" },
        new[] { "--unknown", "video.mp4" },
        new[] { "one.mp4", "two.mp4" },
        new[] { "--compress", "one.mp4", "two.mp4" },
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void TryParse_InvalidSyntax_IsRejected(string[] arguments)
    {
        bool parsed = CliporaCommandLine.TryParse(arguments, out CliporaLaunchRequest request, out CliporaCommandLineError error);

        Assert.False(parsed);
        Assert.Equal(CliporaLaunchRequest.Empty, request);
        Assert.Equal(CliporaCommandLineError.InvalidArguments, error);
    }
}
