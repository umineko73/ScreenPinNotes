using ScreenPinNotes.Services;
using ScreenPinNotes.Models;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Markup;

namespace ScreenPinNotes.Tests;

public class FontCatalogTests
{
    [Fact]
    public async Task FirstLoadCompletesAndUsesJapaneseNames()
    {
        var fonts = await FontCatalog.LoadAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.NotEmpty(fonts);
        foreach (var family in Fonts.SystemFontFamilies)
        {
            if (family.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("ja-jp"), out var name))
                Assert.Contains(fonts, f => f.Source == family.Source && f.DisplayName == name);
        }
        Assert.Same(fonts, await FontCatalog.LoadAsync());
    }

    [Fact]
    public void FontUsageSurvivesSettingsSerialization()
    {
        var settings = new AppSettings();
        settings.FontUsage["Yu Gothic UI"] = 7;
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        restored.Normalize();
        Assert.Equal(7, restored.FontUsage["Yu Gothic UI"]);
    }
}
