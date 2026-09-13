// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
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
}
