namespace Clipora.App.Services;

public interface IOutputFolderPicker
{
    Task<string?> PickAsync(CancellationToken cancellationToken);
}
