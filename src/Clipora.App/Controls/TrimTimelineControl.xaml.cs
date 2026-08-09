using System.Collections;
using System.Collections.Specialized;
using Clipora.Core.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Clipora.App.Controls;

public sealed partial class TrimTimelineControl : UserControl
{
    private const double HandleHitRadius = 24d;
    private const double ThumbnailTargetWidth = 120d;
    private const int MinimumThumbnailSlots = 8;
    private const int MaximumThumbnailSlots = 12;
    private const int MinorTicksPerMajorTick = 5;
    private const double RulerTargetMajorSpacing = 112d;
    private const double RulerLabelWidth = 64d;
    private const double PlayheadLineWidth = 2d;
    private const double PlayheadKnobWidth = 11d;
    private DragTarget _dragTarget;
    private DragTarget _lastHandleTarget = DragTarget.Start;
    private INotifyCollectionChanged? _observedItems;
    private bool _isControlLoaded;
    private int _renderedThumbnailSlotCount = -1;
    private double _dragOffsetX;
    private double _displayStart;
    private double _displayEnd;
    private double _displayPlayhead;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(TrimTimelineControl),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty DurationSecondsProperty = DependencyProperty.Register(
        nameof(DurationSeconds),
        typeof(double),
        typeof(TrimTimelineControl),
        new PropertyMetadata(0d, OnDurationValueChanged));

    public static readonly DependencyProperty SelectionStartSecondsProperty = DependencyProperty.Register(
        nameof(SelectionStartSeconds),
        typeof(double),
        typeof(TrimTimelineControl),
        new PropertyMetadata(0d, OnTimelineValueChanged));

    public static readonly DependencyProperty SelectionEndSecondsProperty = DependencyProperty.Register(
        nameof(SelectionEndSeconds),
        typeof(double),
        typeof(TrimTimelineControl),
        new PropertyMetadata(0d, OnTimelineValueChanged));

    public static readonly DependencyProperty PlayheadSecondsProperty = DependencyProperty.Register(
        nameof(PlayheadSeconds),
        typeof(double),
        typeof(TrimTimelineControl),
        new PropertyMetadata(0d, OnTimelineValueChanged));

    public TrimTimelineControl()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    public event EventHandler<TrimSelectionChangedEventArgs>? SelectionChanged;

    public event EventHandler<TrimSeekRequestedEventArgs>? SeekRequested;

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public double DurationSeconds
    {
        get => (double)GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    public double SelectionStartSeconds
    {
        get => (double)GetValue(SelectionStartSecondsProperty);
        set => SetValue(SelectionStartSecondsProperty, value);
    }

    public double SelectionEndSeconds
    {
        get => (double)GetValue(SelectionEndSecondsProperty);
        set => SetValue(SelectionEndSecondsProperty, value);
    }

    public double PlayheadSeconds
    {
        get => (double)GetValue(PlayheadSecondsProperty);
        set => SetValue(PlayheadSecondsProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        TrimTimelineControl control = (TrimTimelineControl)sender;
        control.ObserveItemsSource();
        control.RefreshThumbnails();
    }

    private static void OnTimelineValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        TrimTimelineControl control = (TrimTimelineControl)sender;
        control.SynchronizeDisplayValues();
        control.RefreshTimeline();
    }

    private static void OnDurationValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        TrimTimelineControl control = (TrimTimelineControl)sender;
        control.SynchronizeDisplayValues();
        control.RefreshRuler();
        control.RefreshTimeline();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isControlLoaded = true;
        ObserveItemsSource();
        SynchronizeDisplayValues();
        RefreshThumbnails();
        RefreshRuler();
        RefreshTimeline();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isControlLoaded = false;
        StopObservingItemsSource();
    }

    private void ObserveItemsSource()
    {
        StopObservingItemsSource();
        if (!_isControlLoaded || ItemsSource is not INotifyCollectionChanged collection)
        {
            return;
        }

        _observedItems = collection;
        _observedItems.CollectionChanged += OnItemsCollectionChanged;
    }

