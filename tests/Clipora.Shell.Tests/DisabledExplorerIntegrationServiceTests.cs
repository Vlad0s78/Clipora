using Clipora.Shell;

namespace Clipora.Shell.Tests;

public sealed class DisabledExplorerIntegrationServiceTests
{
    [Fact]
    public void IsEnabled_AlwaysReturnsFalse()
    {
        DisabledExplorerIntegrationService service = new();

        Assert.False(service.IsEnabled(@"C:\Portable\Clipora.exe"));
    }

    [Fact]
    public void SetEnabled_RejectsRegistryChanges()
    {
        DisabledExplorerIntegrationService service = new();

        Assert.Throws<InvalidOperationException>(() =>
            service.SetEnabled(true, @"C:\Portable\Clipora.exe", "en-US"));
    }
}
