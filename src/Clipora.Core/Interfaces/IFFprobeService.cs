using Clipora.Core.Models;

namespace Clipora.Core.Interfaces;

public interface IFFprobeService
{
    Task<VideoFileInfo> AnalyzeAsync(string inputPath, CancellationToken cancellationToken);
}
