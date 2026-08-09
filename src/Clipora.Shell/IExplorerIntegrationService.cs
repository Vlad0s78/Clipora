namespace Clipora.Shell;

public interface IExplorerIntegrationService
{
    bool IsEnabled(string executablePath);

    void SetEnabled(bool enabled, string executablePath, string language);
}
