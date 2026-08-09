using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Clipora.Core;
using Clipora.Core.Interfaces;
using Clipora.Core.Models;

namespace Clipora.FFmpeg;

public sealed class EncoderDetector : IEncoderDetector
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(2);

    internal static readonly IReadOnlyList<string> PreferredEncoderNames =
    [
        CompressionProfile.NvidiaAv1Encoder,
        CompressionProfile.IntelAv1Encoder,
        CompressionProfile.AmdAv1Encoder,
        CompressionProfile.NvidiaHevcEncoder,
        CompressionProfile.IntelHevcEncoder,
        CompressionProfile.AmdHevcEncoder,
        CompressionProfile.SoftwareAv1FallbackEncoder
    ];

    private readonly IBundledToolResolver _toolResolver;

    public EncoderDetector(IBundledToolResolver toolResolver)
    {
        _toolResolver = toolResolver ?? throw new ArgumentNullException(nameof(toolResolver));
    }

    public async Task<IReadOnlyList<EncoderCapability>> DetectAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string ffmpegPath = _toolResolver.Resolve().FfmpegPath;
        List<EncoderCapability> capabilities = new(PreferredEncoderNames.Count);

        foreach (string encoderName in PreferredEncoderNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            capabilities.Add(await ProbeAsync(ffmpegPath, encoderName, cancellationToken).ConfigureAwait(false));
        }

        return capabilities;
    }

    internal static ProcessStartInfo CreateStartInfo(string ffmpegPath, string encoderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(encoderName);

        ProcessStartInfo startInfo = new()
        {
            FileName = ffmpegPath,
            WorkingDirectory = Path.GetDirectoryName(ffmpegPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        AddArguments(
            startInfo,
            "-hide_banner",
            "-v", "error",
            "-f", "lavfi",
            "-i", "color=c=black:s=640x360:r=24:d=0.25",
            "-frames:v", "1",
            "-an",
            "-sn",
            "-dn",
            "-c:v", encoderName);

        switch (encoderName)
        {
            case CompressionProfile.NvidiaAv1Encoder:
                AddArguments(
                    startInfo,
                    "-preset", CompressionProfile.NvidiaAv1Preset,
                    "-rc", "vbr",
                    "-cq", FormatInteger(CompressionProfile.Av1Quality),
                    "-b_ref_mode", "middle");
                break;
            case CompressionProfile.IntelAv1Encoder:
                AddArguments(
                    startInfo,
                    "-preset", CompressionProfile.IntelAv1Preset,
                    "-global_quality", FormatInteger(CompressionProfile.Av1Quality));
                break;
            case CompressionProfile.AmdAv1Encoder:
                AddArguments(
                    startInfo,
                    "-quality", CompressionProfile.AmdAv1QualityPreset,
                    "-rc", "qvbr",
                    "-qvbr_quality_level", FormatInteger(CompressionProfile.Av1Quality));
                break;
            case CompressionProfile.NvidiaHevcEncoder:
                AddArguments(
                    startInfo,
                    "-preset", CompressionProfile.NvidiaHevcPreset,
                    "-tune", CompressionProfile.NvidiaHevcTune,
                    "-rc", "vbr",
                    "-cq", FormatInteger(CompressionProfile.HevcQuality),
                    "-b:v", "0",
                    "-multipass", "qres",
                    "-spatial-aq", "1",
                    "-temporal-aq", "1",
                    "-b_ref_mode", "middle");
                break;
            case CompressionProfile.IntelHevcEncoder:
                AddArguments(
                    startInfo,
                    "-preset", CompressionProfile.IntelHevcPreset,
                    "-global_quality", FormatInteger(CompressionProfile.HevcQuality));
                break;
            case CompressionProfile.AmdHevcEncoder:
                AddArguments(
                    startInfo,
                    "-quality", CompressionProfile.AmdHevcQualityPreset,
                    "-rc", "qvbr",
                    "-qvbr_quality_level", FormatInteger(CompressionProfile.HevcQuality));
                break;
            case CompressionProfile.SoftwareAv1FallbackEncoder:
                AddArguments(
                    startInfo,
                    "-preset", CompressionProfile.SoftwareAv1FallbackPreset,
                    "-crf", FormatInteger(CompressionProfile.SoftwareAv1FallbackCrf));
                break;
        }

        AddArguments(startInfo, "-f", "null", "-");
        return startInfo;
    }

    private static async Task<EncoderCapability> ProbeAsync(
        string ffmpegPath,
        string encoderName,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = CreateStartInfo(ffmpegPath, encoderName);
        using Process process = new() { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                return Unavailable(encoderName, "FFmpeg process did not start.");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return Unavailable(encoderName, exception.Message);
        }

        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(ProbeTimeout);
        using CancellationTokenSource combined = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {
            await process.WaitForExitAsync(combined.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (combined.IsCancellationRequested)
        {
            TryTerminate(process);
            await DrainBoundedAsync(process, standardOutputTask, standardErrorTask).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            return Unavailable(encoderName, "Real encode probe timed out.");
        }

        await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
        string diagnostic = FormatDiagnostic(standardErrorTask.Result);

        return new EncoderCapability(
            encoderName,
            process.ExitCode == 0,
            IsHardwareEncoder(encoderName),
            process.ExitCode == 0 ? null : diagnostic);
    }

    private static EncoderCapability Unavailable(string encoderName, string diagnostic)
    {
        return new EncoderCapability(
            encoderName,
            IsAvailable: false,
            IsHardwareAccelerated: IsHardwareEncoder(encoderName),
            FormatDiagnostic(diagnostic));
    }

    private static bool IsHardwareEncoder(string encoderName)
    {
        return encoderName is CompressionProfile.NvidiaAv1Encoder or
            CompressionProfile.IntelAv1Encoder or CompressionProfile.AmdAv1Encoder or
            CompressionProfile.NvidiaHevcEncoder or CompressionProfile.IntelHevcEncoder or
            CompressionProfile.AmdHevcEncoder;
    }

    private static void AddArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
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

    private static async Task DrainBoundedAsync(
        Process process,
        Task<string> standardOutputTask,
        Task<string> standardErrorTask)
    {
        Task drainTask = DrainAsync(process, standardOutputTask, standardErrorTask);
        await Task.WhenAny(drainTask, Task.Delay(TerminationTimeout)).ConfigureAwait(false);
    }

    private static async Task DrainAsync(
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

    private static string FormatInteger(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatDiagnostic(string diagnostic)
    {
        const int maximumLength = 2048;
        string value = diagnostic.Trim();

        if (value.Length <= maximumLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maximumLength), "…");
    }
}




