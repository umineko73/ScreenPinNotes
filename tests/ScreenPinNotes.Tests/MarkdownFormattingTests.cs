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

    // 選択の先頭と末尾がマーカーでも、それが1組の対とは限らない。外側だけ
    // 外すと "**a** and **b**" が "a** and **b" に壊れていた。
    [Theory]
    [InlineData("**a** and **b**", "**", "a and b")]
    [InlineData("`a` and `b`", "`", "a and b")]
    [InlineData("~~a~~ x ~~b~~", "~~", "a x b")]
    public void InlineRemovesEveryMarkerWhenEndsAreNotOnePair(string text, string marker, string expected)
    {
        var edit = MarkdownFormatting.Inline(text, 0, text.Length, marker);
        Assert.Equal(expected, text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement));
    }

    [Fact]
    public void InlineStillUnwrapsASinglePair()
        => Assert.Equal("bold", MarkdownFormatting.Inline("**bold**", 0, 8, "**").Replacement);

    // "- [ ] task" は "- " で始まるが箇条書きではない。prefix.Length だけ
    // 削ると "[ ] task" という文字列が本文に残っていた。
    [Fact]
    public void BulletsConvertTaskLineInsteadOfLeavingCheckboxText()
        => Assert.Equal("- task", MarkdownFormatting.Lines("- [ ] task", 0, 0, "- ").Replacement);

    [Fact]
    public void BulletsStillToggleOffAPlainBullet()
        => Assert.Equal("task", MarkdownFormatting.Lines("- task", 0, 0, "- ").Replacement);

    // チェック済みの行もチェックリスト書式として扱う。以前は「未チェックで
    // 付け直す」経路に落ちて [x] が黙って消えていた。
    [Theory]
    [InlineData("- [x] a", "a")]
    [InlineData("- [ ] a", "a")]
    public void ChecklistTogglesOffWithoutLosingCheckedState(string line, string expected)
        => Assert.Equal(expected, MarkdownFormatting.Lines(line, 0, 0, "- [ ] ").Replacement);

    [Fact]
    public void ChecklistOnMixedListRemovesItRatherThanUncheckingIt()
        => Assert.Equal("a\nb", MarkdownFormatting.Lines("- [ ] a\n- [x] b", 0, 15, "- [ ] ").Replacement);

    // 選択に空行が混ざっているだけで、解除が「空行へのマーカー追加」に
    // 化けていた。空行は判定にも書き換えにも含めない。
    [Fact]
    public void BlankLinesAreLeftAloneAndDoNotBlockRemoval()
    {
        Assert.Equal("a\n\nb", MarkdownFormatting.Lines("- a\n\n- b", 0, 8, "- ").Replacement);
        Assert.Equal("- a\n\n- b", MarkdownFormatting.Lines("a\n\nb", 0, 4, "- ").Replacement);
    }

    [Fact]
    public void CaretOnAnEmptyLineStillAddsTheMarker()
        => Assert.Equal("# ", MarkdownFormatting.Lines("", 0, 0, "# ").Replacement);

    // 字下げされた行はマーカーが認識されず "-   - nested" と二重になっていた。
    [Fact]
    public void IndentedListItemsKeepTheirIndent()
    {
        Assert.Equal("  nested", MarkdownFormatting.Lines("  - nested", 0, 0, "- ").Replacement);
        Assert.Equal("  - nested", MarkdownFormatting.Lines("  nested", 0, 0, "- ").Replacement);
    }

    // 見出しは行頭になければ描画されないので、付けるときは字下げを落とす。
    [Fact]
    public void HeadingsAreAddedAtTheStartOfTheLine()
        => Assert.Equal("# title", MarkdownFormatting.Lines("  title", 0, 0, "# ").Replacement);
}
