using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class GlobalNoteHotkeyTests
{
    [Theory]
    [InlineData("ctrl+alt+n", true, "Ctrl+Alt+N")]
    [InlineData("Alt+Ctrl+Shift+F23", true, "Ctrl+Alt+Shift+F23")]
    [InlineData("Ctrl+5", true, "Ctrl+5")]
    [InlineData("", true, "")]
    [InlineData("N", false, "")]
    [InlineData("Shift+N", false, "")]
    [InlineData("Win+N", false, "")]
    [InlineData("Ctrl+F12", false, "")]
    [InlineData("Ctrl+Ctrl+N", false, "")]
    public void ValidatesAndNormalizesGesture(string text, bool valid, string expected)
    {
        Assert.Equal(valid, GlobalNoteHotkey.TryParse(text, out _, out _, out var normalized));
        if (valid) Assert.Equal(expected, normalized);
    }

    [WpfFact]
    public void ConflictKeepsOldRegistrationAndDisableReleasesIt()
    {
        using var first = new GlobalNoteHotkey(() => { });
        using var second = new GlobalNoteHotkey(() => { });
        Assert.True(first.TrySet("Ctrl+Alt+Shift+F23"));
        Assert.True(second.TrySet("Ctrl+Alt+Shift+F24"));
        Assert.False(first.TrySet("Ctrl+Alt+Shift+F24"));
        Assert.Equal("Ctrl+Alt+Shift+F23", first.Gesture);
        Assert.False(second.TrySet("Ctrl+Alt+Shift+F23"));
        Assert.True(first.TrySet(""));
        Assert.True(second.TrySet("Ctrl+Alt+Shift+F23"));
        Assert.True(first.TrySet("Ctrl+Alt+Shift+F24"));
    }
}
