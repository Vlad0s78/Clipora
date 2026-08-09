using Windows.Storage;
using Windows.System;

namespace Clipora.App.Services;

public sealed class LogFolderLauncher : ILogFolderLauncher
{
    public LogFolderLauncher()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clipora",
            "Logs"))
    {
    }

    public LogFolderLauncher(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        LogDirectory = Path.GetFullPath(logDirectory);
    }

    public string LogDirectory { get; }

    public async Task<bool> OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(LogDirectory);
        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(LogDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        return await Launcher.LaunchFolderAsync(folder);
    }
}
