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

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

// MarkdownRenderer.Render は WPF の FlowDocument 要素 (Block/Inline) を直接生成する
// ハンドロールパーサー。ライブの Window/Dispatcher なしで構築できる部分のみ
// スモークテストする。Hyperlink/checkbox の生成はコールバック経由なので、
// テスト側から差し込んだラムダで呼び出し内容を検証する。
public class MarkdownRendererTests
{
    [Fact]
    public void LargeMalformedDocumentFallsBackWithoutLosingSource()
    {
        var text = string.Concat(Enumerable.Repeat("**[~~`(broken", 12000));
        var paragraph = Assert.IsType<Paragraph>(Assert.Single(MarkdownRenderer.Render(text, 13, CreateHyperlink)));
        Assert.Equal(text, Assert.IsType<Run>(Assert.Single(paragraph.Inlines)).Text);
    }

    [Fact]
    public void RandomMalformedMarkupDoesNotThrow()
    {
        var random = new Random(731);
        const string chars = "[]()*_~#>!\\`abc\n";
        for (var i = 0; i < 200; i++)
        {
            var text = new string(Enumerable.Range(0, 300).Select(_ => chars[random.Next(chars.Length)]).ToArray());
            Assert.NotEmpty(MarkdownRenderer.Render(text, 13, CreateHyperlink).ToArray());
        }
    }
    // 上限内に収まる入力でも、'[' が並ぶ行はリンク探索が毎回行末まで走って
    // 解析が O(n^2) になり、UI スレッドが数秒止まっていた。
    [Theory]
    [InlineData("")]              // 行内に "](" が無い
    [InlineData("](zzz)")]        // "](" はあるがリンクにならない
    [InlineData("]( )")]          // 画像にもリンクにもならない
    public void BracketHeavyDocumentStaysWithinTheRenderBudget(string tail)
    {
        var line = new string('[', 8190 - tail.Length) + tail;
        var text = string.Join("\n", Enumerable.Repeat(line, 16));
        Assert.True(text.Length <= 131072 && text.Count(ch => ch == '\n') <= 2000);
        var watch = Stopwatch.StartNew();
        MarkdownRenderer.Render(text, 13, CreateHyperlink).ToArray();
        Assert.True(watch.ElapsedMilliseconds < 1500, $"Render took {watch.ElapsedMilliseconds}ms");
    }

    // 走査を省く最適化が、後ろに続く本物のリンクまで飲み込まないこと。
    // 先頭の "[[[ ]" はリンクにならないので探索打ち切りの経路を通る。
    [Fact]
    public void LinksStillRenderAfterUnmatchedBrackets()
    {
        var labels = new List<string>();
        MarkdownRenderer.Render("[[[ ] [Example](https://example.com)", 13, (label, target) =>
        {
            labels.Add(label);
            return CreateHyperlink(label, target);
        }).ToArray();
        Assert.Equal("Example", Assert.Single(labels));
    }

    // 折りたたんで1行だけ残す高さは、この値から出す。本文サイズで測ると
    // 見出しの下が切れる。
    [Theory]
    [InlineData("# heading", 21.0)]
    [InlineData("## heading", 20.0)]
    [InlineData("###### heading", 16.0)]
    [InlineData("plain text", 13.0)]
    [InlineData("- bullet", 13.0)]
    [InlineData("", 13.0)]
    public void GetFirstLineFontSize_MatchesHowTheFirstLineIsDrawn(string text, double expected)
        => Assert.Equal(expected, MarkdownRenderer.GetFirstLineFontSize(text, 13));

    [Fact]
    public void GetFirstLineFontSize_LooksAtTheFirstLineOnly()
        => Assert.Equal(13, MarkdownRenderer.GetFirstLineFontSize("plain\n# heading", 13));

    // タイトルバーを隠した1行表示では、見出しの拡大よりタイトル文字サイズを優先する。
    [Theory]
    [InlineData("# heading")]
    [InlineData("plain text")]
    public void GetFirstLineFontSize_IgnoreHeadingSize_AlwaysReturnsBaseFontSize(string text)
        => Assert.Equal(13, MarkdownRenderer.GetFirstLineFontSize(text, 13, ignoreHeadingSize: true));

