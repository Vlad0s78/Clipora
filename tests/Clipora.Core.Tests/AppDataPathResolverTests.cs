using Clipora.Core.Services;

namespace Clipora.Core.Tests;

public sealed class AppDataPathResolverTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "Clipora.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_WithoutPortableMarker_UsesLocalApplicationData()
    {
        string applicationDirectory = Path.Combine(_temporaryDirectory, "App");
        string localApplicationData = Path.Combine(_temporaryDirectory, "LocalAppData");
        Directory.CreateDirectory(applicationDirectory);

        var paths = AppDataPathResolver.Resolve(applicationDirectory, localApplicationData);

        string expectedRoot = Path.Combine(Path.GetFullPath(localApplicationData), "Clipora");
        Assert.False(paths.IsPortable);
        Assert.Equal(expectedRoot, paths.RootDirectory);
        Assert.Equal(Path.Combine(expectedRoot, "settings.json"), paths.SettingsFilePath);
        Assert.Equal(Path.Combine(expectedRoot, "Logs"), paths.LogDirectory);
        Assert.Equal(Path.Combine(expectedRoot, "Cache", "Thumbnails"), paths.ThumbnailCacheDirectory);
    }

    [Fact]
    public void Resolve_WithExactPortableMarker_UsesOnlyPortableDataDirectory()
    {
        string applicationDirectory = Path.Combine(_temporaryDirectory, "Clipora Portable");
        string portableDirectory = Path.Combine(
            applicationDirectory,
            AppDataPathResolver.PortableDataDirectoryName);
        Directory.CreateDirectory(portableDirectory);
        File.WriteAllText(
            Path.Combine(portableDirectory, AppDataPathResolver.PortableMarkerFileName),
            "Clipora portable data\n");

        var paths = AppDataPathResolver.Resolve(
            applicationDirectory,
            Path.Combine(_temporaryDirectory, "Must not be used"));

        string expectedRoot = Path.GetFullPath(portableDirectory);
        Assert.True(paths.IsPortable);
        Assert.Equal(expectedRoot, paths.RootDirectory);
        Assert.All(
            new[]
            {
                paths.SettingsFilePath,
                paths.LogDirectory,
                paths.ThumbnailCacheDirectory,
            },
            path => Assert.StartsWith(expectedRoot + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_PortableDirectoryWithoutMarker_RemainsInstalledMode()
    {
        string applicationDirectory = Path.Combine(_temporaryDirectory, "App");
        Directory.CreateDirectory(Path.Combine(
            applicationDirectory,
            AppDataPathResolver.PortableDataDirectoryName));
        string localApplicationData = Path.Combine(_temporaryDirectory, "Local");

        var paths = AppDataPathResolver.Resolve(applicationDirectory, localApplicationData);

        Assert.False(paths.IsPortable);
        Assert.Equal(
            Path.Combine(Path.GetFullPath(localApplicationData), "Clipora"),
            paths.RootDirectory);
    }

    [Theory]
    [InlineData(null, "local")]
    [InlineData("", "local")]
    [InlineData("app", null)]
    [InlineData("app", "")]
    public void Resolve_InvalidDirectory_Throws(string? applicationDirectory, string? localDirectory)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            AppDataPathResolver.Resolve(applicationDirectory!, localDirectory!));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
