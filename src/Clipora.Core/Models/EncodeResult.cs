namespace Clipora.Core.Models;

public sealed record EncodeResult(
    bool IsSuccess,
    bool IsCancelled,
    int ExitCode,
    string? OutputPath,
    string StandardError,
    TimeSpan Elapsed);
