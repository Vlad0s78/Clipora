namespace Clipora.Core;

public static class VideoFileSupport
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".mkv",
        ".mov"
    };

    public static bool IsSupportedPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && SupportedExtensions.Contains(Path.GetExtension(path));
    }
}
