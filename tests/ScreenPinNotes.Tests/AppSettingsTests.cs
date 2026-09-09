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

    [Fact]
    public void Defaults_TitleBarViewToggleUsesDoubleClick()
    {
        var settings = new AppSettings();

        Assert.True(settings.DoubleClickToToggleView);
    }

    [Fact]
    public void Defaults_MaxNoteContentBytes_IsOneMegabyte()
    {
        var settings = new AppSettings();

        Assert.Equal(1024 * 1024, settings.MaxNoteContentBytes);
    }

    [Fact]
    public void Normalize_MaxNoteContentBytes_AtLeastOneKilobyte()
    {
        var settings = new AppSettings { MaxNoteContentBytes = 10 };

        settings.Normalize();

        Assert.Equal(1024, settings.MaxNoteContentBytes);
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("EN", "en")]
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

    [Theory]
    [InlineData("NewNote", "NewNote")]
    [InlineData("newnote", "NewNote")]
    [InlineData("ToggleAll", "ToggleAll")]
    [InlineData("", "ToggleAll")]
    [InlineData("garbage", "ToggleAll")]
    public void Normalize_TrayClickAction_FallsBackToToggleAllUnlessNewNote(string input, string expected)
    {
        var settings = new AppSettings { TrayClickAction = input };

        settings.Normalize();

        Assert.Equal(expected, settings.TrayClickAction);
    }

    // 動物を増やしたら、保存済みのパレットにも不足分が配られること。
    // IconPaletteVersion を上げ忘れると既存ユーザーには増えない。
    [Fact]
    public void Normalize_OlderPaletteVersion_GainsTheNewAnimals()
    {
        var animals = AppSettings.IconGroups.Single(group => group.Key == "IconAnimals").Icons;
        var settings = new AppSettings
        {
            IconPalette = ["📌", "🐶"],
            IconPaletteVersion = 1,
        };

        settings.Normalize();

        foreach (var animal in animals)
            Assert.Contains(animal, settings.IconPalette);
        Assert.Contains("📌", settings.IconPalette);
        Assert.Equal(settings.IconPalette.Count, settings.IconPalette.Distinct().Count());
        Assert.True(settings.IconPaletteVersion >= 2);
    }

    // 既に最新なら触らない。利用者が外したアイコンを毎回戻さないため。
    [Fact]
    public void Normalize_CurrentPaletteVersion_IsLeftAlone()
    {
        var settings = new AppSettings
        {
            IconPalette = ["📌", "🐶"],
            IconPaletteVersion = 2,
        };

        settings.Normalize();

        Assert.Equal(["📌", "🐶"], settings.IconPalette);
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

    // 目印は既定で出す。設定を持たない古い settings.json でも出るようにする。
    [Fact]
    public void Defaults_TitleBarHiddenSpine_IsShownAtThreePixels()
    {
        var settings = new AppSettings();

        Assert.True(settings.ShowTitleBarHiddenSpine);
        Assert.Equal(3, settings.Layout.TitleBarHiddenSpineWidth);
    }

    [Fact]
    public void Defaults_NoteFrame_KeepsTheRoundedGreyLook()
    {
        var settings = new AppSettings();

        Assert.Equal(6, settings.Layout.NoteCornerRadius);
        Assert.Equal(AppSettings.NoteBorderGray, settings.NoteBorderColor);
        Assert.False(settings.MonochromeIcons);
    }

    [Theory]
    [InlineData(-2, 0)]
    [InlineData(0, 0)]
    [InlineData(6, 6)]
    [InlineData(16, 16)]
    [InlineData(40, 16)]
    public void Normalize_NoteCornerRadius_ClampedTo0To16(double input, double expected)
    {
        var settings = new AppSettings();
        settings.Layout.NoteCornerRadius = input;

        settings.Normalize();

        Assert.Equal(expected, settings.Layout.NoteCornerRadius);
    }

    // 決め打ちの3種類は表記ゆれを吸収し、色は "#RRGGBB" だけ通す。
    [Theory]
    [InlineData("Gray", "Gray")]
    [InlineData("gray", "Gray")]
    [InlineData("none", "None")]
    [InlineData("notecolor", "NoteColor")]
    [InlineData("#ff3366", "#FF3366")]
    [InlineData("#abc", "#ABC")]
    [InlineData("#12345", "Gray")]
    [InlineData("cornflowerblue", "Gray")]
    [InlineData("", "Gray")]
    [InlineData(null, "Gray")]
    public void Normalize_NoteBorderColor_KeepsKnownNamesAndHexOnly(string? input, string expected)
    {
        var settings = new AppSettings { NoteBorderColor = input! };

        settings.Normalize();

        Assert.Equal(expected, settings.NoteBorderColor);
    }

    // 0 は「出さない」と見分けが付かず、太すぎると本文を圧迫する。
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(1, 1)]
    [InlineData(6, 6)]
    [InlineData(12, 12)]
    [InlineData(40, 12)]
    public void Normalize_TitleBarHiddenSpineWidth_ClampedTo1To12(double input, double expected)
    {
        var settings = new AppSettings();
        settings.Layout.TitleBarHiddenSpineWidth = input;

        settings.Normalize();

        Assert.Equal(expected, settings.Layout.TitleBarHiddenSpineWidth);
    }
}
