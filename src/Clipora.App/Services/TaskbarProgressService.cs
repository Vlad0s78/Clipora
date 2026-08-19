using System.Runtime.InteropServices;

namespace Clipora.App.Services;

/// <summary>
/// Показывает ход операции на кнопке приложения в панели задач Windows.
/// Ошибки COM намеренно не всплывают: индикатор не должен ломать саму операцию.
/// </summary>
public sealed class TaskbarProgressService : ITaskbarProgressService
{
    private const ulong ProgressScale = 1000;

    private static readonly Guid TaskbarListClassId = new("56FDF344-FD6D-11d0-958A-006097C9A090");

    private ITaskbarList3? _taskbarList;
    private bool _isUnavailable;

    public void SetProgress(double percent)
    {
        ITaskbarList3? taskbarList = GetTaskbarList();
        if (taskbarList is null)
        {
            return;
        }

        ulong value = (ulong)Math.Clamp(Math.Round(percent * ProgressScale / 100d), 0d, ProgressScale);

        try
        {
            taskbarList.SetProgressState(App.WindowHandle, TaskbarProgressState.Normal);
            taskbarList.SetProgressValue(App.WindowHandle, value, ProgressScale);
        }
        catch (COMException)
        {
            _isUnavailable = true;
        }
    }

    public void Clear()
    {
        ITaskbarList3? taskbarList = GetTaskbarList();
        if (taskbarList is null)
        {
            return;
        }

        try
        {
            taskbarList.SetProgressState(App.WindowHandle, TaskbarProgressState.NoProgress);
        }
        catch (COMException)
        {
            _isUnavailable = true;
        }
    }

    private ITaskbarList3? GetTaskbarList()
    {
        if (_isUnavailable)
        {
            return null;
        }

        if (_taskbarList is not null)
        {
            return _taskbarList;
        }

        try
        {
            Type? taskbarType = Type.GetTypeFromCLSID(TaskbarListClassId);
            if (taskbarType is null || Activator.CreateInstance(taskbarType) is not ITaskbarList3 taskbarList)
            {
                _isUnavailable = true;
                return null;
            }

            taskbarList.HrInit();
            _taskbarList = taskbarList;
            return _taskbarList;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or NotSupportedException)
        {
            _isUnavailable = true;
            return null;
        }
    }

    private enum TaskbarProgressState
    {
        NoProgress = 0,
        Normal = 2,
    }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // Порядок объявления обязан повторять vtable ITaskbarList/ITaskbarList2/ITaskbarList3.
        void HrInit();

        void AddTab(nint windowHandle);

        void DeleteTab(nint windowHandle);

        void ActivateTab(nint windowHandle);

        void SetActiveAlt(nint windowHandle);

        void MarkFullscreenWindow(nint windowHandle, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);

        void SetProgressValue(nint windowHandle, ulong completed, ulong total);

        void SetProgressState(nint windowHandle, TaskbarProgressState state);
    }
}
