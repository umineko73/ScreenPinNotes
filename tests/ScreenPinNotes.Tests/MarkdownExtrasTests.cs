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

using System.Windows.Documents;
using System.Windows.Media;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

/// <summary>ハイライト・段落の連結・リストの自動継続・書式メニューの追加分。</summary>
public class MarkdownExtrasTests
{
    private static Hyperlink CreateHyperlink(string label, string target)
        => new(new Run(label)) { NavigateUri = new Uri("about:" + target, UriKind.Absolute) };

    private static string Text(InlineCollection inlines)
        => string.Concat(inlines.Select(inline => inline switch
        {
            Run run => run.Text,
            LineBreak => "\n",
            Span span => Text(span.Inlines),
            _ => "",
        }));

    private static Paragraph Only(string markdown, bool joinLines = false)
        => Assert.IsType<Paragraph>(Assert.Single(MarkdownRenderer.Render(markdown, 13, CreateHyperlink, joinLines: joinLines)));

    // ─── ハイライト ───

    [Fact]
    public void Highlight_PaintsTheMarkedText()
    {
        var paragraph = Only("before ==marked **bold**== after");

        var span = Assert.Single(paragraph.Inlines.OfType<Span>(), s => s.Background != null);
        Assert.Equal("marked bold", Text(span.Inlines));
        Assert.Equal("before marked bold after", Text(paragraph.Inlines));
        Assert.True(((SolidColorBrush)span.Background).Color.A > 0);
    }

    [Theory]
    [InlineData("if a == b == c")]
    [InlineData("==")]
    [InlineData("\\==not\\==")]
    public void Highlight_LeavesComparisonsAndEscapesAlone(string markdown)
    {
        var paragraph = Only(markdown);

        Assert.DoesNotContain(paragraph.Inlines.OfType<Span>(), s => s.Background != null);
    }

    [Fact]
    public void Highlight_CanBeToggledFromTheFormattingMenu()
    {
        var edit = MarkdownFormatting.Inline("a word", 2, 4, "==");
        var applied = "a word".Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        Assert.Equal("a ==word==", applied);

        var undo = MarkdownFormatting.Inline(applied, 4, 4, "==");
        Assert.Equal("a word", applied.Remove(undo.Start, undo.Length).Insert(undo.Start, undo.Replacement));
    }

    // ─── 段落の連結 ───

    [Fact]
    public void JoinLines_Off_KeepsOneParagraphPerLine()
    {
        var blocks = MarkdownRenderer.Render("first\nsecond", 13, CreateHyperlink).ToList();

        Assert.Equal(2, blocks.Count);
    }

    [Theory]
    [InlineData("first line\nsecond line", "first line second line")]
    [InlineData("日本語の文が\n続きます", "日本語の文が続きます")]
    [InlineData("英語 then\n日本語", "英語 then日本語")]
    [InlineData("hard  \nbreak", "hard\nbreak")]
    [InlineData("back\\\nslash", "back\nslash")]
    public void JoinLines_On_WrapsConsecutiveLinesAsOneParagraph(string markdown, string expected)
        => Assert.Equal(expected, Text(Only(markdown, joinLines: true).Inlines));

    [Fact]
    public void JoinLines_On_StopsAtOtherBlocksBlankLinesAndIndentChanges()
    {
        var blocks = MarkdownRenderer.Render(
            "one\ntwo\n# head\nthree\n- item\nfour\n\tindented\n\tmore\n\nfive\n| a | b |\n| --- | --- |\n> quote",
            13, CreateHyperlink, joinLines: true).ToList();

        var kinds = blocks.Select(b => b switch
        {
            Paragraph p when p.FontWeight == System.Windows.FontWeights.Bold => "H:" + Text(p.Inlines),
            Paragraph p => "P:" + Text(p.Inlines),
            System.Windows.Documents.List => "L",
            Table => "T",
            Section => "Q",
            _ => "?",
        });
        Assert.Equal(["P:one two", "H:head", "P:three", "L", "P:four", "P:indented more", "P:", "P:five", "T", "Q"], kinds);
    }

    [Fact]
    public void JoinLines_On_KeepsImagePositionsOnTheirOwnLines()
    {
        var images = new List<MarkdownRenderer.MarkdownImage>();

        MarkdownRenderer.Render("text\n  ![a](assets/a.png)", 13, CreateHyperlink,
            createImage: image => { images.Add(image); return new Run(""); }, joinLines: true).ToList();

        Assert.Empty(images.Where(i => i.LineIndex == 0));
        Assert.Contains(images, i => i.LineIndex == 1 && i.Start == 2);
    }