    private static Hyperlink CreateHyperlink(string label, string target)
        => new(new Run(label)) { NavigateUri = new Uri("about:" + target, UriKind.Absolute) };

    [Fact]
    public void Render_Heading_ProducesBoldParagraphWithHeadingText()
    {
        var blocks = MarkdownRenderer.Render("# Hello", 13, CreateHyperlink).ToList();

        var para = Assert.IsType<Paragraph>(Assert.Single(blocks));
        Assert.Equal(FontWeights.Bold, para.FontWeight);
        Assert.Equal(21.0, para.FontSize);
        var run = Assert.IsType<Run>(Assert.Single(para.Inlines));
        Assert.Equal("Hello", run.Text);
    }

    // タイトルバーを隠した1行表示のときは、先頭行が見出しでも太字のまま
    // タイトル文字サイズ（baseFontSize）で描く。2行目以降の見出しは通常通り拡大する。
    [Fact]
    public void Render_IgnoreFirstLineHeadingSize_KeepsFirstHeadingAtBaseFontSizeButNotLaterOnes()
    {
        var blocks = MarkdownRenderer.Render(
            "# Hello\n# World", 13, CreateHyperlink, ignoreFirstLineHeadingSize: true).ToList();

        var first = Assert.IsType<Paragraph>(blocks[0]);
        Assert.Equal(FontWeights.Bold, first.FontWeight);
        Assert.Equal(13, first.FontSize);

        var second = Assert.IsType<Paragraph>(blocks[1]);
        Assert.Equal(FontWeights.Bold, second.FontWeight);
        Assert.Equal(21.0, second.FontSize);
    }

    [Fact]
    public void Render_BulletList_ProducesListWithExpectedItemCount()
    {
        var blocks = MarkdownRenderer.Render("- a\n- b\n- c", 13, CreateHyperlink).ToList();

        var list = Assert.IsType<System.Windows.Documents.List>(Assert.Single(blocks));
        Assert.Equal(3, list.ListItems.Count);
    }

    [Fact]
    public void Render_IndentedLineAfterListItem_ContinuesThatItem()
    {
        var blocks = MarkdownRenderer.Render(
            "* **Entra**\n  MFA、条件付きアクセス\n* Purview\n\tDLP",
            13,
            CreateHyperlink).ToList();

        var list = Assert.IsType<System.Windows.Documents.List>(Assert.Single(blocks));
        Assert.Equal(2, list.ListItems.Count);
        var first = Assert.IsType<Paragraph>(Assert.Single(list.ListItems.FirstListItem.Blocks));
        Assert.Contains(first.Inlines, inline => inline is LineBreak);
        Assert.Equal("EntraMFA、条件付きアクセス", GetInlineText(first.Inlines));
        var second = Assert.IsType<Paragraph>(Assert.Single(list.ListItems.LastListItem.Blocks));
        Assert.Equal("PurviewDLP", GetInlineText(second.Inlines));
    }

    [Fact]
    public void Render_UnindentedLineAfterListItem_EndsTheList()
    {
        var blocks = MarkdownRenderer.Render("- a\nplain", 13, CreateHyperlink).ToList();

        Assert.Equal(2, blocks.Count);
        Assert.IsType<System.Windows.Documents.List>(blocks[0]);
        Assert.Equal("plain", GetInlineText(Assert.IsType<Paragraph>(blocks[1]).Inlines));
    }

    [Fact]
    public void Render_IndentedQuoteOrFenceAfterListItem_IsNotSwallowedIntoTheItem()
    {
        var blocks = MarkdownRenderer.Render("- a\n  > quote\n- b\n  ```\n  code\n  ```", 13, CreateHyperlink).ToList();

        Assert.Equal(4, blocks.Count);
        Assert.IsType<System.Windows.Documents.List>(blocks[0]);
        var quote = Assert.IsType<Section>(blocks[1]);
        Assert.Equal("quote", GetInlineText(Assert.IsType<Paragraph>(Assert.Single(quote.Blocks)).Inlines));
        Assert.IsType<System.Windows.Documents.List>(blocks[2]);
        Assert.Equal(new Thickness(6, 3, 6, 3), Assert.IsType<Paragraph>(blocks[3]).Padding);
    }

