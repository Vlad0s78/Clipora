using Clipora.Core.Models;

namespace Clipora.Core.Interfaces;

public interface IVideoProcessingService
{
    Task<string> ProcessAsync(
        string inputPath,
        EncodeMode mode,
        TimeSpan sourceDuration,
        TrimRange? trimRange = null,
        string? outputDirectory = null,
        IProgress<EncodeProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
