// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using ScreenPinNotes.Models;
using ScreenPinNotes.ViewModels;
using System.Windows;

namespace ScreenPinNotes.Tests;

public class StickyNoteViewModelTests
{
    [Fact]
    public void TitleBarDisplayText_ExternalContent_AppendsUpdatedAtTimestamp()
    {
        var note = new StickyNote
        {
            Title = "app.log",
            ExternalContentPath = @"C:\logs\app.log",
            UpdatedAt = new DateTime(2024, 1, 2, 3, 4, 5),
        };
        var vm = new StickyNoteViewModel(note, new AppSettings());

        Assert.Equal("app.log (03:04:05)", vm.TitleBarDisplayText);
    }

    [Fact]
    public void TitleBarDisplayText_NonExternalContent_MatchesDisplayTitleWithoutTimestamp()
    {
        var note = new StickyNote { Title = "Regular note" };
        var vm = new StickyNoteViewModel(note, new AppSettings());

        Assert.Equal(vm.DisplayTitle, vm.TitleBarDisplayText);
        Assert.Equal("Regular note", vm.TitleBarDisplayText);
    }

    [Fact]
    public void TitleBarDisplayText_UpdatesWhenContentReloadsBumpsUpdatedAt()
    {
        var note = new StickyNote
        {
            Title = "app.log",
            ExternalContentPath = @"C:\logs\app.log",
            UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0),
        };
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Content = "new content";

