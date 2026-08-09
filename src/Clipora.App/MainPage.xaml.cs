using System.ComponentModel;
using Clipora.App.Services;
using Clipora.App.Controls;
using Clipora.App.ViewModels;
using Clipora.Core.Interfaces;
using Clipora.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace Clipora.App;

public sealed partial class MainPage : Page
{
    private MediaPlayer? _previewMediaPlayer;
    private readonly ITrimShortcutService _trimShortcutService;

    public MainPageViewModel ViewModel { get; }

    public MainPage(
        MainPageViewModel viewModel,
        ITrimShortcutService trimShortcutService)
    {
        ViewModel = viewModel;
        _trimShortcutService = trimShortcutService;
        InitializeComponent();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        SizeChanged += OnPageSizeChanged;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ApplyResponsiveLayout(ActualWidth, ActualHeight);
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        if (ViewModel.IsTrimEditor)
        {
            OpenPreview();
        }
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width, e.NewSize.Height);
    }

    private void ApplyResponsiveLayout(double width, double height)
    {
        bool useWideLayout = width >= 1200d && height >= 700d;
        TrimWorkspace.Height = useWideLayout ? 412d : 360d;
        TrimInspector.Height = useWideLayout ? 412d : 360d;
        TrimInspectorColumn.Width = new GridLength(useWideLayout ? 420d : 376d);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            await ViewModel.LoadVideoCommand.ExecuteAsync(null);
            return;
        }

        try
        {
            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
            StorageFile? video = items
                .OfType<StorageFile>()
                .FirstOrDefault(file => MainPageViewModel.IsSupportedFile(file.Path));

            await ViewModel.LoadVideoCommand.ExecuteAsync(video?.Path);
        }
        catch
        {
            await ViewModel.LoadVideoCommand.ExecuteAsync(null);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainPageViewModel.IsTrimEditor))
        {
            return;
        }

        if (ViewModel.IsTrimEditor)
        {
            OpenPreview();
        }
        else
        {
            ClosePreview();
        }
    }

    private void OpenPreview()
    {
        ClosePreview();
        ViewModel.SetPreviewUnavailable(false);
        SynchronizePreviewTransport(ViewModel.TrimPlayheadSeconds);
        if (string.IsNullOrWhiteSpace(ViewModel.FilePath))
        {
            return;
        }

        MediaPlayer mediaPlayer = new()
        {
            AutoPlay = false,
            IsLoopingEnabled = false
        };
        mediaPlayer.MediaOpened += OnPreviewMediaOpened;
        mediaPlayer.MediaFailed += OnPreviewMediaFailed;
        mediaPlayer.PlaybackSession.PositionChanged += OnPreviewPositionChanged;
        mediaPlayer.PlaybackSession.PlaybackStateChanged += OnPreviewPlaybackStateChanged;
        _previewMediaPlayer = mediaPlayer;
        PreviewPlayer.SetMediaPlayer(mediaPlayer);
        mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(ViewModel.FilePath));
    }

    private void ClosePreview()
    {
        MediaPlayer? mediaPlayer = Interlocked.Exchange(ref _previewMediaPlayer, null);
        if (mediaPlayer is null)
        {
            return;
        }

        mediaPlayer.MediaOpened -= OnPreviewMediaOpened;
        mediaPlayer.MediaFailed -= OnPreviewMediaFailed;
        mediaPlayer.PlaybackSession.PositionChanged -= OnPreviewPositionChanged;
        mediaPlayer.PlaybackSession.PlaybackStateChanged -= OnPreviewPlaybackStateChanged;
        mediaPlayer.Pause();
        mediaPlayer.Source = null;
        PreviewPlayer.SetMediaPlayer(null);
        mediaPlayer.Dispose();
        UpdatePreviewPlaybackGlyph(MediaPlaybackState.None);
    }

    private void OnPreviewMediaOpened(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_previewMediaPlayer, sender) || !ViewModel.IsTrimEditor)
            {
                return;
            }

            double restoredSeconds = Math.Clamp(
                ViewModel.TrimPlayheadSeconds,
                ViewModel.TrimStartSeconds,
                ViewModel.TrimEndSeconds);
            TimeSpan restoredPosition = TimeSpan.FromSeconds(restoredSeconds);
            ViewModel.SetPreviewUnavailable(false);
            sender.PlaybackSession.Position = restoredPosition;
            ViewModel.UpdateTrimPlayhead(restoredSeconds);
            SynchronizePreviewTransport(restoredSeconds);
        });
    }

    private void OnPreviewMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(_previewMediaPlayer, sender) && ViewModel.IsTrimEditor)
            {
                ViewModel.SetPreviewUnavailable(true);
            }
        });
    }

    private void OnPreviewPositionChanged(MediaPlaybackSession sender, object args)
    {
        TimeSpan position = sender.Position;
        DispatcherQueue.TryEnqueue(() =>
        {
            MediaPlayer? mediaPlayer = _previewMediaPlayer;
            if (mediaPlayer is null ||
                !ReferenceEquals(mediaPlayer.PlaybackSession, sender) ||
                !ViewModel.IsTrimEditor)
            {
                return;
            }

            double seconds = position.TotalSeconds;
            if (seconds >= ViewModel.TrimEndSeconds &&
                mediaPlayer.PlaybackSession.PlaybackState is MediaPlaybackState.Playing)
            {
                mediaPlayer.Pause();
                seconds = ViewModel.TrimEndSeconds;
                mediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(seconds);
            }

            ViewModel.UpdateTrimPlayhead(seconds);
            SynchronizePreviewTransport(seconds);
        });
    }

    private void OnPreviewPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            MediaPlayer? mediaPlayer = _previewMediaPlayer;
            if (mediaPlayer is null ||
                !ReferenceEquals(mediaPlayer.PlaybackSession, sender) ||
                !ViewModel.IsTrimEditor)
            {
                return;
            }

            UpdatePreviewPlaybackGlyph(sender.PlaybackState);
        });
    }

    private void OnTrimSelectionChanged(object sender, TrimSelectionChangedEventArgs e)
    {
        ViewModel.UpdateTrimSelection(e.StartSeconds, e.EndSeconds);
        MediaPlayer? mediaPlayer = _previewMediaPlayer;
        if (mediaPlayer is null)
        {
            return;
        }

        double position = mediaPlayer.PlaybackSession.Position.TotalSeconds;
        if (position < ViewModel.TrimStartSeconds || position > ViewModel.TrimEndSeconds)
        {
            double target = position < ViewModel.TrimStartSeconds
                ? ViewModel.TrimStartSeconds
                : ViewModel.TrimEndSeconds;
            mediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(target);
            ViewModel.UpdateTrimPlayhead(target);
            SynchronizePreviewTransport(target);
        }
    }

    private void OnTrimSeekRequested(object sender, TrimSeekRequestedEventArgs e)
    {
        SeekPreview(e.PositionSeconds);
    }

    private void OnPreviewRewindClick(object sender, RoutedEventArgs e)
    {
        SeekPreview(GetPreviewPositionSeconds() - 5d);
    }

    private void OnPreviewForwardClick(object sender, RoutedEventArgs e)
    {
        SeekPreview(GetPreviewPositionSeconds() + 5d);
    }

    private void OnPreviewPlayPauseClick(object sender, RoutedEventArgs e)
    {
        TogglePreviewPlayback();
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ViewModel.IsTrimEditor)
        {
            return;
        }

        if (!ShortcutGestureMapper.TryFromVirtualKey(
                e.Key,
                ShortcutGestureMapper.GetPressedModifiers(),
                out ShortcutGesture gesture))
        {
            return;
        }

        if (_trimShortcutService.Matches(TrimShortcutAction.PlayPause, gesture))
        {
            TogglePreviewPlayback();
        }
        else if (_trimShortcutService.Matches(TrimShortcutAction.SetStart, gesture))
        {
            double start = TrimSelectionMath.ClampStart(
                GetPreviewPositionSeconds(),
                ViewModel.TrimEndSeconds,
                ViewModel.TrimDurationSeconds);
            ViewModel.UpdateTrimSelection(start, ViewModel.TrimEndSeconds);
        }
        else if (_trimShortcutService.Matches(TrimShortcutAction.SetEnd, gesture))
        {
            double end = TrimSelectionMath.ClampEnd(
                GetPreviewPositionSeconds(),
                ViewModel.TrimStartSeconds,
                ViewModel.TrimDurationSeconds);
            ViewModel.UpdateTrimSelection(ViewModel.TrimStartSeconds, end);
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    private void TogglePreviewPlayback()
    {
        MediaPlayer? mediaPlayer = _previewMediaPlayer;
        if (mediaPlayer is null)
        {
            return;
        }

        if (mediaPlayer.PlaybackSession.PlaybackState is MediaPlaybackState.Playing)
        {
            mediaPlayer.Pause();
            return;
        }

        double position = mediaPlayer.PlaybackSession.Position.TotalSeconds;
        if (position < ViewModel.TrimStartSeconds || position >= ViewModel.TrimEndSeconds)
        {
            SeekPreview(ViewModel.TrimStartSeconds);
        }

        mediaPlayer.Play();
    }

    private void OnResetTrimSelectionClick(object sender, RoutedEventArgs e)
    {
        ViewModel.UpdateTrimSelection(0d, ViewModel.TrimDurationSeconds);
        SeekPreview(0d);
    }

    private void SeekPreview(double seconds)
    {
        double position = TrimSelectionMath.ClampPosition(seconds, ViewModel.TrimDurationSeconds);
        if (_previewMediaPlayer is not null)
        {
            _previewMediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(position);
        }

        ViewModel.UpdateTrimPlayhead(position);
        SynchronizePreviewTransport(position);
    }

    private double GetPreviewPositionSeconds()
    {
        return _previewMediaPlayer?.PlaybackSession.Position.TotalSeconds
            ?? ViewModel.TrimPlayheadSeconds;
    }

    private void SynchronizePreviewTransport(double seconds)
    {
        double position = TrimSelectionMath.ClampPosition(seconds, ViewModel.TrimDurationSeconds);
        PreviewCurrentTimeText.Text = TrimSelectionMath.FormatTimestamp(TimeSpan.FromSeconds(position));
        PreviewTotalTimeText.Text = TrimSelectionMath.FormatTimestamp(
            TimeSpan.FromSeconds(ViewModel.TrimDurationSeconds));
    }

    private void UpdatePreviewPlaybackGlyph(MediaPlaybackState state)
    {
        PreviewPlayPauseGlyph.Glyph = state is MediaPlaybackState.Playing
            ? "\uE769"
            : "\uE768";
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ClosePreview();
    }
}
