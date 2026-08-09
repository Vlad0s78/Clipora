namespace Clipora.Shell;

public sealed class DisabledExplorerIntegrationService : IExplorerIntegrationService
{
    public bool IsEnabled(string executablePath)
    {
        return false;
    }

    public void SetEnabled(bool enabled, string executablePath, string language)
    {
        throw new InvalidOperationException("Explorer integration is disabled in Portable mode.");
    }
}
