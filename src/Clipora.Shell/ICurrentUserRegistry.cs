namespace Clipora.Shell;

public interface ICurrentUserRegistry
{
    string? GetString(string subKeyPath, string? valueName);

    void SetString(string subKeyPath, string? valueName, string value);

    void DeleteSubKeyTree(string subKeyPath);
}
