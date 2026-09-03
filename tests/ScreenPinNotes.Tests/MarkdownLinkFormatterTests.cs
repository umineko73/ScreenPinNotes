using System.Windows.Documents;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class MarkdownLinkFormatterTests
{
    [Theory]
    [InlineData("Docs] [v2", "https://example.com/reports/(draft)")]
    [InlineData("notes.txt", @"C:\\Users\\me\\My Notes\\notes.txt")]
    public void Build_RoundTripsLabelAndTargetThroughRenderer(string label, string target)
    {
        var markdown = MarkdownLinkFormatter.Build(label, target);
        string? renderedLabel = null;
        string? renderedTarget = null;

        Hyperlink Factory(string actualLabel, string actualTarget)
        {
            renderedLabel = actualLabel;
            renderedTarget = actualTarget;
            return new Hyperlink(new Run(actualLabel));
        }

        MarkdownRenderer.Render(markdown, 13, Factory).ToList();

        Assert.Equal(label, renderedLabel);
        Assert.Equal(target, renderedTarget);
    }

    [Fact]
    public void Build_EscapesMarkdownDelimiters()
    {
        var markdown = MarkdownLinkFormatter.Build("a[b]\\c", "https://example.com/a(b)\\c");

        Assert.Equal(@"[a\[b\]\\c](https://example.com/a\(b\)\\c)", markdown);
    }

    [Fact]
    public void Build_PastedOnNextLine_PreservesExistingMarkdownLink()
    {
        var first = MarkdownLinkFormatter.Build("First", "https://example.com/first");
        var second = MarkdownLinkFormatter.Build("Second", "https://example.com/second");
        var inserted = TextInsertion.InsertAtSelection(first + "\n", first.Length + 1, 0, second);
        var renderedLinks = new List<(string Label, string Target)>();

        Hyperlink Factory(string label, string target)
        {
            renderedLinks.Add((label, target));
            return new Hyperlink(new Run(label));
        }

        MarkdownRenderer.Render(inserted.Text, 13, Factory).ToList();

        Assert.Equal(first + "\n" + second, inserted.Text);
        Assert.Equal(
            [("First", "https://example.com/first"), ("Second", "https://example.com/second")],
            renderedLinks);
    }
}
