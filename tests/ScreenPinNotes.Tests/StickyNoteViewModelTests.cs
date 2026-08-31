using ScreenPinNotes.Models;
using ScreenPinNotes.ViewModels;
using System.Windows;

namespace ScreenPinNotes.Tests;

public class StickyNoteViewModelTests
{
    [Theory]
    [InlineData(8, 17)]
    [InlineData(12, 17)]
    [InlineData(20, 27)]
    [InlineData(28, 34)]
    [InlineData(48, 34)]
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
}
