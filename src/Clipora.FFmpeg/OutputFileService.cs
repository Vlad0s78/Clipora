using Clipora.Core;
using Clipora.Core.Interfaces;
using Clipora.Core.Models;

namespace Clipora.FFmpeg;

public sealed class OutputFileService : IOutputFileService
{
    public OutputFilePlan CreatePlan(
        string sourcePath,
        EncodeMode mode,
        string? outputDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("The source video file was not found.", fullSourcePath);
        }

        string sourceExtension = Path.GetExtension(fullSourcePath);
        if (string.IsNullOrEmpty(sourceExtension))
        {
            throw new ArgumentException("The source video file must have an extension.", nameof(sourcePath));
        }

        string extension = OutputContainerPolicy.ResolveExtension(sourceExtension, mode);

        string directory = ResolveOutputDirectory(fullSourcePath, outputDirectory);
        string sourceName = Path.GetFileNameWithoutExtension(fullSourcePath);
        string outputName = string.Concat(sourceName, GetSuffix(mode));

        for (int collisionIndex = 1; ; collisionIndex++)
        {
            string collisionSuffix = collisionIndex == 1
                ? string.Empty
                : $"_{collisionIndex}";
            string candidateName = string.Concat(outputName, collisionSuffix);
            string finalPath = Path.Combine(directory, string.Concat(candidateName, extension));
            string temporaryPath = Path.Combine(
                directory,
                string.Concat(candidateName, ".tmp", extension));

            if (PathsEqual(fullSourcePath, finalPath) || PathsEqual(fullSourcePath, temporaryPath))
            {
                continue;
            }

            if (!PathExists(finalPath) && !PathExists(temporaryPath))
            {
                return new OutputFilePlan(fullSourcePath, temporaryPath, finalPath);
            }
        }
    }

    public Task FinalizeAsync(OutputFilePlan plan, CancellationToken cancellationToken)
    {
        ValidatedOutputFilePlan validatedPlan = ValidatePlan(plan);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(validatedPlan.TemporaryPath))
        {
            throw new FileNotFoundException(
                "The temporary output file was not found.",
                validatedPlan.TemporaryPath);
        }

        if (PathExists(validatedPlan.FinalPath))
        {
            throw new IOException($"The final output file already exists: '{validatedPlan.FinalPath}'.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        File.Move(validatedPlan.TemporaryPath, validatedPlan.FinalPath, overwrite: false);
        return Task.CompletedTask;
    }

    public void DeleteTemporaryFile(OutputFilePlan plan)
    {
        ValidatedOutputFilePlan validatedPlan = ValidatePlan(plan);

        // Очистка выполняется в finally, поэтому она не должна подменять исходную ошибку операции.
        try
        {
            File.Delete(validatedPlan.TemporaryPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    private static string ResolveOutputDirectory(string sourcePath, string? outputDirectory)
    {
        string directory;
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            directory = Path.GetDirectoryName(sourcePath) ??
                throw new ArgumentException("The source path does not have a directory.", nameof(sourcePath));
        }
        else
        {
            directory = Path.GetFullPath(outputDirectory);
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string GetSuffix(EncodeMode mode)
    {
        return mode switch
        {
            EncodeMode.TrimOnly => "_trimmed",
            EncodeMode.Compress => "_compressed",
            EncodeMode.TrimAndCompress => "_trimmed_compressed",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown encode mode."),
        };
    }

    private static ValidatedOutputFilePlan ValidatePlan(OutputFilePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.SourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.TemporaryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.FinalPath);

        string sourcePath = Path.GetFullPath(plan.SourcePath);
        string temporaryPath = Path.GetFullPath(plan.TemporaryPath);
        string finalPath = Path.GetFullPath(plan.FinalPath);
        if (PathsEqual(sourcePath, temporaryPath) || PathsEqual(sourcePath, finalPath))
        {
            throw new ArgumentException(
                "The source file cannot be used as an output file.",
                nameof(plan));
        }

        if (PathsEqual(temporaryPath, finalPath))
        {
            throw new ArgumentException(
                "Temporary and final output paths must be different.",
                nameof(plan));
        }

        string extension = Path.GetExtension(finalPath);
        if (string.IsNullOrEmpty(extension))
        {
            throw new ArgumentException("The final output file must have an extension.", nameof(plan));
        }

        string finalDirectory = Path.GetDirectoryName(finalPath) ??
            throw new ArgumentException("The final output path does not have a directory.", nameof(plan));
        string temporaryDirectory = Path.GetDirectoryName(temporaryPath) ??
            throw new ArgumentException("The temporary output path does not have a directory.", nameof(plan));
        if (!PathsEqual(temporaryDirectory, finalDirectory))
        {
            throw new ArgumentException(
                "Temporary and final output files must use the same directory.",
                nameof(plan));
        }

        string expectedTemporaryPath = Path.Combine(
            finalDirectory,
            string.Concat(Path.GetFileNameWithoutExtension(finalPath), ".tmp", extension));
        if (!PathsEqual(temporaryPath, expectedTemporaryPath))
        {
            throw new ArgumentException(
                "The temporary output path does not match the final output path.",
                nameof(plan));
        }

        return new ValidatedOutputFilePlan(sourcePath, temporaryPath, finalPath);
    }

    private static bool PathsEqual(string first, string second)
    {
        return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathExists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }

    private sealed record ValidatedOutputFilePlan(
        string SourcePath,
        string TemporaryPath,
        string FinalPath);
}
