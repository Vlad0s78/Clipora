namespace Clipora.Core.Models;

public sealed record OutputFilePlan(
    string SourcePath,
    string TemporaryPath,
    string FinalPath);
