using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class MarkdownSyntaxTests
{
    [Fact]
    public void ImageRangeIncludesAttributesAndOriginalOffset()
    {
        var image = Assert.IsType<MarkdownImageSyntax>(MarkdownSyntax.ParseImage(
            "prefix ![diagram](<assets/a (1).png>){width=120}{height=80} suffix", 7, 4, 3));
        Assert.Equal("assets/a (1).png", image.Target);
        Assert.Equal(4, image.Source.Line);
        Assert.Equal(10, image.Source.Start);
        Assert.Equal(120, image.Width);
        Assert.Equal(80, image.Height);
        Assert.Equal("![diagram](<assets/a (1).png>){width=120}{height=80}".Length, image.Source.Length);
    }

    [Fact]
    public void LinkAndTaskExposeSourcePositionsWithoutWpf()
    {
        var link = Assert.IsType<MarkdownLinkSyntax>(MarkdownSyntax.ParseLink("[site](https://example.com)", 0, 8, 5));
        Assert.Equal("site", link.Label);
        Assert.Equal(new MarkdownSourceSpan(8, 5, 27), link.Source);
        var task = Assert.IsType<MarkdownTaskSyntax>(MarkdownSyntax.ParseTaskMarker("[X] done", 9, 2));
        Assert.True(task.IsChecked);
        Assert.Equal(new MarkdownSourceSpan(9, 2, 4), task.Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("![broken](")]
    [InlineData("plain text")]
    public void NonImagesDoNotProduceImageOnlyTargets(string text)
        => Assert.Null(MarkdownSyntax.GetImageOnlyTarget(text));
}
