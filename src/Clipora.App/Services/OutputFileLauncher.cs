using Windows.Storage;
using Windows.System;

namespace Clipora.App.Services;

public sealed class OutputFileLauncher : IOutputFileLauncher
{
    public async Task<bool> RevealAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = Path.GetFullPath(filePath);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The output path has no parent directory.", nameof(filePath));

        StorageFile file = await StorageFile.GetFileFromPathAsync(fullPath);
        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(directory);
        cancellationToken.ThrowIfCancellationRequested();

        FolderLauncherOptions options = new();
        options.ItemsToSelect.Add(file);
        return await Launcher.LaunchFolderAsync(folder, options);
    }
}
