using Clipora.Core.Interfaces;
using Clipora.Core.Tools;

namespace Clipora.FFmpeg;

public sealed class BundledToolResolver : IBundledToolResolver
{
    private readonly string _baseDirectory;

    public BundledToolResolver()
        : this(AppContext.BaseDirectory)
    {
    }

    public BundledToolResolver(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = Path.GetFullPath(baseDirectory);
    }

    public BundledToolPaths Resolve()
    {
        string toolDirectory = Path.Combine(_baseDirectory, "Tools", "ffmpeg");
        string ffmpegPath = Path.Combine(toolDirectory, "ffmpeg.exe");
        string ffprobePath = Path.Combine(toolDirectory, "ffprobe.exe");

        EnsureExists(ffmpegPath);
        EnsureExists(ffprobePath);

        return new BundledToolPaths(ffmpegPath, ffprobePath);
    }

    private static void EnsureExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Bundled media tool is missing.", path);
        }
    }
}
