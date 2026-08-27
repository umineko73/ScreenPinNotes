using ScreenStickyNotes.Services;

namespace ScreenStickyNotes.Tests;

public class MarkdownTableClipboardTests
{
    [Fact]
    public void TryTabularTextToMarkdownTable_WithHeaderRow_ProducesHeaderAndSeparator()
    {
        var tsv = "Status\tCount\nDone\t12\nRemaining\t3";

        var ok = MarkdownTableClipboard.TryTabularTextToMarkdownTable(tsv, useFirstRowAsHeader: true, out var markdown);

        Assert.True(ok);
        Assert.Equal(
            "| Status | Count |\n| --- | --- |\n| Done | 12 |\n| Remaining | 3 |",
            markdown);
    }

    [Fact]
    public void TryTabularTextToMarkdownTable_WithoutHeaderRow_UsesBlankHeader()
    {
        var tsv = "Done\t12\nRemaining\t3";

        var ok = MarkdownTableClipboard.TryTabularTextToMarkdownTable(tsv, useFirstRowAsHeader: false, out var markdown);

        Assert.True(ok);
        Assert.StartsWith("|  |  |\n| --- | --- |\n", markdown);
    }

    [Fact]
    public void TryTabularTextToMarkdownTable_SingleColumn_Fails()
    {
        var ok = MarkdownTableClipboard.TryTabularTextToMarkdownTable("just one column\nanother line", true, out var markdown);

        Assert.False(ok);
        Assert.Equal("", markdown);
    }

    [Fact]
    public void TryTabularTextToMarkdownTable_EmptyText_Fails()
    {
        var ok = MarkdownTableClipboard.TryTabularTextToMarkdownTable("", true, out _);

        Assert.False(ok);
    }

    [Fact]
    public void RoundTrip_MarkdownTableToTabularAndBack_PreservesContent()
    {
        // Input may use bare LF (e.g. straight from ParseTabularText's callers);
        // TryMarkdownTableToTabularText always emits CRLF between rows, matching
        // Windows clipboard convention.
        var tsv = "Status\tCount\nDone\t12\nRemaining\t3";
        MarkdownTableClipboard.TryTabularTextToMarkdownTable(tsv, true, out var markdown);

        var ok = MarkdownTableClipboard.TryMarkdownTableToTabularText(markdown, out var roundTripped);

        Assert.True(ok);
        Assert.Equal("Status\tCount\r\nDone\t12\r\nRemaining\t3", roundTripped);
    }

    [Fact]
    public void TryMarkdownTableToTabularText_MissingSeparatorRow_Fails()
    {
        var text = "| a | b |\n| c | d |"; // second row isn't a --- separator

        var ok = MarkdownTableClipboard.TryMarkdownTableToTabularText(text, out _);

        Assert.False(ok);
    }

    [Fact]
    public void EscapeMarkdownTableCell_EscapesPipesBackslashesAndNewlines()
    {
        var escaped = MarkdownTableClipboard.EscapeMarkdownTableCell("a|b\\c\nd");

        Assert.Equal("a\\|b\\\\c<br>d", escaped);
    }

    [Fact]
    public void UnescapeMarkdownTableCell_ReversesEscaping()
    {
        var unescaped = MarkdownTableClipboard.UnescapeMarkdownTableCell("a\\|b\\\\c<br>d");

        Assert.Equal("a|b\\c\nd", unescaped);
    }

    [Fact]
    public void TryCopyableTableTextToTabularText_PlainMultiColumnText_ConvertsDirectly()
    {
        var ok = MarkdownTableClipboard.TryCopyableTableTextToTabularText("a\tb\nc\td", out var tabular);

        Assert.True(ok);
        Assert.Equal("a\tb\r\nc\td", tabular);
    }

    [Fact]
    public void TryCopyableTableTextToTabularText_SingleColumnText_Fails()
    {
        var ok = MarkdownTableClipboard.TryCopyableTableTextToTabularText("just one column", out var tabular);

        Assert.False(ok);
        Assert.Equal("", tabular);
    }

    [Fact]
    public void SplitUnescapedPipes_RespectsBackslashEscapedPipe()
    {
        var cells = MarkdownTableClipboard.SplitUnescapedPipes(@"a\|b|c");

        Assert.Equal([@"a\|b", "c"], cells);
    }

    [Theory]
    [InlineData("---", true)]
    [InlineData(":---:", true)]
    [InlineData("--", false)]
    [InlineData("abc", false)]
    public void IsMarkdownTableSeparatorCell_RecognizesDashRuns(string cell, bool expected)
    {
        Assert.Equal(expected, MarkdownTableClipboard.IsMarkdownTableSeparatorCell(cell));
    }
}
