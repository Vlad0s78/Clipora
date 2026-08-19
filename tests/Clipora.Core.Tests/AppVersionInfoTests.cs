using System.Xml.Linq;
using Clipora.Core;

namespace Clipora.Core.Tests;

public sealed class AppVersionInfoTests
{
    [Fact]
    public void FromAssembly_ReadsCentralVersionAndBuild()
    {
        XElement properties = LoadDirectoryBuildProperties();
        string expectedVersion = properties.Element("VersionPrefix")!.Value;
        string expectedBuild = properties.Element("CliporaBuild")!.Value;

        AppVersionInfo version = AppVersionInfo.FromAssembly(typeof(AppVersionInfo).Assembly);

        // Значения сверяются с Directory.Build.props: подъём версии не должен ломать тест.
        Assert.Equal(expectedVersion, version.Version);
        Assert.Equal(expectedBuild, version.Build);
        Assert.Equal("Vlad0s", version.Developer);
    }

    private static XElement LoadDirectoryBuildProperties()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "Directory.Build.props");
            if (File.Exists(candidate))
            {
                return XDocument.Load(candidate).Root!.Element("PropertyGroup")!;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Directory.Build.props was not found above the test output directory.");
    }
}
