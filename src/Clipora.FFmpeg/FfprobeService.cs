using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Clipora.Core.Interfaces;
using Clipora.Core.Models;

namespace Clipora.FFmpeg;

public sealed class FfprobeService : IFFprobeService
{
    private const int MaximumDiagnosticLength = 4096;
    private static readonly TimeSpan CancellationDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly IBundledToolResolver _toolResolver;

    public FfprobeService(IBundledToolResolver toolResolver)
    {
        _toolResolver = toolResolver ?? throw new ArgumentNullException(nameof(toolResolver));
    }

    public async Task<VideoFileInfo> AnalyzeAsync(
        string inputPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = ValidateInputPath(inputPath);
        long fileSize = GetFileSize(fullPath);
        string ffprobePath = _toolResolver.Resolve().FfprobePath;
        ProcessStartInfo startInfo = CreateStartInfo(ffprobePath, fullPath);

        ProcessResult result = await RunAsync(startInfo, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            string diagnostic = FormatDiagnostic(result.StandardError);
            throw new InvalidDataException(
                $"ffprobe failed for '{fullPath}' with exit code {result.ExitCode}.{diagnostic}");
        }

        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new InvalidDataException($"ffprobe returned empty JSON for '{fullPath}'.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            VideoFileInfo video = FfprobeJsonParser.Parse(result.StandardOutput, fullPath, fileSize);
            cancellationToken.ThrowIfCancellationRequested();
            return video;
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(
                $"Unable to analyze '{fullPath}': {exception.Message}",
                exception);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string ffprobePath, string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffprobePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        ProcessStartInfo startInfo = new()
        {
            FileName = ffprobePath,
            WorkingDirectory = Path.GetDirectoryName(ffprobePath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-print_format");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add("-show_format");
        startInfo.ArgumentList.Add("-show_streams");
        startInfo.ArgumentList.Add(inputPath);

        return startInfo;
    }

    private static string ValidateInputPath(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(inputPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("The input video path is invalid.", nameof(inputPath), exception);
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The input video file was not found.", fullPath);
        }

        return fullPath;
    }

    private static long GetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidDataException($"Unable to read file information for '{path}'.", exception);
        }
    }

    private static async Task<ProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using Process process = new()
        {
            StartInfo = startInfo,
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidDataException($"Unable to start ffprobe at '{startInfo.FileName}'.");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new InvalidDataException(
                $"Unable to start ffprobe at '{startInfo.FileName}'.",
                exception);
        }

        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            await WaitForDrainAsync(
                    DrainOutputAsync(process, standardOutputTask, standardErrorTask),
                    CancellationDrainTimeout)
                .ConfigureAwait(false);
            throw;
        }

        await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
        return new ProcessResult(
            process.ExitCode,
            standardOutputTask.Result,
            standardErrorTask.Result);
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException or Win32Exception)
        {
        }
    }

    private static async Task DrainOutputAsync(
        Process process,
        Task<string> standardOutputTask,
        Task<string> standardErrorTask)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or ObjectDisposedException)
        {
        }
    }

    internal static async Task<bool> WaitForDrainAsync(Task drainTask, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(drainTask);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        try
        {
            await drainTask.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            _ = drainTask.ContinueWith(
                static completedTask => _ = completedTask.Exception,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
            return false;
        }
    }

    private static string FormatDiagnostic(string standardError)
    {
        string diagnostic = standardError.Trim();
        if (diagnostic.Length == 0)
        {
            return string.Empty;
        }

        if (diagnostic.Length > MaximumDiagnosticLength)
        {
            diagnostic = string.Concat(diagnostic.AsSpan(0, MaximumDiagnosticLength), "…");
        }

        return $" ffprobe output: {diagnostic}";
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