    [Fact]
    public void JoinLines_IsOffByDefaultInSettings()
        => Assert.False(new AppSettings().JoinMarkdownLines);

    [Theory]
    [InlineData("ja", "Markdown", "続けて書いた行を1つの段落として折り返す")]
    [InlineData("en", "Markdown", "Join consecutive lines into one paragraph")]
    public void JoinLines_SettingIsLocalized(string language, string section, string label)
    {
        Assert.Equal(section, LocalizationService.T("SettingsMarkdown", language));
        Assert.Equal(label, LocalizationService.T("SettingsJoinMarkdownLines", language));
    }

    // ─── リストの自動継続 ───

    private static string Apply(string text, MarkdownIndentation.Edit edit)
        => text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);

    [Theory]
    [InlineData("- item", "- item\n- ")]
    [InlineData("* item", "* item\n* ")]
    [InlineData("\t- item", "\t- item\n\t- ")]
    [InlineData("3. item", "3. item\n4. ")]
    [InlineData("9) item", "9) item\n10) ")]
    [InlineData("- [x] done", "- [x] done\n- [ ] ")]
    [InlineData("1. [ ] todo", "1. [ ] todo\n2. [ ] ")]
    public void ContinueList_AtTheEndOfAnItem_StartsTheNextItem(string line, string expected)
    {
        var edit = MarkdownIndentation.ContinueList(line, line.Length, "\n")!.Value;

        Assert.Equal(expected, Apply(line, edit));
        Assert.Equal(expected.Length, edit.SelectionStart);
    }

    [Fact]
    public void ContinueList_InTheMiddleOfAnItem_MovesTheRestToTheNewItem()
    {
        var edit = MarkdownIndentation.ContinueList("- ab cd", 4, "\r\n")!.Value;

        Assert.Equal("- ab\r\n-  cd", Apply("- ab cd", edit));
        Assert.Equal(8, edit.SelectionStart);
    }

    [Theory]
    [InlineData("- ", "")]
    [InlineData("- [ ] ", "")]
    [InlineData("\t- ", "- ")]
    [InlineData("    2. ", "2. ")]
    public void ContinueList_OnAnEmptyItem_OutdentsOrEndsTheList(string line, string expected)
    {
        var text = "- first\n" + line;

        var edit = MarkdownIndentation.ContinueList(text, text.Length, "\n")!.Value;

        Assert.Equal("- first\n" + expected, Apply(text, edit));
        Assert.Equal(("- first\n" + expected).Length, edit.SelectionStart);
    }

    [Theory]
    [InlineData("plain text", 5)]
    [InlineData("- item", 1)]      // 印の途中
    [InlineData("-item", 5)]       // 印ではない
    public void ContinueList_OutsideAListItem_LeavesEnterToTheEditor(string text, int caret)
        => Assert.Null(MarkdownIndentation.ContinueList(text, caret, "\n"));

    // ─── 書式メニューの行書式 ───

    private static string ApplyLines(string text, string prefix)
    {
        var edit = MarkdownFormatting.Lines(text, 0, text.Length, prefix);
        return text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
    }

    [Fact]
    public void FormattingMenu_NumbersSelectedLinesAndTogglesBack()
    {
        var numbered = ApplyLines("a\n- b\n\nc", "1. ");
        Assert.Equal("1. a\n2. b\n\n3. c", numbered);
        Assert.Equal("a\nb\n\nc", ApplyLines(numbered, "1. "));
    }

    [Fact]
    public void FormattingMenu_QuotesSelectedLinesKeepingTheirMarkersAndTogglesBack()
    {
        var quoted = ApplyLines("text\n- item", "> ");
        Assert.Equal("> text\n> - item", quoted);
        Assert.Equal("text\n- item", ApplyLines(quoted, "> "));
    }

    [Theory]
    [InlineData("ja", "ハイライト", "番号付きリスト", "引用")]
    [InlineData("en", "Highlight", "Numbered list", "Quote")]
    public void FormattingMenu_NewItemsAreLocalized(string language, string highlight, string numbered, string quote)
    {
        Assert.Equal(highlight, LocalizationService.T("FormatHighlight", language));
        Assert.Equal(numbered, LocalizationService.T("FormatNumbered", language));
        Assert.Equal(quote, LocalizationService.T("FormatQuote", language));
    }
}
