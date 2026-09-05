using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class MarkdownLinkEditorTests
{
    [Theory]
    [InlineData("before [label](https://example.com/a_(b)) after", "label", "https://example.com/a_(b)")]
    [InlineData("[a\\[b\\]](<https://example.com/long?q=1&v=2>)", "a[b]", "https://example.com/long?q=1&v=2")]
    public void FindsLinkAtUrl(string text, string label, string target)
    {
        var link = MarkdownLinkEditor.FindAt(text, text.IndexOf("https") + 4);
        Assert.NotNull(link);
        Assert.Equal(label, link.Label);
        Assert.Equal(target, link.Target);
        Assert.EndsWith(")", text.Substring(link.Start, link.Length));
    }

    [Fact]
    public void SelectsOnlyTheSecondIdenticalLink()
    {
        const string text = "[x](https://example.com) and [x](https://example.com)";
        var link = MarkdownLinkEditor.FindAt(text, text.LastIndexOf("https"))!;
        var result = text.Remove(link.Start, link.Length).Insert(link.Start, MarkdownLinkFormatter.Build("new", "https://other.com"));
        Assert.Equal("[x](https://example.com) and [new](<https://other.com>)", result);
    }

    [Theory]
    [InlineData("![image](https://example.com/a.png)")]
    [InlineData("\\[escaped](https://example.com)")]
    public void DoesNotEditImagesOrEscapedLinks(string text)
        => Assert.Null(MarkdownLinkEditor.FindAt(text, text.IndexOf("https")));
}
