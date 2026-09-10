using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using System.Windows.Media;

namespace ScreenPinNotes.Tests;

public class NoteAppearanceTests
{
    [Theory]
    [InlineData(50, 20, false, false, 128)]
    [InlineData(50, 20, true, false, 178)]
    [InlineData(90, 20, true, false, 255)]
    [InlineData(10, 20, false, true, 255)]
    [InlineData(-10, 0, false, false, 26)]
    public void OpacityAppliesToPanelsButKeepsTextReadable(
        int opacity, int boost, bool hovered, bool forceOpaque, byte expectedAlpha)
    {
        var appearance = new NoteAppearance(
            new StickyNote { OpacityPercent = opacity },
            new AppSettings { HoverOpacityBoostPercent = boost },
            forceOpaque, hovered);

        Assert.Equal(expectedAlpha, Assert.IsType<SolidColorBrush>(appearance.BackgroundBrush).Color.A);
        Assert.Equal(expectedAlpha, Assert.IsType<SolidColorBrush>(appearance.HeaderBrush).Color.A);
        Assert.Equal((byte)255, Assert.IsType<SolidColorBrush>(appearance.TextForeground).Color.A);
    }

    [Fact]
    public void UnknownPresetFallsBackToYellow()
    {
        var settings = new AppSettings();
        var unknown = new NoteAppearance(new StickyNote { ColorKey = "unknown" }, settings);
        var yellow = new NoteAppearance(new StickyNote { ColorKey = "yellow" }, settings);

        Assert.Equal(Assert.IsType<SolidColorBrush>(yellow.BackgroundBrush).Color,
            Assert.IsType<SolidColorBrush>(unknown.BackgroundBrush).Color);
    }
}
