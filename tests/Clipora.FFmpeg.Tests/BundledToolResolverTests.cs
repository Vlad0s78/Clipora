namespace Clipora.FFmpeg.Tests;

public sealed class BundledToolResolverTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "Clipora.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_ReturnsBundledPaths_WhenBothToolsExist()
    {
        string toolDirectory = Path.Combine(_temporaryDirectory, "Tools", "ffmpeg");
        Directory.CreateDirectory(toolDirectory);
        File.WriteAllBytes(Path.Combine(toolDirectory, "ffmpeg.exe"), []);
        File.WriteAllBytes(Path.Combine(toolDirectory, "ffprobe.exe"), []);

        BundledToolResolver resolver = new(_temporaryDirectory);

        var paths = resolver.Resolve();

        Assert.Equal(Path.Combine(toolDirectory, "ffmpeg.exe"), paths.FfmpegPath);
        Assert.Equal(Path.Combine(toolDirectory, "ffprobe.exe"), paths.FfprobePath);
    }

    [Fact]
    public void Resolve_Throws_WhenToolIsMissing()
    {
        BundledToolResolver resolver = new(_temporaryDirectory);

        Assert.Throws<FileNotFoundException>(() => resolver.Resolve());
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, true);
        }
    }
}
