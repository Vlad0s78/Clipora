using Clipora.Core.Models;

namespace Clipora.Core.Interfaces;

public interface IFFmpegCommandBuilder
{
    FfmpegCommand Build(EncodeJob job);
}
