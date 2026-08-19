using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Clipora.App;

/// <summary>
/// Точка входа с перенаправлением повторных запусков в уже открытое окно.
/// </summary>
public static class Program
{
    private const string InstanceKey = "Clipora.SingleInstance";
    private const uint InfiniteTimeout = 0xFFFFFFFF;

    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (TryRedirectToPrimaryInstance())
        {
            return;
        }

        Application.Start(parameters =>
        {
            DispatcherQueueSynchronizationContext context = new(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }

    // Сбой перенаправления не должен мешать запуску: приложение просто откроется вторым окном.
    private static bool TryRedirectToPrimaryInstance()
    {
        try
        {
            AppInstance primaryInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (primaryInstance.IsCurrent)
            {
                primaryInstance.Activated += (_, arguments) => App.HandleRedirectedActivation(arguments);
                return false;
            }

            RedirectActivationTo(AppInstance.GetCurrent().GetActivatedEventArgs(), primaryInstance);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Ожидание завершения перенаправления через COM-совместимое ожидание: STA-поток не должен блокироваться.
    private static void RedirectActivationTo(AppActivationArguments arguments, AppInstance primaryInstance)
    {
        nint redirectEvent = CreateEvent(0, initialState: true, manualReset: false, name: null);

        _ = Task.Run(() =>
        {
            primaryInstance.RedirectActivationToAsync(arguments).AsTask().GetAwaiter().GetResult();
            SetEvent(redirectEvent);
        });

        _ = CoWaitForMultipleObjects(0, InfiniteTimeout, 1, [redirectEvent], out _);
        CloseHandle(redirectEvent);
    }

    [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateEvent(
        nint eventAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool initialState,
        [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        string? name);

    [DllImport("Kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(nint eventHandle);

    [DllImport("Kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("Ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(
        uint flags,
        uint timeout,
        uint handleCount,
        nint[] handles,
        out uint handleIndex);
}
