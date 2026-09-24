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

using System.Text.Json;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class NoteDuplicatorTests
{
    private static StickyNote Source() => new()
    {
        Content = "# body\n![](assets/pic.png)",
        Title = "Original",
        X = 100, Y = 200, Width = 320, Height = 240,
        FoldedX = 150, FoldedY = 250, FoldedWidth = 180,
        EditWidth = 400, EditHeight = 300,
        ColorKey = "sky", Icon = "📝", FontFamily = "Meiryo", FontSize = 15, TitleFontSize = 14,
        OpacityPercent = 70, IsTopmost = true, LayerOrder = 3,
        IsFolded = true, IsReadOnly = true, IsPositionSeparated = true, IsTitleBarHidden = true,
        PositionLayout = "layout", PositionScale = 1.5,
        ExternalContentPath = @"D:\logs\app.log", ExternalTailMode = true,
        ExternalImageWidthOverrides = { ["1:0:pic.png"] = 120 },
        Reminder = new ReminderSettings { NextAt = new DateTime(2026, 9, 17, 9, 0, 0) },
        CreatedAt = new DateTime(2026, 1, 1), UpdatedAt = new DateTime(2026, 1, 2),
    };

    [Fact]
    public void Create_KeepsContentAndAppearance()
    {
        var source = Source();

        var copy = NoteDuplicator.Create(source, 24, -5, new DateTime(2026, 9, 16, 12, 0, 0));

        Assert.Equal(source.Content, copy.Content);
        Assert.Equal(source.Title, copy.Title);
        Assert.Equal((320d, 240d, 180d, 400d, 300d), (copy.Width, copy.Height, copy.FoldedWidth!.Value, copy.EditWidth!.Value, copy.EditHeight!.Value));
        Assert.Equal(("sky", "📝", "Meiryo", 15d, 14d, 70), (copy.ColorKey, copy.Icon, copy.FontFamily, copy.FontSize, copy.TitleFontSize, copy.OpacityPercent));
        Assert.True(copy.IsTopmost && copy.IsFolded && copy.IsReadOnly && copy.IsPositionSeparated && copy.IsTitleBarHidden);
        Assert.Equal(("layout", 1.5), (copy.PositionLayout, copy.PositionScale));
        Assert.Equal((@"D:\logs\app.log", true), (copy.ExternalContentPath, copy.ExternalTailMode));
        Assert.Equal(120, copy.ExternalImageWidthOverrides["1:0:pic.png"]);
    }

    [Fact]
    public void Create_GivesTheCopyItsOwnIdentityPlaceAndTime()
    {
        var source = Source();
        source.IsHidden = true;
        var now = new DateTime(2026, 9, 16, 12, 0, 0);

        var copy = NoteDuplicator.Create(source, 24, -5, now);

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal((124d, 224d), (copy.X, copy.Y));
        Assert.Equal((174d, 274d), (copy.FoldedX!.Value, copy.FoldedY!.Value));
        Assert.Equal(-5, copy.LayerOrder);
        Assert.False(copy.IsHidden);
        // 同じ予定の通知が2回届かないように。
        Assert.Null(copy.Reminder);
        Assert.Equal((now, now), (copy.CreatedAt, copy.UpdatedAt));
    }

    [Fact]
    public void Create_LeavesTheSourceUntouchedAndSharesNothingMutable()
    {
        var source = Source();
        var before = JsonSerializer.Serialize(source);

        var copy = NoteDuplicator.Create(source, 24, -5, DateTime.Now);
        copy.ExternalImageWidthOverrides["1:0:pic.png"] = 999;

        Assert.Equal(before, JsonSerializer.Serialize(source));
        Assert.NotNull(source.Reminder);
    }

    [Fact]
    public void Create_KeepsAnUnsetFoldedPositionUnset()
    {
        var source = new StickyNote { X = 10, Y = 20 };

        var copy = NoteDuplicator.Create(source, 24, 0, DateTime.Now);

        Assert.Null(copy.FoldedX);
        Assert.Null(copy.FoldedY);
    }

    [Theory]
    [InlineData("ja", "付箋を複製")]
    [InlineData("en", "Duplicate note")]
    public void MenuText_IsLocalized(string language, string expected)
        => Assert.Equal(expected, LocalizationService.T("DuplicateNote", language));
}
