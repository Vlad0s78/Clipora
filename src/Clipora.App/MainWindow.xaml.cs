using System.ComponentModel;
using System.Runtime.InteropServices;
using Clipora.App.ViewModels;
using Clipora.Core.Interfaces;
using Clipora.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Clipora.App;

public sealed partial class MainWindow : Window
{
    private const uint GetMinMaxInfoMessage = 0x0024;
    private const int MinimumWidthDip = WindowLayout.MinimumWidthDip;
    private const int MinimumHeightDip = WindowLayout.MinimumHeightDip;
    private const int DefaultWidthDip = 1160;
    private const int DefaultHeightDip = 780;
    private const nuint WindowSubclassId = 1;

    private readonly nint _windowHandle;
    private readonly SubclassProcedure _subclassProcedure;
    private readonly ITrimShortcutService _trimShortcutService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<MainWindow> _logger;
    private MainPage? _mainPage;

    public MainPageViewModel ViewModel { get; }

    public MainWindow(
        MainPageViewModel viewModel,
        ITrimShortcutService trimShortcutService,
        ISettingsService settingsService,
        ILogger<MainWindow> logger)
    {
        ViewModel = viewModel;
        _trimShortcutService = trimShortcutService;
        _settingsService = settingsService;
        _logger = logger;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _subclassProcedure = WindowSubclassProcedure;
        if (!SetWindowSubclass(_windowHandle, _subclassProcedure, WindowSubclassId, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        Closed += OnClosed;

        WindowLayout? savedLayout = LoadSavedLayout();
        uint dpi = GetDpiForWindow(_windowHandle);
        AppWindow.Resize(new SizeInt32(
            ScaleDipToPixel(savedLayout?.WidthDip ?? DefaultWidthDip, dpi),
            ScaleDipToPixel(savedLayout?.HeightDip ?? DefaultHeightDip, dpi)));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
            if (savedLayout is { IsMaximized: true })
            {
                presenter.Maximize();
            }
        }

        RootFrame.Content = GetMainPage();
    }

    public void NavigateHome()
    {
        RootFrame.Content = GetMainPage();
    }

    // Главная страница переиспользуется, чтобы возврат из настроек не пересоздавал таймлайн и предпросмотр.
    private MainPage GetMainPage()
    {
        return _mainPage ??= new MainPage(ViewModel, _trimShortcutService);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        SettingsPageViewModel viewModel = App.Services.GetRequiredService<SettingsPageViewModel>();
        RootFrame.Content = new SettingsPage(viewModel);
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        AboutPageViewModel viewModel = App.Services.GetRequiredService<AboutPageViewModel>();
        RootFrame.Content = new AboutPage(viewModel);
    }

    private nint WindowSubclassProcedure(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData)
    {
        if (message == GetMinMaxInfoMessage && lParam != 0)
        {
            MinMaxInfo minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            uint dpi = GetDpiForWindow(windowHandle);
            minMaxInfo.MinTrackSize.X = ScaleDipToPixel(MinimumWidthDip, dpi);
            minMaxInfo.MinTrackSize.Y = ScaleDipToPixel(MinimumHeightDip, dpi);
            Marshal.StructureToPtr(minMaxInfo, lParam, false);
            return 0;
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private WindowLayout? LoadSavedLayout()
    {
        try
        {
            return _settingsService.LoadAsync(CancellationToken.None).GetAwaiter().GetResult().WindowLayout;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось прочитать сохранённый размер окна.");
            return null;
        }
    }

    // Размер окна сохраняется при закрытии, поэтому запись выполняется синхронно.
    private void SaveLayout()
    {
        try
        {
            uint dpi = GetDpiForWindow(_windowHandle);
            bool isMaximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
            AppSettings settings = _settingsService.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            WindowLayout currentLayout = new(
                ScalePixelToDip(AppWindow.Size.Width, dpi),
                ScalePixelToDip(AppWindow.Size.Height, dpi),
                isMaximized);

            // Развёрнутое окно сообщает размер экрана, поэтому для него сохраняется прежний размер и только флаг.
            WindowLayout layout = isMaximized && settings.WindowLayout is { IsValid: true } savedLayout
                ? savedLayout with { IsMaximized = true }
                : currentLayout;

            _settingsService
                .SaveAsync(settings with { WindowLayout = layout }, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось сохранить размер окна.");
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnClosed;
        SaveLayout();
        RootFrame.Content = null;
        _mainPage = null;
        RemoveWindowSubclass(_windowHandle, _subclassProcedure, WindowSubclassId);
    }

    private static int ScaleDipToPixel(int value, uint dpi)
    {
        uint effectiveDpi = dpi == 0 ? 96u : dpi;
        return checked((int)(((long)value * effectiveDpi + 95) / 96));
    }

    private static int ScalePixelToDip(int value, uint dpi)
    {
        uint effectiveDpi = dpi == 0 ? 96u : dpi;
        return checked((int)(((long)value * 96) / effectiveDpi));
    }

    [DllImport("Comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint windowHandle,
        SubclassProcedure subclassProcedure,
        nuint subclassId,
        nuint referenceData);

    [DllImport("Comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        nint windowHandle,
        SubclassProcedure subclassProcedure,
        nuint subclassId);

    [DllImport("Comctl32.dll")]
    private static extern nint DefSubclassProc(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("User32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    private delegate nint SubclassProcedure(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;

        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;

        public NativePoint MaxSize;

        public NativePoint MaxPosition;

        public NativePoint MinTrackSize;

        public NativePoint MaxTrackSize;
    }
}
