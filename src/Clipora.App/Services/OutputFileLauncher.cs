using System.Diagnostics;

namespace Clipora.App.Services;

public sealed class OutputFileLauncher : IOutputFileLauncher
{
    public Task<bool> RevealAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = Path.GetFullPath(filePath);

        // Windows запрещает кавычки в путях, поэтому такой путь получен не от файловой системы.
        if (fullPath.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("The output path contains an unexpected quote.", nameof(filePath));
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The output file was not found.", fullPath);
        }

        // Launcher.LaunchFolderAsync в unpackaged-приложении молча не открывает окно,
        // поэтому результат показывается через сам Проводник.
        ProcessStartInfo startInfo = new()
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{fullPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process? process = Process.Start(startInfo);
        return Task.FromResult(process is not null);
    }
}
