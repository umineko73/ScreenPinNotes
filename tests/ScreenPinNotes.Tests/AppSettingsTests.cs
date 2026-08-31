using System.Globalization;
using ScreenPinNotes.Models;

namespace ScreenPinNotes.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Normalize_NegativeTimings_ClampToZero()
    {
        var settings = new AppSettings();
        settings.Timings.TitlePreviewDelayMs = -100;
        settings.Timings.SaveDebounceMs = -1;

        settings.Normalize();

        Assert.Equal(0, settings.Timings.TitlePreviewDelayMs);
        Assert.Equal(0, settings.Timings.SaveDebounceMs);
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("EN", "EN")] // matches "en" case-insensitively, so it's left as-is (not lowercased)
    [InlineData("ja", "ja")]
    [InlineData("fr", "ja")]
    [InlineData("", "ja")]
    public void Normalize_Language_FallsBackToJapaneseUnlessEnglish(string input, string expected)
    {
        var settings = new AppSettings { Language = input };

        settings.Normalize();

        Assert.Equal(expected, settings.Language);
    }

    [Theory]
    [InlineData("ja-JP", "ja")]
    [InlineData("ja", "ja")]
    [InlineData("en-US", "en")]
    [InlineData("fr-FR", "en")]
    public void GetDefaultLanguage_UsesJapaneseOnlyForJapaneseCulture(string cultureName, string expected)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);

        var language = AppSettings.GetDefaultLanguage(culture);

        Assert.Equal(expected, language);
    }

    [Theory]
    [InlineData("Dark", "Dark")]
    [InlineData("dark", "Dark")]
    [InlineData("Light", "Light")]
    [InlineData("neon", "Light")]
    public void Normalize_Theme_FallsBackToLightUnlessDark(string input, string expected)
    {
        var settings = new AppSettings { Theme = input };

        settings.Normalize();

        Assert.Equal(expected, settings.Theme);
    }

    [Fact]
    public void Normalize_EmptyIconPalette_RefillsWithDefaults()
    {
        var settings = new AppSettings { IconPalette = [] };

        settings.Normalize();

        Assert.Equal(AppSettings.DefaultIconPalette().Count, settings.IconPalette.Count);
        Assert.NotEmpty(settings.IconPalette);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(10, 10)]
    [InlineData(90, 90)]
    [InlineData(150, 90)]
    public void Normalize_HoverOpacityBoostPercent_ClampedTo0To90(int input, int expected)
    {
        var settings = new AppSettings { HoverOpacityBoostPercent = input };

        settings.Normalize();

        Assert.Equal(expected, settings.HoverOpacityBoostPercent);
    }

    [Fact]
    public void Normalize_DefaultNoteWidthBelowMinWidth_RaisedToMinWidth()
    {
        var settings = new AppSettings();
        settings.Layout.UnfoldedMinWidth = 200;
        settings.Layout.DefaultNoteWidth = 100;

        settings.Normalize();

        Assert.Equal(200, settings.Layout.DefaultNoteWidth);
    }
}
