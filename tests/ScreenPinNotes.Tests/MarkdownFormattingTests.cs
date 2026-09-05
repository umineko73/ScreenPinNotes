using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class MarkdownFormattingTests
{
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(0, 999)]
    public void InvalidRangesLeaveOriginalUntouched(int start, int length)
    {
        foreach (var edit in new[] { MarkdownFormatting.Inline("abc", start, length, "**"), MarkdownFormatting.Lines("abc", start, length, "# ") })
            Assert.Equal("abc", "abc".Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement));
    }
    [Theory]
    [InlineData("**")]
    [InlineData("~~")]
    [InlineData("`")]
    public void InlineRoundTripPreservesSelection(string marker)
    {
        var edit = MarkdownFormatting.Inline("abc", 1, 1, marker);
        var text = "abc".Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        var undo = MarkdownFormatting.Inline(text, edit.SelectionStart, edit.SelectionLength, marker);
        Assert.Equal("abc", text.Remove(undo.Start, undo.Length).Insert(undo.Start, undo.Replacement));
    }

    [Fact]
    public void EmptySelectionPlacesCaretInsideMarkers()
    {
        var edit = MarkdownFormatting.Inline("abc", 1, 0, "**");
        Assert.Equal("****", edit.Replacement);
        Assert.Equal(3, edit.SelectionStart);
        Assert.Equal(0, edit.SelectionLength);
    }

    [Fact]
    public void LinesPreserveCrLfAndDoNotIncludeNextLine()
    {
        var edit = MarkdownFormatting.Lines("a\r\nb\r\nc", 0, 6, "- [ ] ");
        Assert.Equal("- [ ] a\r\n- [ ] b", edit.Replacement);
        var undo = MarkdownFormatting.Lines(edit.Replacement, 0, edit.Replacement.Length, "- [ ] ");
        Assert.Equal("a\r\nb", undo.Replacement);
    }

    [Fact]
    public void HeadingReplacesExistingHeading()
        => Assert.Equal("## title", MarkdownFormatting.Lines("# title", 3, 0, "## ").Replacement);
}
