namespace Clipora.Shell;

public sealed class ExplorerIntegrationService : IExplorerIntegrationService
{
    private const string RussianLanguage = "ru-RU";
    private const string OpenKeyName = "Clipora.Open";
    private const string CompressKeyName = "Clipora.Compress";
    private static readonly string[] SupportedExtensions = [".mp4", ".mkv", ".mov", ".avi", ".webm"];

    private readonly ICurrentUserRegistry _registry;

    public ExplorerIntegrationService()
        : this(new CurrentUserRegistry())
    {
    }

    public ExplorerIntegrationService(ICurrentUserRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public bool IsEnabled(string executablePath)
    {
        string normalizedExecutablePath = NormalizeExecutablePath(executablePath);
        foreach (string extension in SupportedExtensions)
        {
            if (!HasExpectedCommand(extension, OpenKeyName, normalizedExecutablePath, compress: false) ||
                !HasExpectedCommand(extension, CompressKeyName, normalizedExecutablePath, compress: true))
            {
                return false;
            }
        }

        return true;
    }

    public void SetEnabled(bool enabled, string executablePath, string language)
    {
        if (!enabled)
        {
            Unregister();
            return;
        }

        string normalizedExecutablePath = NormalizeExecutablePath(executablePath);
        string openLabel = string.Equals(language, RussianLanguage, StringComparison.OrdinalIgnoreCase)
            ? "Открыть в Clipora"
            : "Open in Clipora";
        string compressLabel = string.Equals(language, RussianLanguage, StringComparison.OrdinalIgnoreCase)
            ? "Сжать с помощью Clipora"
            : "Compress with Clipora";

        try
        {
            foreach (string extension in SupportedExtensions)
            {
                RegisterCommand(extension, OpenKeyName, openLabel, normalizedExecutablePath, compress: false);
                RegisterCommand(extension, CompressKeyName, compressLabel, normalizedExecutablePath, compress: true);
            }
        }
        catch
        {
            Unregister();
            throw;
        }
    }

    private void RegisterCommand(
        string extension,
        string keyName,
        string label,
        string executablePath,
        bool compress)
    {
        string keyPath = GetOwnedKeyPath(extension, keyName);
        _registry.SetString(keyPath, valueName: null, label);
        _registry.SetString(keyPath, "Icon", $"{Quote(executablePath)},0");
        _registry.SetString(keyPath, "MultiSelectModel", "Single");

        string command = BuildCommand(executablePath, compress);
        _registry.SetString($"{keyPath}\\command", valueName: null, command);
    }

    private bool HasExpectedCommand(
        string extension,
        string keyName,
        string executablePath,
        bool compress)
    {
        string commandKeyPath = $"{GetOwnedKeyPath(extension, keyName)}\\command";
        return string.Equals(
            _registry.GetString(commandKeyPath, valueName: null),
            BuildCommand(executablePath, compress),
            StringComparison.Ordinal);
    }

    private void Unregister()
    {
        foreach (string extension in SupportedExtensions)
        {
            _registry.DeleteSubKeyTree(GetOwnedKeyPath(extension, OpenKeyName));
            _registry.DeleteSubKeyTree(GetOwnedKeyPath(extension, CompressKeyName));
        }
    }

    private static string GetOwnedKeyPath(string extension, string keyName)
    {
        return $"Software\\Classes\\SystemFileAssociations\\{extension}\\shell\\{keyName}";
    }

    private static string NormalizeExecutablePath(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        string fullPath = Path.GetFullPath(executablePath);
        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Clipora executable path must point to an .exe file.", nameof(executablePath));
        }

        return fullPath;
    }

    private static string Quote(string value)
    {
        return $"\"{value}\"";
    }

    private static string BuildCommand(string executablePath, bool compress)
    {
        return compress
            ? $"{Quote(executablePath)} --compress \"%1\""
            : $"{Quote(executablePath)} \"%1\"";
    }
}
