using Clipora.App.Services;
using Clipora.App.ViewModels;
using Clipora.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Clipora.App;

public sealed partial class SettingsPage : Page
{
    private Button? _recordingButton;
    private bool _suppressRecorderActivationClick;

    public SettingsPageViewModel ViewModel { get; }

    public SettingsPage(SettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        AddHandler(KeyDownEvent, new KeyEventHandler(OnShortcutKeyDown), handledEventsToo: true);
        AddHandler(KeyUpEvent, new KeyEventHandler(OnShortcutKeyUp), handledEventsToo: true);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelShortcutRecording();
        ViewModel.Dispose();
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        CancelShortcutRecording();
        if (App.Window is MainWindow window)
        {
            window.NavigateHome();
        }
    }

    private void OnPlayPauseShortcutClick(object sender, RoutedEventArgs e)
    {
        BeginShortcutRecording(TrimShortcutAction.PlayPause, (Button)sender);
    }

    private void OnSetStartShortcutClick(object sender, RoutedEventArgs e)
    {
        BeginShortcutRecording(TrimShortcutAction.SetStart, (Button)sender);
    }

    private void OnSetEndShortcutClick(object sender, RoutedEventArgs e)
    {
        BeginShortcutRecording(TrimShortcutAction.SetEnd, (Button)sender);
    }

    private void OnShortcutButtonLostFocus(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, _recordingButton) && ViewModel.IsRecordingShortcut)
        {
            CancelShortcutRecording();
        }
    }

    private void BeginShortcutRecording(TrimShortcutAction action, Button button)
    {
        if (_suppressRecorderActivationClick)
        {
            _suppressRecorderActivationClick = false;
            return;
        }

        SetRecorderVisual(_recordingButton, isRecording: false);
        _recordingButton = button;
        SetRecorderVisual(_recordingButton, isRecording: true);
        ViewModel.BeginShortcutRecording(action);
    }

    private void CancelShortcutRecording()
    {
        SetRecorderVisual(_recordingButton, isRecording: false);
        _recordingButton = null;
        _suppressRecorderActivationClick = false;
        ViewModel.CancelShortcutRecording();
    }

    private void OnShortcutKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ViewModel.IsRecordingShortcut)
        {
            return;
        }

        if (e.Key is VirtualKey.Escape)
        {
            CancelShortcutRecording();
            e.Handled = true;
            return;
        }

        if (ShortcutGestureMapper.IsModifierKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        ShortcutModifiers modifiers = ShortcutGestureMapper.GetPressedModifiers();
        if (ShortcutGestureMapper.TryFromVirtualKey(e.Key, modifiers, out ShortcutGesture gesture))
        {
            ViewModel.TryCompleteShortcutRecording(gesture);
        }
        else
        {
            ViewModel.ReportShortcutRecordingError(TrimShortcutValidationError.UnsupportedKey);
        }

        SetRecorderVisual(_recordingButton, isRecording: false);
        _recordingButton = null;
        _suppressRecorderActivationClick = e.Key is VirtualKey.Space or VirtualKey.Enter;
        e.Handled = true;
    }

    private void OnShortcutKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (_suppressRecorderActivationClick &&
            e.Key is VirtualKey.Space or VirtualKey.Enter)
        {
            e.Handled = true;
            DispatcherQueue.TryEnqueue(() => _suppressRecorderActivationClick = false);
        }
    }

    private static void SetRecorderVisual(Button? button, bool isRecording)
    {
        if (button is null)
        {
            return;
        }

        button.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            isRecording ? "CliporaCyanBrush" : "CliporaBorderBrush"];
        button.BorderThickness = new Thickness(isRecording ? 2d : 1d);
    }
}
