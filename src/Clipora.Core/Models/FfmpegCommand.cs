namespace Clipora.Core.Models;

public sealed record FfmpegCommand(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string SourcePath,
    string TemporaryOutputPath);
