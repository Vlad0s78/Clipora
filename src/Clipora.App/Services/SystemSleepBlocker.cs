using System.Runtime.InteropServices;

namespace Clipora.App.Services;

/// <summary>
/// Не даёт системе уснуть, пока выполняется кодирование. Дисплею гаснуть не мешает.
/// </summary>
internal static class SystemSleepBlocker
{
    public static void Acquire()
    {
        _ = SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired);
    }

    public static void Release()
    {
        _ = SetThreadExecutionState(ExecutionState.Continuous);
    }

    [DllImport("Kernel32.dll")]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState executionState);

    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        Continuous = 0x80000000,
    }
}
