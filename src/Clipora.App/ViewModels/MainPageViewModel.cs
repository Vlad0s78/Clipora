using System.Globalization;
using Clipora.App.Services;
using Clipora.Core;
using Clipora.Core.Interfaces;
using Clipora.Core.Models;
using Clipora.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Clipora.App.ViewModels;

public sealed partial class MainPageViewModel : ObservableObject, IDisposable
{
    private readonly IFFprobeService _ffprobeService;
    private readonly IVideoFilePicker _filePicker;
    private readonly IVideoProcessingService _videoProcessingService;
    private readonly IThumbnailService _thumbnailService;
    private readonly ISettingsService _settingsService;
    private readonly IOutputFolderPicker _outputFolderPicker;
    private readonly IOutputFileLauncher _outputFileLauncher;
    private readonly ITaskbarProgressService _taskbarProgress;
    private readonly ResourceLoader _resources;
    private readonly ILogger<MainPageViewModel> _logger;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _analysisCancellation;
    private CancellationTokenSource? _processingCancellation;
    private CancellationTokenSource? _thumbnailCancellation;
    private long _analysisRequestId;
    private VideoLoadState _processingReturnState = VideoLoadState.Loaded;
    private EncodeMode _activeOperation = EncodeMode.Compress;
    private const int PosterDecodeWidth = 320;

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsLoaded { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial bool IsProcessing { get; set; }

    [ObservableProperty]
    public partial bool IsSuccess { get; set; }

    [ObservableProperty]
    public partial bool IsTrimEditor { get; set; }

    [ObservableProperty]
    public partial bool IsGeneratingThumbnails { get; set; }

    [ObservableProperty]
    public partial bool ShowPreviewWarning { get; set; }

    [ObservableProperty]
    public partial string PreviewWarningText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial VideoFileInfo? LoadedVideo { get; set; }

    [ObservableProperty]
    public partial string FileName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FilePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DurationText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FileSizeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BitrateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string VideoCodecText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResolutionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FrameRateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AudioText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ColorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TracksText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowErrorRetry { get; set; }

    [ObservableProperty]
    public partial bool ShowErrorBrowse { get; set; }

    [ObservableProperty]
    public partial string ProcessingFileName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial string ProgressPercentText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProgressFpsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProgressSpeedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProgressElapsedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProgressEtaText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProgressSizeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OutputPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OriginalResultSizeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OutputResultSizeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResultDifferenceText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProcessingTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProcessingProgressAutomationName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResultTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResultDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OriginalResultSizeLabelText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OutputResultSizeLabelText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double TrimDurationSeconds { get; set; }

    [ObservableProperty]
    public partial double TrimStartSeconds { get; set; }

    [ObservableProperty]
    public partial double TrimEndSeconds { get; set; }

    [ObservableProperty]
    public partial double TrimPlayheadSeconds { get; set; }

    [ObservableProperty]
    public partial string TrimStartText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TrimEndText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TrimSelectionDurationText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<ThumbnailFrame> TimelineThumbnails { get; set; } = [];

    [ObservableProperty]
    public partial ImageSource? PosterImage { get; set; }

    public MainPageViewModel(
        IFFprobeService ffprobeService,
        IVideoFilePicker filePicker,
        IVideoProcessingService videoProcessingService,
        IThumbnailService thumbnailService,
        ISettingsService settingsService,
        IOutputFolderPicker outputFolderPicker,
        IOutputFileLauncher outputFileLauncher,
        ITaskbarProgressService taskbarProgress,
        ResourceLoader resources,
        ILogger<MainPageViewModel> logger)
    {
        _ffprobeService = ffprobeService;
        _filePicker = filePicker;
        _videoProcessingService = videoProcessingService;
        _thumbnailService = thumbnailService;
        _settingsService = settingsService;
        _outputFolderPicker = outputFolderPicker;
        _outputFileLauncher = outputFileLauncher;
        _taskbarProgress = taskbarProgress;
        _resources = resources;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _logger = logger;
        ResetProgress();
        SetState(VideoLoadState.Empty);
    }

    public static bool IsSupportedFile(string? path)
    {
        return VideoFileSupport.IsSupportedPath(path);
    }

    public async Task HandleLaunchRequestAsync(
        CliporaLaunchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action is CliporaLaunchAction.None)
        {
            return;
        }

        if (request.Action is not (CliporaLaunchAction.Open or CliporaLaunchAction.Compress))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        bool loaded = await AnalyzeVideoAsync(request.VideoPath, cancellationToken);
        if (loaded && request.Action is CliporaLaunchAction.Compress)
        {
            await ProcessOperationAsync(EncodeMode.Compress, cancellationToken);
        }
    }

