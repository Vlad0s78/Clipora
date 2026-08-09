using Clipora.Core.Models;

namespace Clipora.Core.Interfaces;

public interface IFFmpegRunner
{
    Task<EncodeResult> RunAsync(
        FfmpegCommand command,
        TimeSpan sourceDuration,
        IProgress<EncodeProgress>? progress,
        CancellationToken cancellationToken);
}
