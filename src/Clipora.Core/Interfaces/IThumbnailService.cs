using Clipora.Core.Models;

namespace Clipora.Core.Interfaces;

public interface IThumbnailService
{
    Task<IReadOnlyList<ThumbnailFrame>> GenerateAsync(
        string videoPath,
        TimeSpan duration,
        int count,
        CancellationToken cancellationToken);

    /// <summary>
    /// Renders a single frame used as the poster of the loaded video.
    /// </summary>
    Task<string> GeneratePosterAsync(
        string videoPath,
        TimeSpan duration,
        CancellationToken cancellationToken);
}
