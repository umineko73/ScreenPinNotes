// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3 of the License.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