    [RelayCommand]
    private async Task BrowseAsync(CancellationToken cancellationToken)
    {
        try
        {
            string? path = await _filePicker.PickAsync(cancellationToken);
            if (path is not null)
            {
                await AnalyzeVideoAsync(path, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось открыть системный диалог выбора видео.");
            ShowAnalyzeError();
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task LoadVideoAsync(string? path, CancellationToken cancellationToken)
    {
        return AnalyzeVideoAsync(path, cancellationToken);
    }

    private async Task<bool> AnalyzeVideoAsync(string? path, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _processingCancellation) is not null)
        {
            return false;
        }

        CancelThumbnailGeneration();

        if (!IsSupportedFile(path))
        {
            Interlocked.Increment(ref _analysisRequestId);
            Interlocked.Exchange(ref _analysisCancellation, null)?.Cancel();
            ShowUnsupportedFileError();
            return false;
        }

        long requestId = Interlocked.Increment(ref _analysisRequestId);
        CancellationTokenSource requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource? previousCancellation = Interlocked.Exchange(
            ref _analysisCancellation,
            requestCancellation);
        previousCancellation?.Cancel();

        SetState(VideoLoadState.Loading);

        try
        {
            VideoFileInfo video = await _ffprobeService.AnalyzeAsync(path!, requestCancellation.Token);
            if (requestId != Volatile.Read(ref _analysisRequestId))
            {
                return false;
            }

            ShowLoadedVideo(video);
            _logger.LogInformation("Видео проанализировано: {VideoPath}", video.Path);
            return true;
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            if (requestId == Volatile.Read(ref _analysisRequestId))
            {
                SetState(LoadedVideo is null ? VideoLoadState.Empty : VideoLoadState.Loaded);
            }

            return false;
        }
        catch (Exception exception)
        {
            if (requestId == Volatile.Read(ref _analysisRequestId))
            {
                _logger.LogError(exception, "Не удалось проанализировать видео {VideoPath}.", path);
                ShowAnalyzeError();
            }

            return false;
        }
        finally
        {
            Interlocked.CompareExchange(ref _analysisCancellation, null, requestCancellation);
            requestCancellation.Dispose();
        }
    }

    private void ShowLoadedVideo(VideoFileInfo video)
    {
        CancelThumbnailGeneration();
        TimelineThumbnails = [];
        VideoStreamInfo? videoStream = video.PrimaryVideoStream;
        AudioStreamInfo? audioStream = video.PrimaryAudioStream;

        LoadedVideo = video;
        PosterImage = null;
        _ = LoadPosterAsync(video);
        SetPreviewUnavailable(false);
        FileName = Path.GetFileName(video.Path);
        FilePath = video.Path;
        DurationText = FormatDuration(video.Duration);
        FileSizeText = FormatFileSize(video.FileSizeBytes);
        BitrateText = FormatBitrate(video.BitrateBitsPerSecond ?? videoStream?.BitrateBitsPerSecond);
        VideoCodecText = FormatVideoCodec(videoStream);
        ResolutionText = videoStream is null
            ? GetString("UnknownValue")
            : string.Format(
                CultureInfo.CurrentCulture,
                GetString("ResolutionValueFormat"),
                videoStream.DisplayWidth,
                videoStream.DisplayHeight);
        FrameRateText = videoStream is null
            ? GetString("UnknownValue")
            : string.Format(CultureInfo.CurrentCulture, GetString("FrameRateValueFormat"), videoStream.FramesPerSecond);
        AudioText = FormatAudio(audioStream);
        ColorText = FormatColor(videoStream);
        TracksText = string.Format(
            CultureInfo.CurrentCulture,
            GetString("TracksValueFormat"),
            video.AudioStreams.Count,
            video.SubtitleStreams.Count);

        SetState(VideoLoadState.Loaded);
    }

    // Кадр показывается в карточке файла; его отсутствие не должно мешать работе.
    private async Task LoadPosterAsync(VideoFileInfo video)
    {
        try
        {
            string posterPath = await _thumbnailService.GeneratePosterAsync(
                video.Path,
                video.Duration,
                CancellationToken.None);

            if (!ReferenceEquals(LoadedVideo, video))
            {
                return;
            }

            PosterImage = new BitmapImage(new Uri(posterPath))
            {
                DecodePixelWidth = PosterDecodeWidth,
                DecodePixelType = DecodePixelType.Physical,
            };
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось подготовить кадр для {VideoPath}.", video.Path);
        }
    }

    [RelayCommand]
    private async Task OpenTrimEditorAsync(CancellationToken cancellationToken)
    {
        VideoFileInfo? video = LoadedVideo;
        if (video is null || video.Duration <= TimeSpan.Zero)
        {
            return;
        }

        TrimDurationSeconds = video.Duration.TotalSeconds;
        UpdateTrimSelection(0d, TrimDurationSeconds);
        UpdateTrimPlayhead(0d);
        SetState(VideoLoadState.TrimEditor);

        await EnsureThumbnailsAsync(video, cancellationToken);
    }

    private async Task EnsureThumbnailsAsync(VideoFileInfo video, CancellationToken cancellationToken)
    {
        if (TimelineThumbnails.Count > 0 || !IsTrimEditor)
        {
            return;
        }

        CancellationTokenSource thumbnailCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (Interlocked.CompareExchange(ref _thumbnailCancellation, thumbnailCancellation, null) is not null)
        {
            thumbnailCancellation.Dispose();
            return;
        }

        IsGeneratingThumbnails = true;

        try
        {
            IReadOnlyList<ThumbnailFrame> frames = await _thumbnailService.GenerateAsync(
                video.Path,
                video.Duration,
                20,
                thumbnailCancellation.Token);

            if (!ReferenceEquals(Volatile.Read(ref _thumbnailCancellation), thumbnailCancellation) || !IsTrimEditor)
            {
                return;
            }

            TimelineThumbnails = frames;
        }
        catch (OperationCanceledException) when (thumbnailCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось подготовить миниатюры для {VideoPath}.", video.Path);
        }
        finally
        {
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _thumbnailCancellation, null, thumbnailCancellation),
                    thumbnailCancellation))
            {
                IsGeneratingThumbnails = false;
            }

            thumbnailCancellation.Dispose();
        }
    }

    [RelayCommand]
    private void CloseTrimEditor()
    {
        CancelThumbnailGeneration();
        SetState(VideoLoadState.Loaded);
    }

    public void UpdateTrimSelection(double startSeconds, double endSeconds)
    {
        (double start, double end) = TrimSelectionMath.Normalize(
            startSeconds,
            endSeconds,
            TrimDurationSeconds);
        TrimStartSeconds = start;
        TrimEndSeconds = end;
        TrimStartText = TrimSelectionMath.FormatTimestamp(TimeSpan.FromSeconds(start));
        TrimEndText = TrimSelectionMath.FormatTimestamp(TimeSpan.FromSeconds(end));
        TrimSelectionDurationText = TrimSelectionMath.FormatTimestamp(TimeSpan.FromSeconds(end - start));
        UpdateTrimPlayhead(TrimPlayheadSeconds);
    }

    public void UpdateTrimPlayhead(double seconds)
    {
        TrimPlayheadSeconds = TrimSelectionMath.ClampPosition(seconds, TrimDurationSeconds);
    }

    public void SetPreviewUnavailable(bool unavailable)
    {
        ShowPreviewWarning = unavailable;
        PreviewWarningText = unavailable ? GetString("PreviewUnavailableWarning") : string.Empty;
    }

    [RelayCommand]
    private Task CompressAsync(CancellationToken cancellationToken)
    {
        return ProcessOperationAsync(EncodeMode.Compress, cancellationToken);
    }

    [RelayCommand]
    private Task TrimAndCompressAsync(CancellationToken cancellationToken)
    {
        return ProcessOperationAsync(EncodeMode.TrimAndCompress, cancellationToken);
    }

    [RelayCommand]
    private Task TrimOnlyAsync(CancellationToken cancellationToken)
    {
        return ProcessOperationAsync(EncodeMode.TrimOnly, cancellationToken);
    }

    [RelayCommand]
    private Task RetryProcessingAsync(CancellationToken cancellationToken)
    {
        return ProcessOperationAsync(_activeOperation, cancellationToken);
    }

    private async Task ProcessOperationAsync(EncodeMode mode, CancellationToken cancellationToken)
    {
        VideoFileInfo? video = LoadedVideo;
        if (video is null)
        {
            return;
        }

        TrimRange? trimRange = mode is EncodeMode.Compress ? null : CreateTrimRange();
        if (mode is not EncodeMode.Compress && trimRange is null)
        {
            return;
        }

        CancellationTokenSource processingCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (Interlocked.CompareExchange(
                ref _processingCancellation,
                processingCancellation,
                null) is not null)
        {
            processingCancellation.Dispose();
            return;
        }

        _activeOperation = mode;
        _processingReturnState = mode is EncodeMode.Compress
            ? VideoLoadState.Loaded
            : VideoLoadState.TrimEditor;
        bool processingStarted = false;

        try
        {
            (bool shouldProcess, string? outputDirectory) = await ResolveOutputDirectoryAsync(
                processingCancellation.Token);
            if (!shouldProcess)
            {
                return;
            }

            CancelThumbnailGeneration();
            PrepareOperationText(mode);
            ResetProgress();
            ProcessingFileName = FileName;
            SetState(VideoLoadState.Processing);
            processingStarted = true;
            SystemSleepBlocker.Acquire();
            _taskbarProgress.SetProgress(0d);

            var progress = new Progress<EncodeProgress>(
                value => ReportProgress(processingCancellation, value));
            string outputPath = await _videoProcessingService.ProcessAsync(
                video.Path,
                mode,
                video.Duration,
                trimRange,
                outputDirectory,
                progress: progress,
                cancellationToken: processingCancellation.Token);

            if (!ReferenceEquals(Volatile.Read(ref _processingCancellation), processingCancellation))
            {
                return;
            }

            long outputSizeBytes = new FileInfo(outputPath).Length;
            OutputPath = outputPath;
            OriginalResultSizeText = FormatFileSize(video.FileSizeBytes);
            OutputResultSizeText = FormatFileSize(outputSizeBytes);
            ResultDifferenceText = mode is EncodeMode.Compress
                ? FormatSizeDifference(video.FileSizeBytes, outputSizeBytes)
                : await FormatTrimDurationResultAsync(outputPath, trimRange!, video);
            ProgressValue = 100;
            ProgressPercentText = string.Format(
                CultureInfo.CurrentCulture,
                GetString("ProgressPercentFormat"),
                ProgressValue);
            SetState(VideoLoadState.Success);
            _logger.LogInformation(
                "Операция {Mode} завершена: {SourcePath} -> {OutputPath}",
                mode,
                video.Path,
                outputPath);
        }
        catch (OperationCanceledException) when (processingCancellation.IsCancellationRequested)
        {
            if (processingStarted &&
                ReferenceEquals(Volatile.Read(ref _processingCancellation), processingCancellation))
            {
                SetState(_processingReturnState);
                RestartThumbnailGenerationIfNeeded();
                _logger.LogInformation("Операция {Mode} отменена: {VideoPath}", mode, video.Path);
            }
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(Volatile.Read(ref _processingCancellation), processingCancellation))
            {
                if (processingStarted)
                {
                    _logger.LogError(exception, "Операция {Mode} завершилась ошибкой для {VideoPath}.", mode, video.Path);
                    ShowProcessingError();
                }
                else
                {
                    _logger.LogError(exception, "Не удалось определить папку для результата.");
                    ShowOutputFolderError();
                }
            }
        }
        finally
        {
            if (processingStarted)
            {
                SystemSleepBlocker.Release();
                _taskbarProgress.Clear();
            }

            Interlocked.CompareExchange(ref _processingCancellation, null, processingCancellation);
            processingCancellation.Dispose();
        }
    }

    private async Task<(bool ShouldProcess, string? OutputDirectory)> ResolveOutputDirectoryAsync(
        CancellationToken cancellationToken)
    {
        AppSettings settings = await _settingsService.LoadAsync(cancellationToken);
        if (settings.OutputMode is OutputMode.SameFolder)
        {
            return (true, null);
        }

        string? selectedFolder = settings.OutputMode is OutputMode.AskEveryTime
            ? await _outputFolderPicker.PickAsync(cancellationToken)
            : settings.CustomOutputFolder;
        if (selectedFolder is null && settings.OutputMode is OutputMode.AskEveryTime)
        {
            return (false, null);
        }

        if (!OutputDirectoryPolicy.TryNormalizeExistingDirectory(
                selectedFolder,
                out string? normalizedFolder))
        {
            throw new DirectoryNotFoundException("The selected output directory is unavailable.");
        }

        return (true, normalizedFolder);
    }

    private TrimRange? CreateTrimRange()
    {
        (double start, double end) = TrimSelectionMath.Normalize(
            TrimStartSeconds,
            TrimEndSeconds,
            TrimDurationSeconds);
        return end > start
            ? new TrimRange(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end))
            : null;
    }

    private async Task<string> FormatTrimDurationResultAsync(
        string outputPath,
        TrimRange trimRange,
        VideoFileInfo sourceVideo)
    {
        string selected = TrimSelectionMath.FormatTimestamp(trimRange.Duration);
        try
        {
            VideoFileInfo outputVideo = await _ffprobeService.AnalyzeAsync(outputPath, CancellationToken.None);
            double? framesPerSecond = sourceVideo.PrimaryVideoStream?.FramesPerSecond;
            double frameThreshold = framesPerSecond is > 0d
                ? 1d / framesPerSecond.Value
                : 0d;
            double threshold = Math.Max(frameThreshold, 0.025d);
            bool approximate = Math.Abs((outputVideo.Duration - trimRange.Duration).TotalSeconds) > threshold;
            return string.Format(
                CultureInfo.CurrentCulture,
                GetString("TrimDurationResultFormat"),
                approximate ? "≈ " : string.Empty,
                TrimSelectionMath.FormatTimestamp(outputVideo.Duration),
                selected);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось проверить длительность результата {OutputPath}.", outputPath);
            return string.Format(
                CultureInfo.CurrentCulture,
                GetString("TrimDurationResultUnavailableFormat"),
                selected);
        }
    }

    private void PrepareOperationText(EncodeMode mode)
    {
        string prefix = mode switch
        {
            EncodeMode.TrimAndCompress => "TrimCompress",
            EncodeMode.TrimOnly => "TrimOnly",
            _ => "Compression"
        };

        ProcessingTitle = GetString($"Operation{prefix}ProgressTitle");
        ProcessingProgressAutomationName = GetString($"Operation{prefix}ProgressAutomationName");
        ResultTitle = GetString($"Operation{prefix}SuccessTitle");
        ResultDescription = GetString($"Operation{prefix}SuccessDescription");
        OriginalResultSizeLabelText = GetString("ResultOriginalSizeLabel");
        OutputResultSizeLabelText = GetString(mode is EncodeMode.Compress
            ? "ResultCompressedSizeLabel"
            : "ResultTrimmedSizeLabel");
    }

    [RelayCommand]
    private void CancelProcessing()
    {
        Volatile.Read(ref _processingCancellation)?.Cancel();
    }

    [RelayCommand]
    private async Task RevealOutputAsync(CancellationToken cancellationToken)
    {
        string outputPath = OutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        try
        {
            if (!await _outputFileLauncher.RevealAsync(outputPath, cancellationToken))
            {
                _logger.LogWarning("Проводник не открылся для результата {OutputPath}.", outputPath);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось показать результат в Проводнике: {OutputPath}.", outputPath);
        }
    }

    [RelayCommand]
    private void BackToLoadedVideo()
    {
        if (LoadedVideo is not null)
        {
            CancelThumbnailGeneration();
            SetState(VideoLoadState.Loaded);
        }
    }

    [RelayCommand]
    private void BackToPreviousOperationScreen()
    {
        if (LoadedVideo is not null)
        {
            SetState(_processingReturnState);
            RestartThumbnailGenerationIfNeeded();
        }
    }

    private void RestartThumbnailGenerationIfNeeded()
    {
        if (_processingReturnState is VideoLoadState.TrimEditor &&
            LoadedVideo is { } video &&
            TimelineThumbnails.Count == 0)
        {
            _ = EnsureThumbnailsAsync(video, CancellationToken.None);
        }
    }

    private void CancelThumbnailGeneration()
    {
        Interlocked.Exchange(ref _thumbnailCancellation, null)?.Cancel();
        IsGeneratingThumbnails = false;
    }

    private void ReportProgress(CancellationTokenSource owner, EncodeProgress progress)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            ApplyProgress(owner, progress);
            return;
        }

        _dispatcherQueue.TryEnqueue(() => ApplyProgress(owner, progress));
    }

    private void ApplyProgress(CancellationTokenSource owner, EncodeProgress progress)
    {
        if (!ReferenceEquals(Volatile.Read(ref _processingCancellation), owner))
        {
            return;
        }

        ProgressValue = Math.Clamp(progress.Percent, 0, 100);
        _taskbarProgress.SetProgress(ProgressValue);
        ProgressPercentText = string.Format(
            CultureInfo.CurrentCulture,
            GetString("ProgressPercentFormat"),
            ProgressValue);
        ProgressFpsText = string.Format(
            CultureInfo.CurrentCulture,
            GetString("ProgressFramesPerSecondFormat"),
            progress.FramesPerSecond);
        ProgressSpeedText = string.Format(
            CultureInfo.CurrentCulture,
            GetString("ProgressSpeedFormat"),
            progress.Speed);
        ProgressElapsedText = FormatShortDuration(progress.Elapsed);
        ProgressEtaText = progress.EstimatedRemaining.HasValue
            ? FormatShortDuration(progress.EstimatedRemaining.Value)
            : GetString("UnknownProgressValue");
        ProgressSizeText = FormatFileSize(progress.TotalSizeBytes);
    }

    private void ResetProgress()
    {
        string unknown = GetString("UnknownProgressValue");
        ProgressValue = 0;
        ProgressPercentText = string.Format(
            CultureInfo.CurrentCulture,
            GetString("ProgressPercentFormat"),
            0d);
        ProgressFpsText = unknown;
        ProgressSpeedText = unknown;
        ProgressElapsedText = FormatShortDuration(TimeSpan.Zero);
        ProgressEtaText = unknown;
        ProgressSizeText = FormatFileSize(0);
    }

    private string FormatSizeDifference(long originalSizeBytes, long outputSizeBytes)
    {
        if (outputSizeBytes == originalSizeBytes)
        {
            return GetString("CompressionSizeUnchanged");
        }

        bool savedSpace = outputSizeBytes < originalSizeBytes;
        long difference = savedSpace
            ? originalSizeBytes - outputSizeBytes
            : outputSizeBytes - originalSizeBytes;
        double percent = originalSizeBytes == 0
            ? 0
            : difference * 100d / originalSizeBytes;

        return string.Format(
            CultureInfo.CurrentCulture,
            GetString(savedSpace ? "CompressionSavingsFormat" : "CompressionIncreaseFormat"),
            FormatFileSize(difference),
            percent);
    }

    private static string FormatShortDuration(TimeSpan duration)
    {
        long hours = (long)duration.TotalHours;
        return hours > 0
            ? $"{hours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private string FormatVideoCodec(VideoStreamInfo? stream)
    {
        if (stream is null)
        {
            return GetString("UnknownValue");
        }

        string codec = FormatCodec(stream.Codec);
        return string.IsNullOrWhiteSpace(stream.Profile)
            ? codec
            : string.Format(CultureInfo.CurrentCulture, GetString("CodecProfileValueFormat"), codec, stream.Profile);
    }

    private string FormatAudio(AudioStreamInfo? stream)
    {
        if (stream is null)
        {
            return GetString("UnknownValue");
        }

        string sampleRate = stream.SampleRate.HasValue
            ? string.Format(
                CultureInfo.CurrentCulture,
                GetString("SampleRateValueFormat"),
                stream.SampleRate.Value / 1000d)
            : GetString("UnknownValue");

        return string.Format(
            CultureInfo.CurrentCulture,
            GetString("AudioValueFormat"),
            FormatCodec(stream.Codec),
            stream.Channels,
            sampleRate);
    }

    private string FormatColor(VideoStreamInfo? stream)
    {
        if (stream is null)
        {
            return GetString("UnknownValue");
        }

        List<string> parts = [];
        AddIfPresent(parts, stream.PixelFormat);
        AddIfPresent(parts, FormatColorRange(stream.ColorRange));

        string colorMetadata = string.Join(
            " / ",
            new[] { stream.ColorPrimaries, stream.ColorTransfer, stream.ColorSpace }
                .Where(static value => !string.IsNullOrWhiteSpace(value)));
        AddIfPresent(parts, colorMetadata);
        parts.Add(GetString(stream.IsHdr ? "HdrValue" : "SdrValue"));

        return string.Join(" · ", parts);
    }

    private string? FormatColorRange(string? colorRange)
    {
        return colorRange?.ToLowerInvariant() switch
        {
            "pc" or "jpeg" => GetString("FullColorRangeValue"),
            "tv" or "mpeg" => GetString("LimitedColorRangeValue"),
            null or "" => null,
            _ => colorRange.ToUpperInvariant()
        };
    }

    private static void AddIfPresent(ICollection<string> parts, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(value);
        }
    }

    private string FormatFileSize(long bytes)
    {
        double value = bytes;
        string resourceKey = "BytesValueFormat";

        if (bytes >= 1024L * 1024L * 1024L)
        {
            value /= 1024d * 1024d * 1024d;
            resourceKey = "GibibytesValueFormat";
        }
        else if (bytes >= 1024L * 1024L)
        {
            value /= 1024d * 1024d;
            resourceKey = "MebibytesValueFormat";
        }
        else if (bytes >= 1024L)
        {
            value /= 1024d;
            resourceKey = "KibibytesValueFormat";
        }

        return string.Format(CultureInfo.CurrentCulture, GetString(resourceKey), value);
    }

    private string FormatBitrate(long? bitsPerSecond)
    {
        if (!bitsPerSecond.HasValue)
        {
            return GetString("UnknownValue");
        }

        if (bitsPerSecond.Value >= 1_000_000)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                GetString("MegabitsValueFormat"),
                bitsPerSecond.Value / 1_000_000d);
        }

        if (bitsPerSecond.Value >= 1_000)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                GetString("KilobitsValueFormat"),
                bitsPerSecond.Value / 1_000d);
        }

        return string.Format(CultureInfo.CurrentCulture, GetString("BitsValueFormat"), bitsPerSecond.Value);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        long hours = (long)duration.TotalHours;
        return $"{hours:00}:{duration.Minutes:00}:{duration.Seconds:00}.{duration.Milliseconds:000}";
    }

    private static string FormatCodec(string codec)
    {
        return codec.ToLowerInvariant() switch
        {
            "h264" => "H.264",
            "h265" or "hevc" => "H.265 / HEVC",
            "av1" => "AV1",
            "vp8" => "VP8",
            "vp9" => "VP9",
            "aac" => "AAC",
            "opus" => "Opus",
            _ => codec.ToUpperInvariant()
        };
    }

    private void ShowUnsupportedFileError()
    {
        ErrorTitle = GetString("UnsupportedVideoTitle");
        ErrorDescription = GetString("UnsupportedVideoDescription");
        ShowErrorBrowse = true;
        ShowErrorRetry = false;
        SetState(VideoLoadState.Error);
    }

    private void ShowAnalyzeError()
    {
        ErrorTitle = GetString("AnalyzeErrorTitle");
        ErrorDescription = GetString("AnalyzeErrorDescription");
        ShowErrorBrowse = true;
        ShowErrorRetry = false;
        SetState(VideoLoadState.Error);
    }

    private void ShowProcessingError()
    {
        string prefix = _activeOperation switch
        {
            EncodeMode.TrimAndCompress => "TrimCompress",
            EncodeMode.TrimOnly => "TrimOnly",
            _ => "Compression"
        };
        ErrorTitle = GetString($"{prefix}ErrorTitle");
        ErrorDescription = GetString($"{prefix}ErrorDescription");
        ShowErrorBrowse = false;
        ShowErrorRetry = true;
        SetState(VideoLoadState.Error);
    }

    private void ShowOutputFolderError()
    {
        ErrorTitle = GetString("OutputFolderErrorTitle");
        ErrorDescription = GetString("OutputFolderErrorDescription");
        ShowErrorBrowse = false;
        ShowErrorRetry = true;
        SetState(VideoLoadState.Error);
    }

    private string GetString(string key)
    {
        return _resources.GetString(key);
    }

    private void SetState(VideoLoadState state)
    {
        IsEmpty = state is VideoLoadState.Empty;
        IsLoading = state is VideoLoadState.Loading;
        IsLoaded = state is VideoLoadState.Loaded;
        HasError = state is VideoLoadState.Error;
        IsProcessing = state is VideoLoadState.Processing;
        IsSuccess = state is VideoLoadState.Success;
        IsTrimEditor = state is VideoLoadState.TrimEditor;

        if (state is not VideoLoadState.Error)
        {
            ShowErrorBrowse = false;
            ShowErrorRetry = false;
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation = Interlocked.Exchange(ref _analysisCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();

        CancellationTokenSource? processing = Interlocked.Exchange(ref _processingCancellation, null);
        processing?.Cancel();
        processing?.Dispose();

        CancellationTokenSource? thumbnails = Interlocked.Exchange(ref _thumbnailCancellation, null);
        thumbnails?.Cancel();
        thumbnails?.Dispose();
    }

    private enum VideoLoadState
    {
        Empty,
        Loading,
        Loaded,
        TrimEditor,
        Processing,
        Success,
        Error
    }
}


