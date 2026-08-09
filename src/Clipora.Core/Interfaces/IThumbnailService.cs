using Clipora.Core.Models;

namespace Clipora.Core.Interfaces;

public interface IThumbnailService
{
    Task<IReadOnlyList<ThumbnailFrame>> GenerateAsync(
        string videoPath,
        TimeSpan duration,
        int count,
        CancellationToken cancellationToken);
}
