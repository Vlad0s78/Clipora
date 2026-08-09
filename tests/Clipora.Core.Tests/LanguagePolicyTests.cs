using Clipora.Core;

namespace Clipora.Core.Tests;

public sealed class LanguagePolicyTests
{
    [Theory]
    [InlineData("ru-RU")]
    [InlineData("en-US")]
    public void Resolve_PreservesSupportedManualLanguage(string language)
    {
        Assert.Equal(language, LanguagePolicy.Resolve(language, ["de-DE"]));
    }

    [Theory]
    [InlineData("ru", "ru-RU")]
    [InlineData("ru-BY", "ru-RU")]
    [InlineData("en-US", "en-US")]
    [InlineData("de-DE", "en-US")]
    public void Resolve_UsesFirstPreferredLanguage(string preferredLanguage, string expected)
    {
        Assert.Equal(expected, LanguagePolicy.Resolve(null, [preferredLanguage, "ru-RU"]));
    }

    [Fact]
    public void Resolve_UsesEnglishWhenPreferredLanguagesAreEmpty()
    {
        Assert.Equal(LanguagePolicy.English, LanguagePolicy.Resolve(null, []));
    }
}
