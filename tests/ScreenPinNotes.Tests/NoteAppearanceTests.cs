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

using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using System.Windows.Media;

namespace ScreenPinNotes.Tests;

public class NoteAppearanceTests
{
    [Theory]
    [InlineData(50, 20, false, false, 128)]
    [InlineData(50, 20, true, false, 178)]
    [InlineData(90, 20, true, false, 255)]
    [InlineData(10, 20, false, true, 255)]
    [InlineData(-10, 0, false, false, 26)]
    public void OpacityAppliesToPanelsButKeepsTextReadable(
        int opacity, int boost, bool hovered, bool forceOpaque, byte expectedAlpha)
    {
        var appearance = new NoteAppearance(
            new StickyNote { OpacityPercent = opacity },
            new AppSettings { HoverOpacityBoostPercent = boost },
            forceOpaque, hovered);

        Assert.Equal(expectedAlpha, Assert.IsType<SolidColorBrush>(appearance.BackgroundBrush).Color.A);
        Assert.Equal(expectedAlpha, Assert.IsType<SolidColorBrush>(appearance.HeaderBrush).Color.A);
        Assert.Equal((byte)255, Assert.IsType<SolidColorBrush>(appearance.TextForeground).Color.A);
    }

    [Fact]
    public void UnknownPresetFallsBackToYellow()
    {
        var settings = new AppSettings();
        var unknown = new NoteAppearance(new StickyNote { ColorKey = "unknown" }, settings);
        var yellow = new NoteAppearance(new StickyNote { ColorKey = "yellow" }, settings);

        Assert.Equal(Assert.IsType<SolidColorBrush>(yellow.BackgroundBrush).Color,
            Assert.IsType<SolidColorBrush>(unknown.BackgroundBrush).Color);
    }

    [Fact]
    public void PaletteHasTenLightAndTenDarkColors()
    {
        Assert.Equal(10, NoteAppearance.LightPresetKeys.Count());
        Assert.Equal(10, NoteAppearance.DarkPresetKeys.Count());
        foreach (var key in NoteAppearance.LightPresetKeys)
            Assert.False(NoteAppearance.UsesDarkColors(new StickyNote { ColorKey = key }), key);
        foreach (var key in NoteAppearance.DarkPresetKeys)
            Assert.True(NoteAppearance.UsesDarkColors(new StickyNote { ColorKey = key }), key);
    }

    // ライトの色はアプリをダークテーマにしても暗く塗り替えない。
    [Fact]
    public void LightPresetsKeepTheirColorsUnderTheDarkTheme()
    {
        foreach (var key in NoteAppearance.LightPresetKeys)
        {
            var note = new StickyNote { ColorKey = key };
            var light = new NoteAppearance(note, new AppSettings { Theme = "Light" });
            var dark = new NoteAppearance(note, new AppSettings { Theme = "Dark" });

            Assert.Equal(ColorOf(light.BackgroundBrush), ColorOf(dark.BackgroundBrush));
            Assert.Equal(ColorOf(light.TitleBarBrush), ColorOf(dark.TitleBarBrush));
            Assert.Equal(ColorOf(light.TextForeground), ColorOf(dark.TextForeground));
        }
    }

    // パレットから外した色の付箋も、保存したときの色のまま描く。
    [Fact]
    public void RemovedPresetsStillRenderTheirOriginalColors()
    {
        var amber = new NoteAppearance(new StickyNote { ColorKey = "amber", OpacityPercent = 100 }, new AppSettings());

        Assert.DoesNotContain("amber", NoteAppearance.Presets.Keys);
        Assert.Equal("#FFFEF3C7", ColorOf(amber.BackgroundBrush).ToString());
        Assert.Equal("#FFB45309", ColorOf(amber.HeaderBrush).ToString());
    }

    [Theory]
    [InlineData("#FFF0F5", "#C71585", false, 0.10)]
    [InlineData("#203040", "#FFCC00", true, 0.30)]
    public void CustomColors_TitleBarIsTheBackgroundMadeSlightlyDarker(
        string background, string accent, bool dark, double darken)
    {
        var note = new StickyNote
        {
            ColorKey = NoteAppearance.CustomColorKey, OpacityPercent = 100,
            CustomBackgroundColor = background, CustomAccentColor = accent,
        };
        var appearance = new NoteAppearance(note, new AppSettings());
        var bg = (Color)ColorConverter.ConvertFromString(background);
        byte Darker(byte v) => (byte)Math.Round(v * (1 - darken));

        Assert.Equal(bg, ColorOf(appearance.BackgroundBrush));
        Assert.Equal((Color)ColorConverter.ConvertFromString(accent), ColorOf(appearance.HeaderBrush));
        Assert.Equal(Color.FromRgb(Darker(bg.R), Darker(bg.G), Darker(bg.B)), ColorOf(appearance.TitleBarBrush));
        Assert.Equal(dark, NoteAppearance.UsesDarkColors(note));
        AssertReadable(appearance.TitleBarBrush, appearance.TitleBarForeground);
        AssertReadable(appearance.BackgroundBrush, appearance.TextForeground);
    }

    [Fact]
    public void CustomColors_FallBackToYellowWhenUnreadable()
    {
        var settings = new AppSettings();
        var broken = new NoteAppearance(new StickyNote { ColorKey = NoteAppearance.CustomColorKey, CustomBackgroundColor = "nope" }, settings);
        var yellow = new NoteAppearance(new StickyNote { ColorKey = "yellow" }, settings);

        Assert.Equal(ColorOf(yellow.BackgroundBrush), ColorOf(broken.BackgroundBrush));
    }

    private static Color ColorOf(System.Windows.Media.Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    private static void AssertReadable(System.Windows.Media.Brush background, System.Windows.Media.Brush foreground)
    {
        static double Brightness(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
        Assert.True(Math.Abs(Brightness(ColorOf(background)) - Brightness(ColorOf(foreground))) > 0.45);
    }
}