        Assert.Contains(nameof(StickyNoteViewModel.TitleBarDisplayText), raised);
        Assert.Contains(note.UpdatedAt.ToString("HH:mm:ss"), vm.TitleBarDisplayText);
    }

    // 付箋ごとに地の色が違うので、つまみは決め打ちではなく本文の色を薄めて作る。
    [Theory]
    [InlineData("yellow")]
    [InlineData("dark-charcoal")]
    public void ScrollThumbBrushes_AreTheNoteTextColorMadeTranslucent(string colorKey)
    {
        var vm = new StickyNoteViewModel(new StickyNote { ColorKey = colorKey }, new AppSettings());

        var text = ((System.Windows.Media.SolidColorBrush)vm.TextForeground).Color;
        var thumb = ((System.Windows.Media.SolidColorBrush)vm.ScrollThumbBrush).Color;
        var hover = ((System.Windows.Media.SolidColorBrush)vm.ScrollThumbHoverBrush).Color;

        Assert.Equal((text.R, text.G, text.B), (thumb.R, thumb.G, thumb.B));
        Assert.Equal((text.R, text.G, text.B), (hover.R, hover.G, hover.B));
        Assert.InRange(thumb.A, 1, 254);            // 地が透けるていどに薄い
        Assert.True(hover.A > thumb.A);             // ホバーでははっきりさせる
    }

    [Fact]
    public void ScrollThumbBrush_FollowsTheNoteColorWhenItChanges()
    {
        var vm = new StickyNoteViewModel(new StickyNote { ColorKey = "yellow" }, new AppSettings());
        var before = ((System.Windows.Media.SolidColorBrush)vm.ScrollThumbBrush).Color;

        vm.ColorKey = "dark-charcoal";

        Assert.NotEqual(before, ((System.Windows.Media.SolidColorBrush)vm.ScrollThumbBrush).Color);
    }

    [Fact]
    public void TailModeVisibility_OnlyVisibleWhileExternalNoteIsTailing()
    {
        var note = new StickyNote { ExternalContentPath = @"C:\logs\app.log" };
        var vm = new StickyNoteViewModel(note, new AppSettings());

        Assert.Equal(Visibility.Collapsed, vm.TailModeVisibility);
        Assert.Null(vm.TailModeTooltip);

        vm.IsExternalTailMode = true;

        Assert.Equal(Visibility.Visible, vm.TailModeVisibility);
        Assert.Contains("tail", vm.TailModeTooltip);
        Assert.Contains("200", vm.TailModeTooltip);
    }

    [Fact]
    public void TailModeVisibility_StaysHiddenForNotesWithoutAnExternalFile()
    {
        var note = new StickyNote { ExternalTailMode = true };
        var vm = new StickyNoteViewModel(note, new AppSettings());

        Assert.Equal(Visibility.Collapsed, vm.TailModeVisibility);
    }

    [Fact]
    public void IsExternalTailMode_RaisesIndicatorPropertyChanged()
    {
        var note = new StickyNote { ExternalContentPath = @"C:\logs\app.log" };
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsExternalTailMode = true;

        Assert.Contains(nameof(StickyNoteViewModel.TailModeVisibility), raised);
        Assert.Contains(nameof(StickyNoteViewModel.TailModeTooltip), raised);
        Assert.True(note.ExternalTailMode);
    }

    [Fact]
    public void ClearExternalContentPath_HidesTailModeIndicator()
    {
        var note = new StickyNote { ExternalContentPath = @"C:\logs\app.log", ExternalTailMode = true };
        var vm = new StickyNoteViewModel(note, new AppSettings());
        Assert.Equal(Visibility.Visible, vm.TailModeVisibility);

        vm.ClearExternalContentPath();

        Assert.Equal(Visibility.Collapsed, vm.TailModeVisibility);
        Assert.False(vm.IsExternalTailMode);
    }

    [Fact]
    public void ClearExternalContentPath_DropsTimestampFromTitleBarDisplayText()
    {
        var note = new StickyNote
        {
            Title = "app.log",
            ExternalContentPath = @"C:\logs\app.log",
            UpdatedAt = new DateTime(2024, 1, 2, 3, 4, 5),
        };
        var vm = new StickyNoteViewModel(note, new AppSettings());

        vm.ClearExternalContentPath();

        Assert.Equal("app.log", vm.TitleBarDisplayText);
    }

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
    [InlineData(28, 42)]
    [InlineData(36, 54)]
    [InlineData(48, 54)]
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

    [Theory]
    [InlineData("ja", "外部ファイル (変更は自動で反映されます):\nD:\\logs\\app.log\n最終更新: 09:15:30")]
    [InlineData("en", "External file (changes appear automatically):\nD:\\logs\\app.log\nLast updated: 09:15:30")]
    public void TitleIconTooltip_SaysExternalNotesRefreshAutomatically(string language, string expected)
    {
        var note = new StickyNote
        {
            ExternalContentPath = @"D:\logs\app.log",
            UpdatedAt = new DateTime(2026, 9, 16, 9, 15, 30),
        };
        var vm = new StickyNoteViewModel(note, new AppSettings { Language = language });

        Assert.Equal(expected, vm.TitleIconTooltip);
    }

    [Theory]
    [InlineData("ja", "\n● 変更を確認中")]
    [InlineData("en", "\n● Checking for changes")]
    public void TitleIconTooltip_SaysWhenTheFileIsBeingChecked(string language, string line)
    {
        var vm = new StickyNoteViewModel(
            new StickyNote { ExternalContentPath = @"D:\logs\app.log" },
            new AppSettings { Language = language });
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.DoesNotContain(line, vm.TitleIconTooltip);
        vm.IsExternalPolling = true;
        Assert.EndsWith(line, vm.TitleIconTooltip);
        Assert.Contains(nameof(StickyNoteViewModel.TitleIconTooltip), changed);
        vm.IsExternalPolling = false;
        Assert.DoesNotContain(line, vm.TitleIconTooltip);
    }

    [Fact]
    public void ExternalPollingIndicator_ReservesSpaceOnlyOnExternalNotes()
    {
        var external = new StickyNoteViewModel(new StickyNote { ExternalContentPath = @"D:\logs\app.log" }, new AppSettings());
        var plain = new StickyNoteViewModel(new StickyNote(), new AppSettings());

        Assert.Equal(Visibility.Visible, external.ExternalPollingIndicatorVisibility);
        Assert.Equal(new Thickness(4, 0, 16, 0), external.TitleTextMargin);
        Assert.Equal(Visibility.Collapsed, plain.ExternalPollingIndicatorVisibility);
        Assert.Equal(new Thickness(4, 0, 4, 0), plain.TitleTextMargin);

        external.IsExternalPolling = true;
        external.ClearExternalContentPath();
        Assert.False(external.IsExternalPolling);
        Assert.Equal(Visibility.Collapsed, external.ExternalPollingIndicatorVisibility);
        Assert.Equal(new Thickness(4, 0, 4, 0), external.TitleTextMargin);
    }

    [Fact]
    public void TitleIconTooltip_IsRefreshedWhenTheContentIsReloaded()
    {
        var vm = new StickyNoteViewModel(
            new StickyNote { ExternalContentPath = @"D:\logs\app.log", UpdatedAt = new DateTime(2026, 9, 16, 9, 0, 0) },
            new AppSettings());
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Content = "reloaded";

        Assert.Contains(nameof(StickyNoteViewModel.TitleIconTooltip), changed);
        Assert.Contains(nameof(StickyNoteViewModel.TitleTooltip), changed);
        Assert.DoesNotContain("09:00:00", vm.TitleIconTooltip);
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