    [Fact]
    public void Render_ImageOnListContinuationLine_ReportsItsOwnLineAndOffset()
    {
        MarkdownRenderer.MarkdownImage? captured = null;

        MarkdownRenderer.Render(
            "- caption\n  ![shot](assets/a.png)",
            13,
            CreateHyperlink,
            createImage: image =>
            {
                captured = image;
                return new Run("");
            }).ToList();

        Assert.NotNull(captured);
        Assert.Equal(1, captured.LineIndex);
        Assert.Equal(2, captured.Start);
    }

    [Fact]
    public void Render_Table_ProducesTableWithHeaderAndDataRow()
    {
        var markdown = "| A | B |\n| --- | --- |\n| 1 | 2 |";

        var blocks = MarkdownRenderer.Render(markdown, 13, CreateHyperlink).ToList();

        var table = Assert.IsType<Table>(Assert.Single(blocks));
        var group = Assert.Single(table.RowGroups);
        Assert.Equal(2, group.Rows.Count); // header row + one data row
        Assert.Equal(2, group.Rows[0].Cells.Count);
        Assert.Equal(2, group.Rows[1].Cells.Count);
    }

    [Fact]
    public void Render_Table_AppliesColumnAlignment()
    {
        var markdown = "| L | C | R |\n| :--- | :---: | ---: |\n| 1 | 2 | 3 |";

        var blocks = MarkdownRenderer.Render(markdown, 13, CreateHyperlink).ToList();

        var table = Assert.IsType<Table>(Assert.Single(blocks));
        var row = Assert.Single(table.RowGroups).Rows[1];
        Assert.Equal(TextAlignment.Left, GetOnlyCellParagraph(row.Cells[0]).TextAlignment);
        Assert.Equal(TextAlignment.Center, GetOnlyCellParagraph(row.Cells[1]).TextAlignment);
        Assert.Equal(TextAlignment.Right, GetOnlyCellParagraph(row.Cells[2]).TextAlignment);
    }

    [Fact]
    public void Render_MarkdownLink_InvokesHyperlinkCallbackWithLabelAndTarget()
    {
        var calls = new List<(string Label, string Target)>();
        Hyperlink Factory(string label, string target)
        {
            calls.Add((label, target));
            return CreateHyperlink(label, target);
        }

        MarkdownRenderer.Render("[Example](https://example.com)", 13, Factory).ToList();

        Assert.Contains(("Example", "https://example.com"), calls);
    }

    [Fact]
    public void Render_MarkdownLink_AllowsParenthesesInTarget()
    {
        var calls = new List<(string Label, string Target)>();
        Hyperlink Factory(string label, string target)
        {
            calls.Add((label, target));
            return CreateHyperlink(label, target);
        }

        MarkdownRenderer.Render("[Example](https://example.com/files/report(1).html)", 13, Factory).ToList();

        Assert.Contains(("Example", "https://example.com/files/report(1).html"), calls);
    }

    [Fact]
    public void Render_MarkdownLink_IgnoresOptionalTitle()
    {
        var calls = new List<(string Label, string Target)>();
        Hyperlink Factory(string label, string target)
        {
            calls.Add((label, target));
            return CreateHyperlink(label, target);
        }

        MarkdownRenderer.Render("[Example](https://example.com \"title\")", 13, Factory).ToList();

        Assert.Contains(("Example", "https://example.com"), calls);
    }

