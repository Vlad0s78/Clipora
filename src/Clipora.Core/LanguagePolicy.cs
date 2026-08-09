namespace Clipora.Core;

public static class LanguagePolicy
{
    public const string Russian = "ru-RU";

    public const string English = "en-US";

    public static string Resolve(string? persistedLanguage, IEnumerable<string> preferredLanguages)
    {
        ArgumentNullException.ThrowIfNull(preferredLanguages);

        if (persistedLanguage is Russian or English)
        {
            return persistedLanguage;
        }

        string? preferredLanguage = preferredLanguages.FirstOrDefault();
        return preferredLanguage?.StartsWith("ru", StringComparison.OrdinalIgnoreCase) is true
            ? Russian
            : English;
    }
}
