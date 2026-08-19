namespace Clipora.App.Services;

public interface IOutputFileLauncher
{
    Task<bool> RevealAsync(string filePath, CancellationToken cancellationToken);
}
