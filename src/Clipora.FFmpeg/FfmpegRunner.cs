using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Clipora.Core.Interfaces;
using Clipora.Core.Models;

namespace Clipora.FFmpeg;

public sealed class FfmpegRunner : IFFmpegRunner
{
    internal const int MaximumDiagnosticLength = 8192;

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan DefaultGracefulStopTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultForcedStopTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan StandardInputTimeout = TimeSpan.FromMilliseconds(500);

    private readonly TimeSpan _gracefulStopTimeout;
    private readonly TimeSpan _forcedStopTimeout;

    public FfmpegRunner()
        : this(DefaultGracefulStopTimeout, DefaultForcedStopTimeout)
    {
    }

    internal FfmpegRunner(TimeSpan gracefulStopTimeout, TimeSpan forcedStopTimeout)
    {
        if (gracefulStopTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(gracefulStopTimeout));
        }

        if (forcedStopTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(forcedStopTimeout));
        }

        _gracefulStopTimeout = gracefulStopTimeout;
        _forcedStopTimeout = forcedStopTimeout;
    }

    public async Task<EncodeResult> RunAsync(
        FfmpegCommand command,
        TimeSpan sourceDuration,
        IProgress<EncodeProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command, sourceDuration);

        if (cancellationToken.IsCancellationRequested)
        {
            string diagnostic = DeleteTemporaryOutput(command);
            return new EncodeResult(
                IsSuccess: false,
                IsCancelled: true,
                ExitCode: -1,
                OutputPath: null,
                diagnostic,
                TimeSpan.Zero);
        }

        ProcessStartInfo startInfo = CreateStartInfo(command);
        using Process process = new() { StartInfo = startInfo };
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    $"FFmpeg process did not start at '{startInfo.FileName}'.");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            stopwatch.Stop();
            string cleanupDiagnostic = DeleteTemporaryOutput(command);
            throw new InvalidOperationException(
                CombineDiagnostics(
                    $"Unable to start FFmpeg at '{startInfo.FileName}'.",
                    cleanupDiagnostic),
                exception);
        }

        FFmpegProgressParser parser = new(sourceDuration);
        ThrottledProgressReporter reporter = new(progress, stopwatch, ProgressInterval);
        Task progressTask = ReadProgressAsync(process.StandardOutput, parser, reporter, stopwatch);
        Task<string> diagnosticTask = ReadDiagnosticAsync(process.StandardError);
        Task exitTask = process.WaitForExitAsync(CancellationToken.None);
        Task cancellationTask = CreateCancellationTask(cancellationToken);

        Task firstCompleted = await Task.WhenAny(exitTask, cancellationTask).ConfigureAwait(false);
        bool isCancelled = firstCompleted == cancellationTask && !exitTask.IsCompleted;

        if (isCancelled)
        {
            await StopForCancellationAsync(process).ConfigureAwait(false);
            stopwatch.Stop();

            await DrainBoundedAsync(progressTask, diagnosticTask).ConfigureAwait(false);
            string diagnostic = diagnosticTask.IsCompletedSuccessfully
                ? diagnosticTask.Result
                : string.Empty;
            diagnostic = CombineDiagnostics(
                diagnostic,
                DeleteTemporaryOutput(command));

            return new EncodeResult(
                IsSuccess: false,
                IsCancelled: true,
                ExitCode: TryGetExitCode(process),
                OutputPath: null,
                diagnostic,
                stopwatch.Elapsed);
        }

        try
        {
            await exitTask.ConfigureAwait(false);
            await Task.WhenAll(progressTask, diagnosticTask).ConfigureAwait(false);
        }
        catch
        {
            stopwatch.Stop();
            TryKillProcessTree(process);
            DeleteTemporaryOutput(command);
            throw;
        }

        stopwatch.Stop();

        int exitCode = process.ExitCode;
        bool isSuccess = exitCode == 0;
        string standardError = diagnosticTask.Result;
        if (!isSuccess)
        {
            standardError = CombineDiagnostics(standardError, DeleteTemporaryOutput(command));
        }

        return new EncodeResult(
            isSuccess,
            IsCancelled: false,
            exitCode,
            isSuccess ? command.TemporaryOutputPath : null,
            standardError,
            stopwatch.Elapsed);
    }

    internal static ProcessStartInfo CreateStartInfo(FfmpegCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ExecutablePath);
        ArgumentNullException.ThrowIfNull(command.Arguments);

        ProcessStartInfo startInfo = new()
        {
            FileName = command.ExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(command.ExecutablePath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            StandardInputEncoding = Utf8NoBom,
        };

        foreach (string argument in command.Arguments)
        {
            if (argument is null)
            {
                throw new ArgumentException("FFmpeg arguments cannot contain null values.", nameof(command));
            }

            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void ValidateCommand(FfmpegCommand command, TimeSpan sourceDuration)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.TemporaryOutputPath);
        ArgumentNullException.ThrowIfNull(command.Arguments);

        if (sourceDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceDuration),
                sourceDuration,
                "Source duration must be greater than zero.");
        }

        if (!TryValidateTemporaryOutput(command, out _, out string reason))
        {
            throw new ArgumentException(
                $"Unsafe FFmpeg output command: {reason}",
                nameof(command));
        }
    }

    private static async Task ReadProgressAsync(
        StreamReader reader,
        FFmpegProgressParser parser,
        ThrottledProgressReporter reporter,
        Stopwatch stopwatch)
    {
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            EncodeProgress? snapshot = parser.ParseLine(line, stopwatch.Elapsed);
            if (snapshot is not null)
            {
                await reporter.ReportAsync(snapshot).ConfigureAwait(false);
            }
        }
    }

    private static async Task<string> ReadDiagnosticAsync(StreamReader reader)
    {
        BoundedTextBuffer diagnostic = new(MaximumDiagnosticLength);
        char[] buffer = ArrayPool<char>.Shared.Rent(1024);

        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
            {
                diagnostic.Append(buffer.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        return diagnostic.ToString();
    }

    private async Task StopForCancellationAsync(Process process)
    {
        await TryRequestGracefulStopAsync(process).ConfigureAwait(false);
        if (await WaitForExitAsync(process, _gracefulStopTimeout).ConfigureAwait(false))
        {
            return;
        }

        TryKillProcessTree(process);
        await WaitForExitAsync(process, _forcedStopTimeout).ConfigureAwait(false);
    }

    private static async Task TryRequestGracefulStopAsync(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            await process.StandardInput.WriteLineAsync("q")
                .WaitAsync(StandardInputTimeout)
                .ConfigureAwait(false);
            await process.StandardInput.FlushAsync()
                .WaitAsync(StandardInputTimeout)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(timeout)
                .ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void TryKillProcessTree(Process process)
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

    private async Task DrainBoundedAsync(Task progressTask, Task<string> diagnosticTask)
    {
        Task drainTask = Task.WhenAll(progressTask, diagnosticTask);
        Task completed = await Task.WhenAny(
                drainTask,
                Task.Delay(_forcedStopTimeout))
            .ConfigureAwait(false);

        if (completed == drainTask)
        {
            try
            {
                await drainTask.ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException or ObjectDisposedException)
            {
            }

            return;
        }

        _ = drainTask.ContinueWith(
            task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static Task CreateCancellationTask(CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return Task.Delay(Timeout.InfiniteTimeSpan);
        }

        return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private static int TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : -1;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static string DeleteTemporaryOutput(FfmpegCommand command)
    {
        if (!TryValidateTemporaryOutput(command, out string temporaryOutputPath, out string reason))
        {
            return $"Temporary output cleanup skipped: {reason}";
        }

        try
        {
            File.Delete(temporaryOutputPath);
            return string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return $"Unable to delete temporary output '{temporaryOutputPath}': {exception.Message}";
        }
    }

    private static bool TryValidateTemporaryOutput(
        FfmpegCommand command,
        out string temporaryOutputPath,
        out string reason)
    {
        temporaryOutputPath = string.Empty;
        reason = string.Empty;

        string sourcePath;
        string lastArgumentPath;
        try
        {
            temporaryOutputPath = Path.GetFullPath(command.TemporaryOutputPath);
            sourcePath = Path.GetFullPath(command.SourcePath);

            if (command.Arguments.Count == 0)
            {
                reason = "the command has no output argument.";
                return false;
            }

            lastArgumentPath = Path.GetFullPath(command.Arguments[^1]);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = "one of the command paths is invalid.";
            return false;
        }

        string extension = Path.GetExtension(temporaryOutputPath);
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(temporaryOutputPath);
        if (string.IsNullOrEmpty(extension) ||
            !nameWithoutExtension.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            nameWithoutExtension.Length == ".tmp".Length)
        {
            reason = "the filename does not match '<name>.tmp<extension>'.";
            return false;
        }

        if (PathsEqual(sourcePath, temporaryOutputPath))
        {
            reason = "the temporary output resolves to the source path.";
            return false;
        }

        if (!PathsEqual(lastArgumentPath, temporaryOutputPath))
        {
            reason = "the last command argument is not the temporary output path.";
            return false;
        }

        return true;
    }

    private static bool PathsEqual(string first, string second)
    {
        return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    }

    private static string CombineDiagnostics(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return TrimDiagnostic(second);
        }

        if (string.IsNullOrWhiteSpace(second))
        {
            return TrimDiagnostic(first);
        }

        return TrimDiagnostic($"{first}{Environment.NewLine}{second}");
    }

    private static string TrimDiagnostic(string value)
    {
        string diagnostic = value.Trim();
        if (diagnostic.Length <= MaximumDiagnosticLength)
        {
            return diagnostic;
        }

        return string.Concat("…", diagnostic.AsSpan(diagnostic.Length - MaximumDiagnosticLength + 1));
    }

    private sealed class ThrottledProgressReporter
    {
        private readonly IProgress<EncodeProgress>? _progress;
        private readonly Stopwatch _stopwatch;
        private readonly TimeSpan _minimumInterval;

        private TimeSpan? _lastReportTime;

        public ThrottledProgressReporter(
            IProgress<EncodeProgress>? progress,
            Stopwatch stopwatch,
            TimeSpan minimumInterval)
        {
            _progress = progress;
            _stopwatch = stopwatch;
            _minimumInterval = minimumInterval;
        }

        public async ValueTask ReportAsync(EncodeProgress snapshot)
        {
            if (_progress is null)
            {
                return;
            }

            TimeSpan currentTime = _stopwatch.Elapsed;
            if (_lastReportTime is TimeSpan lastReportTime)
            {
                TimeSpan remainingDelay = _minimumInterval - (currentTime - lastReportTime);
                if (remainingDelay > TimeSpan.Zero)
                {
                    if (!snapshot.IsComplete)
                    {
                        return;
                    }

                    await Task.Delay(remainingDelay).ConfigureAwait(false);
                    snapshot = snapshot with { Elapsed = _stopwatch.Elapsed };
                }
            }

            _progress.Report(snapshot);
            _lastReportTime = _stopwatch.Elapsed;
        }
    }

    private sealed class BoundedTextBuffer
    {
        private readonly int _maximumLength;
        private readonly StringBuilder _value = new();

        private bool _wasTruncated;

        public BoundedTextBuffer(int maximumLength)
        {
            _maximumLength = maximumLength;
        }

        public void Append(ReadOnlySpan<char> value)
        {
            if (value.Length >= _maximumLength)
            {
                _value.Clear();
                _value.Append(value[^_maximumLength..]);
                _wasTruncated = true;
                return;
            }

            _value.Append(value);
            int excessLength = _value.Length - _maximumLength;
            if (excessLength > 0)
            {
                _value.Remove(0, excessLength);
                _wasTruncated = true;
            }
        }

        public override string ToString()
        {
            string result = _value.ToString().Trim();
            if (!_wasTruncated || result.Length == 0)
            {
                return result;
            }

            if (result.Length >= _maximumLength)
            {
                return string.Concat("…", result.AsSpan(result.Length - _maximumLength + 1));
            }

            return $"…{result}";
        }
    }
}
