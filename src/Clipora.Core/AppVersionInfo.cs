using System.Reflection;

namespace Clipora.Core;

public sealed record AppVersionInfo(string Version, string Build, string Developer)
{
    public const string ProductName = "Clipora";

    public static AppVersionInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        string version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+', 2)[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        Dictionary<string, string> metadata = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Value is not null)
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value!, StringComparer.Ordinal);

        return new AppVersionInfo(
            version,
            metadata.GetValueOrDefault("Build", "0"),
            metadata.GetValueOrDefault("Developer", "Vlad0s"));
    }
}
