using System.ComponentModel;
using System.Runtime.InteropServices;
using Clipora.App.ViewModels;
using Clipora.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Clipora.App;

public sealed partial class MainWindow : Window
{
    private const uint GetMinMaxInfoMessage = 0x0024;
    private const int MinimumWidthDip = 1100;
    private const int MinimumHeightDip = 700;
    private const nuint WindowSubclassId = 1;

    private readonly nint _windowHandle;
    private readonly SubclassProcedure _subclassProcedure;
    private readonly ITrimShortcutService _trimShortcutService;

    public MainPageViewModel ViewModel { get; }

    public MainWindow(
        MainPageViewModel viewModel,
        ITrimShortcutService trimShortcutService)
    {
        ViewModel = viewModel;
        _trimShortcutService = trimShortcutService;
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

        uint dpi = GetDpiForWindow(_windowHandle);
        AppWindow.Resize(new SizeInt32(ScaleDipToPixel(1280, dpi), ScaleDipToPixel(820, dpi)));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
        }

        RootFrame.Content = new MainPage(viewModel, _trimShortcutService);
    }

    public void NavigateHome()
    {
        RootFrame.Content = new MainPage(ViewModel, _trimShortcutService);
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

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnClosed;
        RootFrame.Content = null;
        RemoveWindowSubclass(_windowHandle, _subclassProcedure, WindowSubclassId);
    }

    private static int ScaleDipToPixel(int value, uint dpi)
    {
        uint effectiveDpi = dpi == 0 ? 96u : dpi;
        return checked((int)(((long)value * effectiveDpi + 95) / 96));
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
