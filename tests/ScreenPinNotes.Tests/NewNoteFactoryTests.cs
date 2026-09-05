using ScreenPinNotes.Models;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class NewNoteFactoryTests
{
    private static readonly DateTime When = new(2026, 9, 6, 10, 30, 0);

    private static AppSettings SettingsWithDefaults()
    {
        var settings = new AppSettings();
        settings.NoteDefaults.ColorKey = "blue";
        settings.NoteDefaults.FontFamily = "Meiryo";
        settings.NoteDefaults.FontSize = 18;
        settings.NoteDefaults.Icon = "\U0001F98A";
        settings.NoteDefaults.TitleBarHidden = true;
        return settings;
    }

    // タスクトレイから作った付箋には、設定画面で決めた既定値が乗る。
    [Fact]
    public void WithoutATemplate_TakesTheConfiguredDefaults()
    {
        var note = NewNoteFactory.Create(SettingsWithDefaults(), template: null, 10, 20, When);

        Assert.Equal("blue", note.ColorKey);
        Assert.Equal("Meiryo", note.FontFamily);
        Assert.Equal(18, note.FontSize);
        Assert.Equal("\U0001F98A", note.Icon);
        Assert.True(note.IsTitleBarHidden);
        Assert.Equal(10, note.X);
        Assert.Equal(20, note.Y);
    }

    // 既存の付箋の「＋」から増やしたときは、そちらの見た目を引き継ぐ。
    // 既定値で上書きすると、揃えて並べた付箋の中に1枚だけ違う色が混ざる。
    [Fact]
    public void WithATemplate_TheTemplateWinsOverTheDefaults()
    {
        var template = new StickyNote
        {
            ColorKey = "green",
            FontFamily = "Yu Gothic UI",
            FontSize = 11,
            TitleFontSize = 15,
            Icon = "\U0001F431",
            OpacityPercent = 70,
            IsTitleBarHidden = false,
        };

        var note = NewNoteFactory.Create(SettingsWithDefaults(), template, 0, 0, When);

        Assert.Equal("green", note.ColorKey);
        Assert.Equal("Yu Gothic UI", note.FontFamily);
        Assert.Equal(11, note.FontSize);
        Assert.Equal(15, note.TitleFontSize);
        Assert.Equal("\U0001F431", note.Icon);
        Assert.Equal(70, note.OpacityPercent);
        Assert.False(note.IsTitleBarHidden);
    }

    // 既定値を触っていない環境では、これまでと同じ見た目のままにする。
    [Fact]
    public void UntouchedDefaults_MatchTheOriginalNoteAppearance()
    {
        var fresh = new StickyNote();

        var note = NewNoteFactory.Create(new AppSettings(), template: null, 0, 0, When);

        Assert.Equal(fresh.ColorKey, note.ColorKey);
        Assert.Equal(fresh.FontFamily, note.FontFamily);
        Assert.Equal(fresh.FontSize, note.FontSize);
        Assert.Equal(fresh.Icon, note.Icon);
        Assert.Equal(fresh.IsTitleBarHidden, note.IsTitleBarHidden);
    }

    [Fact]
    public void Normalize_KeepsTheDefaultsWithinTheRangeTheNoteToolbarAllows()
    {
        var settings = new AppSettings();
        settings.NoteDefaults.FontSize = 999;
        settings.NoteDefaults.ColorKey = "   ";
        settings.NoteDefaults.FontFamily = "";

        settings.Normalize();

        Assert.Equal(48, settings.NoteDefaults.FontSize);
        Assert.Equal("yellow", settings.NoteDefaults.ColorKey);
        Assert.Equal("Yu Gothic UI", settings.NoteDefaults.FontFamily);
    }
}
