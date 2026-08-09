using Clipora.Core.Models;

namespace Clipora.Core.Services;

public static class AppDataPathResolver
{
    public const string PortableDataDirectoryName = "portable-data";
    public const string PortableMarkerFileName = ".clipora-portable";

    public static AppDataPaths Resolve(
        string applicationBaseDirectory,
        string localApplicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataDirectory);

        string applicationRoot = Path.GetFullPath(applicationBaseDirectory);
        string portableRoot = Path.Combine(applicationRoot, PortableDataDirectoryName);
        string portableMarker = Path.Combine(portableRoot, PortableMarkerFileName);
        bool isPortable = File.Exists(portableMarker);
        string rootDirectory = isPortable
            ? portableRoot
            : Path.Combine(Path.GetFullPath(localApplicationDataDirectory), "Clipora");

        return new AppDataPaths(
            isPortable,
            rootDirectory,
            Path.Combine(rootDirectory, "settings.json"),
            Path.Combine(rootDirectory, "Logs"),
            Path.Combine(rootDirectory, "Cache", "Thumbnails"));
    }
}
