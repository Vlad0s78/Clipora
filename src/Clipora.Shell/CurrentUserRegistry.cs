using Microsoft.Win32;

namespace Clipora.Shell;

public sealed class CurrentUserRegistry : ICurrentUserRegistry
{
    public string? GetString(string subKeyPath, string? valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
        return key?.GetValue(valueName ?? string.Empty, defaultValue: null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            as string;
    }

    public void SetString(string subKeyPath, string? valueName, string value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true);
        key.SetValue(valueName ?? string.Empty, value, RegistryValueKind.String);
    }

    public void DeleteSubKeyTree(string subKeyPath)
    {
        Registry.CurrentUser.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
    }
}
