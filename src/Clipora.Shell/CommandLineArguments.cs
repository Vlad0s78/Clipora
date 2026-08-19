using System.Runtime.InteropServices;

namespace Clipora.Shell;

/// <summary>
/// Splits a Windows command line into arguments the same way the shell does.
/// </summary>
public static class CommandLineArguments
{
    public static IReadOnlyList<string> Split(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);

        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        nint argumentsPointer = CommandLineToArgvW(commandLine, out int count);
        if (argumentsPointer == 0)
        {
            return [];
        }

        try
        {
            List<string> arguments = new(count);
            for (int index = 0; index < count; index++)
            {
                nint entry = Marshal.ReadIntPtr(argumentsPointer, index * nint.Size);
                arguments.Add(Marshal.PtrToStringUni(entry) ?? string.Empty);
            }

            return arguments;
        }
        finally
        {
            _ = LocalFree(argumentsPointer);
        }
    }

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("Kernel32.dll", SetLastError = true)]
    private static extern nint LocalFree(nint memory);
}
