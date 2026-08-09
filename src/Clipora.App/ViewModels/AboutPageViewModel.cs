using Clipora.App.Services;
using Clipora.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Clipora.App.ViewModels;

public sealed partial class AboutPageViewModel : ObservableObject, IDisposable
{
    private readonly IFFmpegVersionService _ffmpegVersionService;
    private readonly ResourceLoader _resources;
    private readonly ILogger<AboutPageViewModel> _logger;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private int _loadStarted;

    [ObservableProperty]
    public partial string FFmpegVersion { get; set; }

    public AboutPageViewModel(
        AppVersionInfo versionInfo,
        IFFmpegVersionService ffmpegVersionService,
        ResourceLoader resources,
        ILogger<AboutPageViewModel> logger)
    {
        Version = versionInfo.Version;
        Build = versionInfo.Build;
        Developer = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            resources.GetString("AboutDeveloperValueFormat"),
            versionInfo.Developer);
        _ffmpegVersionService = ffmpegVersionService;
        _resources = resources;
        _logger = logger;
        FFmpegVersion = resources.GetString("AboutFFmpegLoading");
    }

    public string Version { get; }

    public string Build { get; }

    public string Developer { get; }

    public async Task LoadAsync()
    {
        if (Interlocked.Exchange(ref _loadStarted, 1) != 0)
        {
            return;
        }

        try
        {
            FFmpegVersion = await _ffmpegVersionService.GetFirstLineAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось получить версию встроенного FFmpeg.");
            FFmpegVersion = _resources.GetString("AboutFFmpegUnavailable");
        }
    }

    public void Dispose()
    {
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }
}
