namespace Clipora.Core.Models;

public sealed record AppDataPaths(
    bool IsPortable,
    string RootDirectory,
    string SettingsFilePath,
    string LogDirectory,
    string ThumbnailCacheDirectory);
