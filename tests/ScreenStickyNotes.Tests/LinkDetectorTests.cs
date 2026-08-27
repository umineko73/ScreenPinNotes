using ScreenStickyNotes.Services;

namespace ScreenStickyNotes.Tests;

public class LinkDetectorTests
{
    [Fact]
    public void Parse_PlainTextOnly_ReturnsSingleNonLinkSegment()
    {
        var segments = LinkDetector.Parse("just some text");

        var segment = Assert.Single(segments);
        Assert.Equal("just some text", segment.Text);
        Assert.False(segment.IsLink);
    }

    [Fact]
    public void Parse_TextSurroundingUrl_SplitsIntoThreeSegments()
    {
        var segments = LinkDetector.Parse("see https://example.com for details");

        Assert.Equal(3, segments.Count);
        Assert.Equal("see ", segments[0].Text);
        Assert.False(segments[0].IsLink);
        Assert.Equal("https://example.com", segments[1].Text);
        Assert.True(segments[1].IsLink);
        Assert.Equal(" for details", segments[2].Text);
        Assert.False(segments[2].IsLink);
    }

    [Fact]
    public void Parse_WindowsPath_IsDetectedAsLink()
    {
        var segments = LinkDetector.Parse(@"open C:\Users\me\notes.txt now");

        Assert.Contains(segments, s => s.IsLink && s.Text == @"C:\Users\me\notes.txt");
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/path?query=1")]
    [InlineData(@"C:\Users\me")]
    [InlineData(@"\\server\share")]
    public void IsLink_RecognizedFormats_ReturnsTrue(string text)
    {
        Assert.True(LinkDetector.IsLink(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a link")]
    public void IsLink_UnrecognizedText_ReturnsFalse(string text)
    {
        Assert.False(LinkDetector.IsLink(text));
    }

    [Theory]
    [InlineData(@"C:\Users\me")]
    [InlineData(@"\\server\share\folder")]
    public void IsFolder_PathLikeText_ReturnsTrue(string text)
    {
        Assert.True(LinkDetector.IsFolder(text));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("just text")]
    public void IsFolder_NonPathText_ReturnsFalse(string text)
    {
        Assert.False(LinkDetector.IsFolder(text));
    }
}
