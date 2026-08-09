namespace Clipora.Core.Models;

public sealed record EncodeJob(
    string InputPath,
    string TemporaryOutputPath,
    string FinalOutputPath,
    EncodeMode Mode,
    EncoderCapability? Encoder,
    TrimRange? TrimRange);
