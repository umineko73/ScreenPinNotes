using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class PathDisplayTests
{
    [Theory]
    [InlineData("assets/0月写真/image.png", 30, "assets/0月写真/image.png")]
    [InlineData("assets/0月写真/image.png", 16, "…/0月写真/image.png")]
    [InlineData("assets/0月写真/image.png", 11, "…/image.png")]
    [InlineData("assets/0月写真/image.png", 7, "…ge.png")]
    [InlineData("assets/0月写真/image.png", 0, "")]
    public void Fit_PreservesTrailingPath(string path, int width, string expected)
        => Assert.Equal(expected, PathDisplay.Fit(path, width, text => text.Length));

    [Theory]
    [InlineData("![photo](assets/image.png)", "assets/image.png")]
    [InlineData("![](assets/image.png){width=100}", "assets/image.png")]
    [InlineData("text\n![photo](assets/image.png)", null)]
    [InlineData("", null)]
    public void ImageOnly_ExcludesTextNotes(string source, string? target)
        => Assert.Equal(target, MarkdownRenderer.GetImageOnlyTarget(source));
}