    [Fact]
    public void Render_AngleAutolink_InvokesHyperlinkCallback()
    {
        var calls = new List<(string Label, string Target)>();
        Hyperlink Factory(string label, string target)
        {
            calls.Add((label, target));
            return CreateHyperlink(label, target);
        }

        MarkdownRenderer.Render("<https://example.com>", 13, Factory).ToList();

        Assert.Contains(("https://example.com", "https://example.com"), calls);
    }

    [Fact]
    public void Render_MarkdownImage_AllowsParenthesesInTarget()
    {
        MarkdownRenderer.MarkdownImage? captured = null;

        MarkdownRenderer.Render(
            "![image](assets/report(1).png)",
            13,
            CreateHyperlink,
            createImage: image =>
            {
                captured = image;
                return new Run("");
            }).ToList();

        Assert.NotNull(captured);
        Assert.Equal("assets/report(1).png", captured.Target);
    }

    [Fact]
    public void Render_MarkdownImage_PreservesZeroWidthAttribute()
    {
        MarkdownRenderer.MarkdownImage? captured = null;

        MarkdownRenderer.Render(
            "![image](assets/pasted.png){width=0}",
            13,
            CreateHyperlink,
            createImage: image =>
            {
                captured = image;
                return new Run("");
            }).ToList();

        Assert.NotNull(captured);
        Assert.Equal(0, captured.Width);
    }

    [Fact]
    public void Render_MarkdownImage_ConsumesDuplicateWidthAttributesAndUsesLastValue()
    {
        MarkdownRenderer.MarkdownImage? captured = null;

        var blocks = MarkdownRenderer.Render(
            "![image](assets/pasted.png){width=238}{width=1190}",
            13,
            CreateHyperlink,
            createImage: image =>
            {
                captured = image;
                return new Run("");
            }).ToList();

        var paragraph = Assert.IsType<Paragraph>(Assert.Single(blocks));
        Assert.NotNull(captured);
        Assert.Equal(1190, captured.Width);
        Assert.Equal("", GetInlineText(paragraph.Inlines));
    }

    [Fact]
    public void Render_UnderscoreEmphasis_ProducesBoldAndItalicSpans()
    {
        var blocks = MarkdownRenderer.Render("__bold__ and _italic_", 13, CreateHyperlink).ToList();

        var para = Assert.IsType<Paragraph>(Assert.Single(blocks));
        var spans = para.Inlines.OfType<Span>().ToList();
        Assert.Equal(2, spans.Count);
        Assert.Equal(FontWeights.Bold, spans[0].FontWeight);
        Assert.Equal(FontStyles.Italic, spans[1].FontStyle);
        Assert.Equal("bold and italic", GetInlineText(para.Inlines));
    }

    [Fact]
    public void Render_Strikethrough_ProducesStrikethroughSpan()
    {
        var blocks = MarkdownRenderer.Render("~~deleted~~", 13, CreateHyperlink).ToList();

        var para = Assert.IsType<Paragraph>(Assert.Single(blocks));
        var span = Assert.IsType<Span>(Assert.Single(para.Inlines));
        Assert.Same(TextDecorations.Strikethrough, span.TextDecorations);
        Assert.Equal("deleted", GetInlineText(span.Inlines));
    }

    [Fact]
    public void Render_EscapedMarkdownMarkers_RemainPlainText()
    {
        var blocks = MarkdownRenderer.Render(@"not \*italic\* and \[link\]", 13, CreateHyperlink).ToList();

        var para = Assert.IsType<Paragraph>(Assert.Single(blocks));
        Assert.Empty(para.Inlines.OfType<Span>());
        Assert.Empty(para.Inlines.OfType<Hyperlink>());
        Assert.Equal("not *italic* and [link]", GetInlineText(para.Inlines));
    }

    // Constructing a System.Windows.Controls.CheckBox (a Control, unlike the plain
    // TextElement types the other tests use) requires an STA thread with a WPF
    // Dispatcher, which plain [Fact] doesn't provide.
    [WpfFact]
    public void Render_TaskListItem_InvokesTaskCheckboxCallbackWithCheckedState()
    {
        var calls = new List<(int LineIndex, bool IsChecked)>();
        CheckBox Factory(int lineIndex, bool isChecked)
        {
            calls.Add((lineIndex, isChecked));
            return new CheckBox { IsChecked = isChecked };
        }

        MarkdownRenderer.Render("- [x] done\n- [ ] todo", 13, CreateHyperlink, createTaskCheckbox: Factory).ToList();

        Assert.Contains((0, true), calls);
        Assert.Contains((1, false), calls);
    }

