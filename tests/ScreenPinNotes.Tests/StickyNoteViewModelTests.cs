using ScreenPinNotes.Models;
using ScreenPinNotes.ViewModels;
using System.Windows;

namespace ScreenPinNotes.Tests;

public class StickyNoteViewModelTests
{
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void DarkPresetsKeepDarkBackgroundAndReadableText(string theme)
    {
        foreach (var preset in StickyNoteViewModel.ColorPresets.Where(p => p.Key.StartsWith("dark-")))
        {
            var settings = new AppSettings { Theme = theme };
            var vm = new StickyNoteViewModel(new StickyNote { ColorKey = preset.Key, OpacityPercent = 100 }, settings);
            var bg = ((System.Windows.Media.SolidColorBrush)vm.BackgroundBrush).Color;
            var text = ((System.Windows.Media.SolidColorBrush)vm.TextForeground).Color;
            Assert.True(vm.UsesDarkNoteColors);
            Assert.Equal((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(preset.Value.Bg), bg);
            Assert.True(text.R > 220 && text.G > 220 && text.B > 220);
            settings.Theme = theme == "Dark" ? "Light" : "Dark";
            vm.RefreshSettings();
            Assert.Equal(bg, ((System.Windows.Media.SolidColorBrush)vm.BackgroundBrush).Color);
        }
    }

    [Theory]
    [InlineData(8, 20)]
    [InlineData(12, 20)]
    [InlineData(20, 30)]
    [InlineData(28, 38)]
    [InlineData(48, 38)]
    public void TitleIconSize_FollowsTitleFontSizeWithinBounds(double titleFontSize, double expectedIconSize)
    {
        var vm = new StickyNoteViewModel(
            new StickyNote { TitleFontSize = titleFontSize },
            new AppSettings());

        Assert.Equal(expectedIconSize, vm.TitleIconSize);
    }

    [Fact]
    public void SettingTitleFontSize_NotifiesTitleIconSize()
    {
        var vm = new StickyNoteViewModel(new StickyNote(), new AppSettings());
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.TitleFontSize = 20;

        Assert.Contains(nameof(StickyNoteViewModel.TitleIconSize), changed);
    }

    [Fact]
    public void EditLockVisibility_FollowsReadOnlyState()
    {
        var vm = new StickyNoteViewModel(new StickyNote(), new AppSettings());

        Assert.Equal(Visibility.Collapsed, vm.EditLockVisibility);

        vm.IsReadOnly = true;

        Assert.Equal(Visibility.Visible, vm.EditLockVisibility);
    }

    [Fact]
    public void PositionSeparatedVisibility_FollowsSeparatedState()
    {
        var vm = new StickyNoteViewModel(new StickyNote(), new AppSettings());

        Assert.Equal(Visibility.Collapsed, vm.PositionSeparatedVisibility);

        vm.IsPositionSeparated = true;

        Assert.Equal(Visibility.Visible, vm.PositionSeparatedVisibility);
    }

    [Fact]
    public void TitleIconTooltip_FollowsExternalContentPath()
    {
        var note = new StickyNote { ExternalContentPath = @"D:\notes\todo.md" };
        note.ExternalImageWidthOverrides["1:0:image.png"] = 320;
        var vm = new StickyNoteViewModel(
            note,
            new AppSettings());

        Assert.Contains(@"D:\notes\todo.md", vm.TitleIconTooltip);
        Assert.Contains(@"D:\notes\todo.md", vm.TitleTooltip);

        vm.ClearExternalContentPath();

        Assert.Null(vm.TitleIconTooltip);
        Assert.Null(vm.TitleTooltip);
        Assert.Empty(note.ExternalImageWidthOverrides);
    }

    [Fact]
    public void SetReminder_UpdatesReminderIndicator()
    {
        var vm = new StickyNoteViewModel(new StickyNote(), new AppSettings());
        var nextAt = new DateTime(2026, 8, 31, 14, 30, 0);

        Assert.Equal(Visibility.Collapsed, vm.ReminderVisibility);

        vm.SetReminder(nextAt);

        Assert.Equal(Visibility.Visible, vm.ReminderVisibility);
        Assert.Contains("2026/08/31 14:30", vm.ReminderTooltip);

        vm.SetReminder(null);

        Assert.Equal(Visibility.Collapsed, vm.ReminderVisibility);
        Assert.Null(vm.ReminderTooltip);
    }

    [Theory]
    [InlineData(AppSettings.NoteBorderGray, "#FF9A9A9A")]
    [InlineData(AppSettings.NoteBorderNone, "#00FFFFFF")]
    [InlineData("#FF3366", "#FFFF3366")]
    public void NoteBorderBrush_FollowsTheSetting(string setting, string expected)
    {
        var settings = new AppSettings { NoteBorderColor = setting };
        var vm = new StickyNoteViewModel(new StickyNote { ColorKey = "yellow" }, settings);

        var brush = Assert.IsType<System.Windows.Media.SolidColorBrush>(vm.NoteBorderBrush);
        Assert.Equal(expected, brush.Color.ToString());
    }

    // 「付箋の色」は色ごとに違う枠になる。不透明度の設定にも従う。
    [Fact]
    public void NoteBorderBrush_NoteColor_UsesTheHeaderColourOfEachNote()
    {
        var settings = new AppSettings { NoteBorderColor = AppSettings.NoteBorderNoteColor };
        var yellow = new StickyNoteViewModel(new StickyNote { ColorKey = "yellow" }, settings);
        var blue = new StickyNoteViewModel(new StickyNote { ColorKey = "blue" }, settings);

        Assert.Equal("#FFF9A825", ((System.Windows.Media.SolidColorBrush)yellow.NoteBorderBrush).Color.ToString());
        Assert.Equal("#FF1D4ED8", ((System.Windows.Media.SolidColorBrush)blue.NoteBorderBrush).Color.ToString());
    }

    // 明滅枠は外枠の内側をなぞるので、1つぶん小さい丸みで描く。
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(6, 6, 5)]
    [InlineData(16, 16, 15)]
    public void NoteCornerRadius_FollowsTheSetting(double setting, double expected, double flashExpected)
    {
        var settings = new AppSettings();
        settings.Layout.NoteCornerRadius = setting;
        var vm = new StickyNoteViewModel(new StickyNote(), settings);

        Assert.Equal(new CornerRadius(expected), vm.NoteCornerRadius);
        Assert.Equal(new CornerRadius(flashExpected), vm.NoteFlashCornerRadius);
    }
}
