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
    private static Hyperlink CreateHyperlink(string label, string target)
        => new(new Run(label)) { NavigateUri = new Uri("about:" + target, UriKind.Absolute) };

    [Fact]
    public void Render_Heading_ProducesBoldParagraphWithHeadingText()
    {
        var blocks = MarkdownRenderer.Render("# Hello", 13, CreateHyperlink).ToList();

        var para = Assert.IsType<Paragraph>(Assert.Single(blocks));
        Assert.Equal(FontWeights.Bold, para.FontWeight);
        var run = Assert.IsType<Run>(Assert.Single(para.Inlines));
        Assert.Equal("Hello", run.Text);
    }

    [Fact]
    public void Render_BulletList_ProducesListWithExpectedItemCount()
    {
        var blocks = MarkdownRenderer.Render("- a\n- b\n- c", 13, CreateHyperlink).ToList();

        var list = Assert.IsType<System.Windows.Documents.List>(Assert.Single(blocks));
        Assert.Equal(3, list.ListItems.Count);
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

    [Fact]
    public void Render_EmptyText_ProducesSingleEmptyParagraph()
    {
        var blocks = MarkdownRenderer.Render("", 13, CreateHyperlink).ToList();

        var para = Assert.IsType<Paragraph>(Assert.Single(blocks));
        Assert.Empty(para.Inlines);
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
