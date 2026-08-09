using Clipora.Core.Models;

namespace Clipora.Core.Interfaces;

public interface IOutputFileService
{
    OutputFilePlan CreatePlan(string sourcePath, EncodeMode mode, string? outputDirectory = null);

    Task FinalizeAsync(OutputFilePlan plan, CancellationToken cancellationToken);

    void DeleteTemporaryFile(OutputFilePlan plan);
}
