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
