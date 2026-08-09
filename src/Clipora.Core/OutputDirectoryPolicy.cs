using Clipora.Core.Models;

namespace Clipora.Core;

public static class OutputDirectoryPolicy
{
    public static bool RequiresPicker(OutputMode outputMode)
    {
        return outputMode is OutputMode.AskEveryTime;
    }

    public static bool TryNormalizeExistingDirectory(string? path, out string? normalizedPath)
    {
        normalizedPath = null;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
            {
                return false;
            }

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
