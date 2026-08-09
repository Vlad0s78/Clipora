using Clipora.Core;

namespace Clipora.Core.Tests;

public sealed class AppVersionInfoTests
{
    [Fact]
    public void FromAssembly_ReadsCentralVersionAndBuild()
    {
        AppVersionInfo version = AppVersionInfo.FromAssembly(typeof(AppVersionInfo).Assembly);

        Assert.Equal("0.1.0", version.Version);
        Assert.Equal("1", version.Build);
        Assert.Equal("Vlad0s", version.Developer);
    }
}