    // チェックボックスは文書に入って初めてテンプレートが当たり幅が決まるので、
    // 実際にウィンドウへ配置してから文字位置を比べる。
    [WpfFact]
    public void Render_TaskListItem_HangsContinuationLinesUnderTheItemText()
    {
        WpfApplicationFixture.Ensure();
        var document = new FlowDocument();
        foreach (var block in MarkdownRenderer.Render(
                     "- [ ] task\n  more",
                     13,
                     CreateHyperlink,
                     createTaskCheckbox: (_, isChecked) => new CheckBox { IsChecked = isChecked }))
        {
            document.Blocks.Add(block);
        }

        var window = new Window
        {
            Width = 400,
            Height = 200,
            Left = -10000,
            Top = -10000,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Content = new RichTextBox { Document = document, IsReadOnly = true },
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            window.UpdateLayout();

            var list = Assert.IsType<System.Windows.Documents.List>(Assert.Single(document.Blocks));
            var para = Assert.IsType<Paragraph>(Assert.Single(list.ListItems.FirstListItem.Blocks));
            var marker = Assert.IsType<InlineUIContainer>(para.Inlines.FirstInline);
            var box = Assert.IsType<Border>(marker.Child);
            var checkbox = Assert.IsType<CheckBox>(box.Child);
            Assert.True(checkbox.ActualWidth > 0);
            Assert.True(box.ActualWidth > checkbox.ActualWidth);
            Assert.Equal(box.ActualWidth, para.Margin.Left);
            Assert.Equal(-box.ActualWidth, para.TextIndent);
            Assert.Equal("taskmore", GetInlineText(para.Inlines));

            var runs = para.Inlines.OfType<Run>().ToList();
            var taskLeft = runs.Single(run => run.Text == "task").ContentStart.GetCharacterRect(LogicalDirection.Forward).Left;
            var moreLeft = runs.Single(run => run.Text == "more").ContentStart.GetCharacterRect(LogicalDirection.Forward).Left;
            Assert.Equal(taskLeft, moreLeft, 1);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Render_ReferenceLink_ResolvesDefinitionAndHidesItsLines()
    {
        var calls = new List<(string Label, string Target)>();

        var blocks = MarkdownRenderer.Render(
            "監査など。([Microsoft Learn][1])\n\n[1]: https://learn.microsoft.com/ja-jp/credentials/ \"SC-900\"\n",
            13,
            (label, target) =>
            {
                calls.Add((label, target));
                return new Hyperlink(new Run(label));
            }).ToList();

        Assert.Equal(("Microsoft Learn", "https://learn.microsoft.com/ja-jp/credentials/"), Assert.Single(calls));
        var para = Assert.IsType<Paragraph>(Assert.Single(blocks));
        Assert.Equal("監査など。(Microsoft Learn)", GetInlineText(para.Inlines));
    }

    [Fact]
    public void Render_CollapsedAndShortcutReferenceLinks_MatchLabelsCaseInsensitively()
    {
        var targets = new List<string>();

        MarkdownRenderer.Render(
            "[Docs][] と [docs]\n[DOCS]: <https://example.com/docs>",
            13,
            (label, target) =>
            {
                targets.Add(target);
                return new Hyperlink(new Run(label));
            }).ToList();

        Assert.Equal(["https://example.com/docs", "https://example.com/docs"], targets);
    }

    [Fact]
    public void Render_UndefinedReferenceOrNonLinkDefinition_StaysAsText()
    {
        var calls = 0;

        var blocks = MarkdownRenderer.Render(
            "[重要]: 明日までに提出\n[x][missing]",
            13,
            (label, _) =>
            {
                calls++;
                return new Hyperlink(new Run(label));
            }).ToList();

        Assert.Equal(0, calls);
        Assert.Equal(2, blocks.Count);
        Assert.Equal("[重要]: 明日までに提出", GetInlineText(Assert.IsType<Paragraph>(blocks[0]).Inlines));
        Assert.Equal("[x][missing]", GetInlineText(Assert.IsType<Paragraph>(blocks[1]).Inlines));
    }

    [Fact]
    public void Render_DefinitionInsideCodeFence_IsNeitherUsedNorHidden()
    {
        var calls = 0;

        var blocks = MarkdownRenderer.Render(
            "```\n[1]: https://example.com\n```\n[a][1]",
            13,
            (label, _) =>
            {
                calls++;
                return new Hyperlink(new Run(label));
            }).ToList();

        Assert.Equal(0, calls);
        Assert.Equal(2, blocks.Count);
        Assert.Equal("[1]: https://example.com", GetInlineText(Assert.IsType<Paragraph>(blocks[0]).Inlines));
    }

    [Fact]
    public void Render_ReferenceLinkInTableCell_Resolves()
    {
        var targets = new List<string>();

        MarkdownRenderer.Render(
            "| a | b |\n| --- | --- |\n| [x][1] | y |\n\n[1]: https://example.com",
            13,
            (label, target) =>
            {
                targets.Add(target);
                return new Hyperlink(new Run(label));
            }).ToList();

        Assert.Equal(["https://example.com"], targets);
    }

    [Fact]
    public void Render_ReferenceSource_SuppliesDefinitionsForAPartialText()
    {
        var targets = new List<string>();

        MarkdownRenderer.Render(
            "[x][1]",
            13,
            (label, target) =>
            {
                targets.Add(target);
                return new Hyperlink(new Run(label));
            },
            referenceSource: "[x][1]\n\n[1]: https://example.com").ToList();

        Assert.Equal(["https://example.com"], targets);
    }

    [Fact]
    public void Render_EmptyText_ProducesSingleEmptyParagraph()
    {
        var blocks = MarkdownRenderer.Render("", 13, CreateHyperlink).ToList();

        var para = Assert.IsType<Paragraph>(Assert.Single(blocks));
        Assert.Empty(para.Inlines);
    }

    [Theory]
    [InlineData("- a\n  - b\n    - c\n- d")]      // スペースで字下げ
    [InlineData("- a\n\t- b\n\t\t- c\n- d")]      // タブで字下げ
    [InlineData("- a\n　- b\n　　- c\n- d")]      // 全角スペースで字下げ
    public void Render_IndentedListItems_NestInsideTheItemAbove(string text)
    {
        var list = Assert.IsType<System.Windows.Documents.List>(
            Assert.Single(MarkdownRenderer.Render(text, 13, CreateHyperlink)));

        Assert.Equal(2, list.ListItems.Count);
        Assert.Equal(TextMarkerStyle.Disc, list.MarkerStyle);
        var a = list.ListItems.FirstListItem;
        Assert.Equal("a", GetInlineText(Assert.IsType<Paragraph>(a.Blocks.FirstBlock).Inlines));
        var level2 = Assert.IsType<System.Windows.Documents.List>(a.Blocks.LastBlock);
        Assert.Equal(TextMarkerStyle.Circle, level2.MarkerStyle);
        var b = Assert.Single(level2.ListItems);
        Assert.Equal("b", GetInlineText(Assert.IsType<Paragraph>(b.Blocks.FirstBlock).Inlines));
        var level3 = Assert.IsType<System.Windows.Documents.List>(b.Blocks.LastBlock);
        Assert.Equal(TextMarkerStyle.Square, level3.MarkerStyle);
        Assert.Equal("c", GetInlineText(Assert.IsType<Paragraph>(Assert.Single(level3.ListItems).Blocks.FirstBlock).Inlines));
        Assert.Equal("d", GetInlineText(Assert.IsType<Paragraph>(list.ListItems.LastListItem.Blocks.FirstBlock).Inlines));
    }

    [Fact]
    public void Render_OrderedList_StartsAtTheWrittenNumberAndAcceptsParenthesis()
    {
        var blocks = MarkdownRenderer.Render("3. c\n4. d\n\n1) one\n2) two", 13, CreateHyperlink).ToList();

        var first = Assert.IsType<System.Windows.Documents.List>(blocks[0]);
        Assert.Equal(TextMarkerStyle.Decimal, first.MarkerStyle);
        Assert.Equal(3, first.StartIndex);
        Assert.Equal(2, first.ListItems.Count);
        var second = Assert.IsType<System.Windows.Documents.List>(blocks[2]);
        Assert.Equal(1, second.StartIndex);
        Assert.Equal("two", GetInlineText(Assert.IsType<Paragraph>(second.ListItems.LastListItem.Blocks.FirstBlock).Inlines));
    }

    [Fact]
    public void Render_OrderedListWithAnotherDelimiter_StartsANewList()
    {
        var blocks = MarkdownRenderer.Render("3. c\n4. d\n1) one", 13, CreateHyperlink).ToList();

        Assert.Equal(2, blocks.Count);
        Assert.Equal(3, Assert.IsType<System.Windows.Documents.List>(blocks[0]).StartIndex);
        Assert.Equal(1, Assert.IsType<System.Windows.Documents.List>(blocks[1]).StartIndex);
    }

    [Fact]
    public void Render_OrderedListInsideBulletList_KeepsItsOwnNumbering()
    {
        var list = Assert.IsType<System.Windows.Documents.List>(
            Assert.Single(MarkdownRenderer.Render("- steps\n    1. open\n    2. save\n- done", 13, CreateHyperlink)));

        var nested = Assert.IsType<System.Windows.Documents.List>(list.ListItems.FirstListItem.Blocks.LastBlock);
        Assert.Equal(TextMarkerStyle.Decimal, nested.MarkerStyle);
        Assert.Equal(2, nested.ListItems.Count);
        Assert.Equal(2, list.ListItems.Count);
    }

    [Fact]
    public void Render_ContinuationAfterNestedList_StaysInTheOuterItem()
    {
        var list = Assert.IsType<System.Windows.Documents.List>(
            Assert.Single(MarkdownRenderer.Render("- a\n  - b\n  more a", 13, CreateHyperlink)));

        var a = Assert.Single(list.ListItems);
        Assert.Equal(3, a.Blocks.Count);
        Assert.Equal("more a", GetInlineText(Assert.IsType<Paragraph>(a.Blocks.LastBlock).Inlines));
    }

    [Fact]
    public void Render_ConsecutiveQuoteLines_FormOneQuote()
    {
        var quote = Assert.IsType<Section>(Assert.Single(
            MarkdownRenderer.Render("> first\n> second\n>\n> third", 13, CreateHyperlink)));

        Assert.Equal(new Thickness(3, 0, 0, 0), quote.BorderThickness);
        var paragraphs = quote.Blocks.Cast<Paragraph>().Select(p => GetInlineText(p.Inlines)).ToArray();
        Assert.Equal(["first", "second", "", "third"], paragraphs);
    }

    [Fact]
    public void Render_DoubleQuoteMarker_NestsAQuoteInsideTheQuote()
    {
        var blocks = MarkdownRenderer.Render("> outer\n>> inner\n> > inner too\n> back\nafter", 13, CreateHyperlink).ToList();

        Assert.Equal(2, blocks.Count);
        var outer = Assert.IsType<Section>(blocks[0]);
        var items = outer.Blocks.ToArray();
        Assert.Equal("outer", GetInlineText(Assert.IsType<Paragraph>(items[0]).Inlines));
        var inner = Assert.IsType<Section>(items[1]);
        Assert.Equal(new Thickness(3, 0, 0, 0), inner.BorderThickness);
        Assert.Equal(["inner", "inner too"], inner.Blocks.Cast<Paragraph>().Select(p => GetInlineText(p.Inlines)));
        Assert.Equal("back", GetInlineText(Assert.IsType<Paragraph>(items[2]).Inlines));
        Assert.Equal("after", GetInlineText(Assert.IsType<Paragraph>(blocks[1]).Inlines));
    }

    [Fact]
    public void Render_QuoteContent_IsRenderedAsMarkdown()
    {
        var quote = Assert.IsType<Section>(Assert.Single(
            MarkdownRenderer.Render("> # Title\n> - item\n> 1. first", 13, CreateHyperlink)));

        var blocks = quote.Blocks.ToArray();
        Assert.Equal(FontWeights.Bold, Assert.IsType<Paragraph>(blocks[0]).FontWeight);
        Assert.Equal(TextMarkerStyle.Disc, Assert.IsType<System.Windows.Documents.List>(blocks[1]).MarkerStyle);
        Assert.Equal(TextMarkerStyle.Decimal, Assert.IsType<System.Windows.Documents.List>(blocks[2]).MarkerStyle);
    }

    // 画像のサイズ変更やチェックの切り替えは、元の本文の行と文字位置で書き換える。
    // 引用やリストの入れ子の中でも、その位置が元の本文を指していること。
    [WpfFact]
    public void Render_NestedContent_ReportsPositionsInTheOriginalText()
    {
        var images = new List<MarkdownRenderer.MarkdownImage>();
        var tasks = new List<(int Line, bool Checked)>();

        MarkdownRenderer.Render(
            "> ![a](assets/a.png)\n>> - [x] ![b](assets/b.png)\n- top\n\t- [ ] ![c](assets/c.png)",
            13,
            CreateHyperlink,
            createImage: image =>
            {
                images.Add(image);
                return new Run("");
            },
            createTaskCheckbox: (line, isChecked) =>
            {
                tasks.Add((line, isChecked));
                return new CheckBox { IsChecked = isChecked };
            }).ToList();

        Assert.Equal([(0, 2), (1, 9), (3, 7)], images.Select(i => (i.LineIndex, i.Start)));
        Assert.Equal([(1, true), (3, false)], tasks);
    }

    [Theory]
    [InlineData("  indented", 2)]
    [InlineData("\tindented", 4)]
    [InlineData("　indented", 2)]
    public void Render_IndentedParagraph_TurnsTheIndentIntoAMargin(string text, int columns)
    {
        var paragraph = Assert.IsType<Paragraph>(Assert.Single(MarkdownRenderer.Render(text, 14, CreateHyperlink)));

        Assert.Equal(columns * 7, paragraph.Margin.Left);
        Assert.Equal("indented", GetInlineText(paragraph.Inlines));
    }

    [Fact]
    public void Render_DeepNesting_StaysWithinTheLimit()
    {
        var deepQuote = new string('>', 8000) + " text";
        var deepList = string.Join("\n", Enumerable.Range(0, 300).Select(n => new string(' ', n * 2) + "- item"));

        Assert.NotEmpty(MarkdownRenderer.Render(deepQuote, 13, CreateHyperlink).ToList());
        var list = Assert.IsType<System.Windows.Documents.List>(
            Assert.Single(MarkdownRenderer.Render(deepList, 13, CreateHyperlink)));

        var depth = 0;
        for (var current = list; current != null; depth++)
            current = current.ListItems.FirstListItem.Blocks.LastBlock as System.Windows.Documents.List;
        Assert.Equal(9, depth);
    }

    private static Paragraph GetOnlyCellParagraph(TableCell cell)
        => Assert.IsType<Paragraph>(Assert.Single(cell.Blocks));

    private static string GetInlineText(InlineCollection inlines)
    {
        var parts = new List<string>();
        foreach (var inline in inlines)
        {
            parts.Add(inline switch
            {
                Run run => run.Text,
                Hyperlink hyperlink => GetInlineText(hyperlink.Inlines),
                Span span => GetInlineText(span.Inlines),
                _ => "",
            });
        }

        return string.Concat(parts);
    }
}
