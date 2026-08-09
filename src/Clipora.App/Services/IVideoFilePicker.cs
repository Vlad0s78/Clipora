namespace Clipora.App.Services;

public interface IVideoFilePicker
{
    Task<string?> PickAsync(CancellationToken cancellationToken);
}
