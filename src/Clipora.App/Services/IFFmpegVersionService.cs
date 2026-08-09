namespace Clipora.App.Services;

public interface IFFmpegVersionService
{
    Task<string> GetFirstLineAsync(CancellationToken cancellationToken);
}