    private void StopObservingItemsSource()
    {
        if (_observedItems is null)
        {
            return;
        }

        _observedItems.CollectionChanged -= OnItemsCollectionChanged;
        _observedItems = null;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            RefreshThumbnails();
        }
        else
        {
            DispatcherQueue.TryEnqueue(RefreshThumbnails);
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshThumbnailsForSize();
        RefreshRuler();
        RefreshTimeline();
    }

    private void SynchronizeDisplayValues()
    {
        (double start, double end) = TrimSelectionMath.Normalize(
            SelectionStartSeconds,
            SelectionEndSeconds,
            DurationSeconds);
        _displayStart = start;
        _displayEnd = end;
        _displayPlayhead = TrimSelectionMath.ClampPositionToSelection(
            PlayheadSeconds,
            _displayStart,
            _displayEnd,
            DurationSeconds);
    }

    private void RefreshThumbnails()
    {
        RenderThumbnails(force: true);
    }

    private void RefreshThumbnailsForSize()
    {
        RenderThumbnails(force: false);
    }

    private void RenderThumbnails(bool force)
    {
        if (ThumbnailGrid is null)
        {
            return;
        }

        List<ThumbnailFrame> frames = ItemsSource?
            .OfType<ThumbnailFrame>()
            .OrderBy(static frame => frame.Timestamp)
            .ToList() ?? [];

        double availableWidth = Math.Max(ThumbnailGrid.ActualWidth, ActualWidth);
        int slotCount = CalculateThumbnailSlotCount(availableWidth, frames.Count);
        if (!force && slotCount == _renderedThumbnailSlotCount)
        {
            return;
        }

        _renderedThumbnailSlotCount = slotCount;
        ThumbnailGrid.ColumnDefinitions.Clear();
        ThumbnailGrid.Children.Clear();

        IReadOnlyList<ThumbnailFrame> displayedFrames = SampleFrames(frames, slotCount);
        int columnCount = displayedFrames.Count > 0 ? displayedFrames.Count : slotCount;
        for (int index = 0; index < columnCount; index++)
        {
            ThumbnailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            FrameworkElement content;
            if (displayedFrames.Count > 0)
            {
                ThumbnailFrame frame = displayedFrames[index];
                content = new Image
                {
                    Source = new BitmapImage(new Uri(frame.ImagePath)),
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
            }
            else
            {
                content = new Rectangle
                {
                    Fill = index % 2 == 0
                        ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 25, 35, 60))
                        : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 29, 51))
                };
            }

            var tile = new Border
            {
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CliporaBorderBrush"],
                BorderThickness = index < columnCount - 1 ? new Thickness(0d, 0d, 1d, 0d) : new Thickness(0d),
                Child = content
            };
            Grid.SetColumn(tile, index);
            ThumbnailGrid.Children.Add(tile);
        }
    }

    private static int CalculateThumbnailSlotCount(double width, int frameCount)
    {
        int target = Math.Clamp(
            (int)Math.Round(Math.Max(0d, width) / ThumbnailTargetWidth, MidpointRounding.AwayFromZero),
            MinimumThumbnailSlots,
            MaximumThumbnailSlots);
        return frameCount > 0 ? Math.Min(frameCount, target) : target;
    }

    private static IReadOnlyList<ThumbnailFrame> SampleFrames(IReadOnlyList<ThumbnailFrame> frames, int slotCount)
    {
        if (frames.Count <= slotCount)
        {
            return frames;
        }

        var sampledFrames = new List<ThumbnailFrame>(slotCount);
        for (int index = 0; index < slotCount; index++)
        {
            int frameIndex = (int)Math.Round(
                index * (frames.Count - 1d) / (slotCount - 1d),
                MidpointRounding.AwayFromZero);
            sampledFrames.Add(frames[frameIndex]);
        }

        return sampledFrames;
    }

    private void RefreshRuler()
    {
        if (RulerCanvas is null)
        {
            return;
        }

        RulerCanvas.Children.Clear();
        double width = RulerCanvas.ActualWidth;
        double height = RulerCanvas.ActualHeight;
        if (width <= 0d || height <= 0d)
        {
            return;
        }

        var rulerBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CliporaSecondaryBrush"];
        var baseline = new Rectangle
        {
            Width = width,
            Height = 1d,
            Fill = rulerBrush,
            Opacity = 0.38d
        };
        Canvas.SetTop(baseline, height - baseline.Height);
        RulerCanvas.Children.Add(baseline);

        double duration = double.IsFinite(DurationSeconds) && DurationSeconds > 0d
            ? DurationSeconds
            : 0d;
        if (duration <= 0d)
        {
            AddRulerTick(0d, height, isMajor: true, rulerBrush);
            AddRulerLabel(0d, 0d, width, rulerBrush);
            return;
        }

        double majorInterval = ChooseMajorInterval(duration, width);
        double minorInterval = majorInterval / MinorTicksPerMajorTick;
        int lastMinorIndex = (int)Math.Floor(duration / minorInterval + 0.0000001d);
        double lastTickSeconds = 0d;
        double lastLabelX = double.NegativeInfinity;

        for (int index = 0; index <= lastMinorIndex; index++)
        {
            double seconds = index * minorInterval;
            double x = SecondsToX(seconds, width);
            bool isMajor = index % MinorTicksPerMajorTick == 0;
            AddRulerTick(x, height, isMajor, rulerBrush);
            if (isMajor)
            {
                AddRulerLabel(seconds, x, width, rulerBrush);
                lastLabelX = x;
            }

            lastTickSeconds = seconds;
        }

        if (duration - lastTickSeconds > 0.000001d)
        {
            AddRulerTick(width, height, isMajor: true, rulerBrush);
            if (width - lastLabelX >= 56d)
            {
                AddRulerLabel(duration, width, width, rulerBrush);
            }
        }
    }

    private void AddRulerTick(
        double x,
        double rulerHeight,
        bool isMajor,
        Microsoft.UI.Xaml.Media.Brush brush)
    {
        double tickHeight = isMajor ? 10d : 5d;
        var tick = new Rectangle
        {
            Width = isMajor ? 1.5d : 1d,
            Height = tickHeight,
            Fill = brush,
            Opacity = isMajor ? 0.82d : 0.46d
        };
        Canvas.SetLeft(tick, Math.Clamp(x - tick.Width / 2d, 0d, Math.Max(0d, RulerCanvas.ActualWidth - tick.Width)));
        Canvas.SetTop(tick, Math.Max(0d, rulerHeight - tickHeight));
        RulerCanvas.Children.Add(tick);
    }

    private void AddRulerLabel(
        double seconds,
        double x,
        double rulerWidth,
        Microsoft.UI.Xaml.Media.Brush brush)
    {
        bool isFirst = x <= 0.5d;
        bool isLast = x >= rulerWidth - 0.5d;
        var label = new TextBlock
        {
            Width = RulerLabelWidth,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
            FontSize = 10.5d,
            Foreground = brush,
            Opacity = 0.96d,
            Text = TrimSelectionMath.FormatRulerLabel(seconds),
            TextAlignment = isFirst
                ? TextAlignment.Left
                : isLast
                    ? TextAlignment.Right
                    : TextAlignment.Center
        };
        double left = isFirst
            ? 0d
            : isLast
                ? Math.Max(0d, rulerWidth - RulerLabelWidth)
                : Math.Clamp(x - RulerLabelWidth / 2d, 0d, Math.Max(0d, rulerWidth - RulerLabelWidth));
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, 0d);
        RulerCanvas.Children.Add(label);
    }

    private static double ChooseMajorInterval(double duration, double width)
    {
        double targetIntervalCount = Math.Clamp(width / RulerTargetMajorSpacing, 4d, 12d);
        double rawInterval = duration / targetIntervalCount;
        double magnitude = Math.Pow(10d, Math.Floor(Math.Log10(rawInterval)));
        double normalized = rawInterval / magnitude;
        double factor = normalized < 1.5d
            ? 1d
            : normalized < 3.5d
                ? 2d
                : normalized < 7.5d
                    ? 5d
                    : 10d;
        return Math.Max(double.Epsilon, factor * magnitude);
    }

    private void RefreshTimeline()
    {
        if (OverlayCanvas is null)
        {
            return;
        }

        double width = OverlayCanvas.ActualWidth;
        double height = OverlayCanvas.ActualHeight;
        if (width <= 0d || height <= 0d)
        {
            return;
        }

        StartShade.Height = height;
        EndShade.Height = height;
        SelectionBorder.Height = height;
        StartHandle.Height = height;
        EndHandle.Height = height;
        Playhead.Height = height;
        Canvas.SetTop(StartShade, 0d);
        Canvas.SetTop(EndShade, 0d);
        Canvas.SetTop(SelectionBorder, 0d);
        Canvas.SetTop(StartHandle, 0d);
        Canvas.SetTop(EndHandle, 0d);
        Canvas.SetTop(Playhead, 0d);
        Canvas.SetTop(PlayheadKnob, 0d);

        bool hasDuration = double.IsFinite(DurationSeconds) && DurationSeconds > 0d;
        Visibility timelineVisibility = hasDuration ? Visibility.Visible : Visibility.Collapsed;
        StartShade.Visibility = timelineVisibility;
        EndShade.Visibility = timelineVisibility;
        SelectionBorder.Visibility = timelineVisibility;
        StartHandle.Visibility = timelineVisibility;
        EndHandle.Visibility = timelineVisibility;
        Playhead.Visibility = timelineVisibility;
        PlayheadKnob.Visibility = timelineVisibility;
        if (!hasDuration)
        {
            return;
        }

        double startX = SecondsToX(_displayStart, width);
        double endX = SecondsToX(_displayEnd, width);
        double playheadX = Math.Clamp(SecondsToX(_displayPlayhead, width), startX, endX);
        double selectionWidth = Math.Max(0d, endX - startX);
        double playheadLineWidth = Math.Min(PlayheadLineWidth, selectionWidth);
        double playheadKnobWidth = Math.Min(PlayheadKnobWidth, selectionWidth);

        StartShade.Width = Math.Max(0d, startX);
        Canvas.SetLeft(StartShade, 0d);
        EndShade.Width = Math.Max(0d, width - endX);
        Canvas.SetLeft(EndShade, endX);

        SelectionBorder.Width = Math.Max(0d, endX - startX);
        Canvas.SetLeft(SelectionBorder, startX);
        Canvas.SetLeft(StartHandle, Math.Clamp(startX - StartHandle.Width / 2d, 0d, Math.Max(0d, width - StartHandle.Width)));
        Canvas.SetLeft(EndHandle, Math.Clamp(endX - EndHandle.Width / 2d, 0d, Math.Max(0d, width - EndHandle.Width)));
        Playhead.Width = playheadLineWidth;
        PlayheadKnob.Width = playheadKnobWidth;
        Canvas.SetLeft(Playhead, ClampMarkerLeft(playheadX, playheadLineWidth, startX, endX));
        Canvas.SetLeft(PlayheadKnob, ClampMarkerLeft(playheadX, playheadKnobWidth, startX, endX));
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        Point point = e.GetCurrentPoint(OverlayCanvas).Position;
        double width = OverlayCanvas.ActualWidth;
        double startX = SecondsToX(_displayStart, width);
        double endX = SecondsToX(_displayEnd, width);

        double startDistance = Math.Abs(point.X - startX);
        double endDistance = Math.Abs(point.X - endX);
        bool hitsStart = startDistance <= HandleHitRadius;
        bool hitsEnd = endDistance <= HandleHitRadius;

        if (hitsStart || hitsEnd)
        {
            if (hitsStart && hitsEnd)
            {
                double midpoint = (startX + endX) / 2d;
                _dragTarget = point.X < midpoint
                    ? DragTarget.Start
                    : point.X > midpoint
                        ? DragTarget.End
                        : _lastHandleTarget;
            }
            else
            {
                _dragTarget = hitsStart ? DragTarget.Start : DragTarget.End;
            }

            _lastHandleTarget = _dragTarget;
            _dragOffsetX = point.X - (_dragTarget is DragTarget.Start ? startX : endX);
        }
        else
        {
            _dragTarget = DragTarget.Playhead;
            _dragOffsetX = 0d;
        }

        OverlayCanvas.CapturePointer(e.Pointer);
        if (_dragTarget is DragTarget.Playhead)
        {
            ApplyPointerPosition(point.X);
        }
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragTarget is DragTarget.None)
        {
            return;
        }

        ApplyPointerPosition(e.GetCurrentPoint(OverlayCanvas).Position.X);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        ApplyPointerPosition(e.GetCurrentPoint(OverlayCanvas).Position.X);
        OverlayCanvas.ReleasePointerCapture(e.Pointer);
        _dragTarget = DragTarget.None;
        _dragOffsetX = 0d;
        e.Handled = true;
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _dragTarget = DragTarget.None;
        _dragOffsetX = 0d;
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _dragTarget = DragTarget.None;
        _dragOffsetX = 0d;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        double step = Math.Clamp(DurationSeconds / 1000d, 0.1d, 1d);
        switch (e.Key)
        {
            case VirtualKey.Left:
                MoveWithKeyboard(-step);
                e.Handled = true;
                break;
            case VirtualKey.Right:
                MoveWithKeyboard(step);
                e.Handled = true;
                break;
            case VirtualKey.Home:
                SetPlayhead(_displayStart);
                e.Handled = true;
                break;
            case VirtualKey.End:
                SetPlayhead(_displayEnd);
                e.Handled = true;
                break;
        }
    }

    private void MoveWithKeyboard(double delta)
    {
        bool controlDown = IsKeyDown(VirtualKey.Control);
        bool shiftDown = IsKeyDown(VirtualKey.Shift);
        if (controlDown)
        {
            _displayStart = TrimSelectionMath.ClampStart(
                _displayStart + delta,
                _displayEnd,
                DurationSeconds);
            SelectionChanged?.Invoke(this, new TrimSelectionChangedEventArgs(_displayStart, _displayEnd));
            RefreshTimeline();
            return;
        }

        if (shiftDown)
        {
            _displayEnd = TrimSelectionMath.ClampEnd(
                _displayEnd + delta,
                _displayStart,
                DurationSeconds);
            SelectionChanged?.Invoke(this, new TrimSelectionChangedEventArgs(_displayStart, _displayEnd));
            RefreshTimeline();
            return;
        }

        SetPlayhead(_displayPlayhead + delta);
    }

    private static bool IsKeyDown(VirtualKey key)
    {
        return (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
    }

    private void ApplyPointerPosition(double pointerX)
    {
        double x = pointerX - _dragOffsetX;
        double seconds = XToSeconds(x, OverlayCanvas.ActualWidth);
        switch (_dragTarget)
        {
            case DragTarget.Start:
                _displayStart = TrimSelectionMath.ClampStart(seconds, _displayEnd, DurationSeconds);
                SelectionChanged?.Invoke(this, new TrimSelectionChangedEventArgs(_displayStart, _displayEnd));
                RefreshTimeline();
                break;
            case DragTarget.End:
                _displayEnd = TrimSelectionMath.ClampEnd(seconds, _displayStart, DurationSeconds);
                SelectionChanged?.Invoke(this, new TrimSelectionChangedEventArgs(_displayStart, _displayEnd));
                RefreshTimeline();
                break;
            case DragTarget.Playhead:
                SetPlayhead(seconds);
                break;
        }
    }

    private void SetPlayhead(double seconds)
    {
        _displayPlayhead = TrimSelectionMath.ClampPositionToSelection(
            seconds,
            _displayStart,
            _displayEnd,
            DurationSeconds);
        SeekRequested?.Invoke(this, new TrimSeekRequestedEventArgs(_displayPlayhead));
        RefreshTimeline();
    }

    private static double ClampMarkerLeft(
        double centerX,
        double markerWidth,
        double selectionStartX,
        double selectionEndX)
    {
        double maximumLeft = Math.Max(selectionStartX, selectionEndX - markerWidth);
        return Math.Clamp(centerX - markerWidth / 2d, selectionStartX, maximumLeft);
    }

    private double SecondsToX(double seconds, double width)
    {
        return DurationSeconds <= 0d ? 0d : Math.Clamp(seconds / DurationSeconds * width, 0d, width);
    }

    private double XToSeconds(double x, double width)
    {
        return width <= 0d ? 0d : Math.Clamp(x / width * DurationSeconds, 0d, DurationSeconds);
    }

    private enum DragTarget
    {
        None,
        Start,
        End,
        Playhead
    }
}
