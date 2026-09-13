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

namespace ScreenPinNotes.Tests;

public class StickyNoteTests
{
    // 2026-08-27 is a Thursday.
    private static readonly DateTime Thursday = new(2026, 8, 27, 14, 5, 9);

    [Fact]
    public void CreateDefaultTitle_Japanese_UsesJapaneseDayName()
    {
        var title = StickyNote.CreateDefaultTitle(Thursday);

        Assert.Equal("2026/08/27(木) 14:05:09", title);
    }

    [Fact]
    public void CreateDefaultTitle_EnglishFalse_MatchesJapaneseOverload()
    {
        var title = StickyNote.CreateDefaultTitle(Thursday, english: false);

        Assert.Equal(StickyNote.CreateDefaultTitle(Thursday), title);
    }

    [Fact]
    public void CreateDefaultTitle_EnglishTrue_UsesEnglishDayName()
    {
        var title = StickyNote.CreateDefaultTitle(Thursday, english: true);

        Assert.Equal("2026/08/27(Thu) 14:05:09", title);
    }

    [Fact]
    public void NewNote_HasSensibleDefaults()
    {
        var note = new StickyNote();

        Assert.Equal("yellow", note.ColorKey);
        Assert.Equal(100, note.OpacityPercent);
        Assert.False(note.IsFolded);
        Assert.False(note.IsReadOnly);
        Assert.Null(note.FoldedWidth);
        Assert.False(string.IsNullOrWhiteSpace(note.Id));
    }
}
