using System.Diagnostics;
using System.Text;
using Clipora.Core.Interfaces;

namespace Clipora.App.Services;

public sealed class FFmpegVersionService : IFFmpegVersionService
{
    private const int MaximumFirstLineLength = 512;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private readonly IBundledToolResolver _toolResolver;

    public FFmpegVersionService(IBundledToolResolver toolResolver)
    {
        _toolResolver = toolResolver;
    }

    public async Task<string> GetFirstLineAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string ffmpegPath = _toolResolver.Resolve().FfmpegPath;
        ProcessStartInfo startInfo = new()
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-version");

        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Bundled FFmpeg version probe did not start.");
        }

        using CancellationTokenSource timeout = new(ProbeTimeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        Task<string> stdoutTask = ReadFirstLineAndDrainAsync(process.StandardOutput, linked.Token);
        Task<string> stderrTask = ReadFirstLineAndDrainAsync(process.StandardError, linked.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            string[] firstLines = await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Bundled FFmpeg version probe exited with code {process.ExitCode}.");
            }

            string firstLine = firstLines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
                ?? throw new InvalidDataException("Bundled FFmpeg returned no version text.");
            return firstLine;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await KillProcessTreeAsync(process).ConfigureAwait(false);
            await ObserveReadersAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            throw new TimeoutException("Bundled FFmpeg version probe timed out.");
        }
        catch
        {
            await KillProcessTreeAsync(process).ConfigureAwait(false);
            await ObserveReadersAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<string> ReadFirstLineAndDrainAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[1024];
        StringBuilder firstLine = new(MaximumFirstLineLength);
        bool lineComplete = false;

        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (lineComplete)
            {
                continue;
            }

            for (int index = 0; index < read; index++)
            {
                char character = buffer[index];
                if (character is '\r' or '\n')
                {
                    lineComplete = true;
                    break;
                }

                if (firstLine.Length < MaximumFirstLineLength)
                {
                    firstLine.Append(character);
                }
            }
        }

        return firstLine.ToString().Trim();
    }

    private static async Task ObserveReadersAsync(params Task<string>[] readerTasks)
    {
        try
        {
            await Task.WhenAll(readerTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static async Task KillProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using CancellationTokenSource waitTimeout = new(TimeSpan.FromSeconds(1));
                await process.WaitForExitAsync(waitTimeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
