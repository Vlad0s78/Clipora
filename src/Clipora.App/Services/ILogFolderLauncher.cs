namespace Clipora.App.Services;

public interface ILogFolderLauncher
{
    string LogDirectory { get; }

    Task<bool> OpenAsync(CancellationToken cancellationToken);
}
