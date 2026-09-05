using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class LocalizationServiceTests
{
    [Theory]
    [InlineData("ja", "はい")]
    [InlineData("ja-JP", "はい")]
    [InlineData("en-US", "Yes")]
    [InlineData("fr", "Yes")]
    public void TranslatesWithParentAndEnglishFallback(string culture, string expected)
        => Assert.Equal(expected, LocalizationService.T("Yes", culture));

    [Fact]
    public void DiscoversCompiledJapaneseResources()
    {
        Assert.Contains(LocalizationService.Languages, language => language.Code == "ja");
        Assert.Contains(LocalizationService.Languages, language => language.Code == "en");
        Assert.Equal("UnknownKey", LocalizationService.T("UnknownKey", "ja"));
    }

    [Fact]
    public void ResourcePlaceholdersRemainUsable()
    {
        Assert.Equal("本文 15pt", string.Format(LocalizationService.T("BodySize", "ja"), 15));
        Assert.Equal("Body 15pt", string.Format(LocalizationService.T("BodySize", "en"), 15));
    }
}
