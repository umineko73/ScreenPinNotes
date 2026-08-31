using ScreenStickyNotes.Models;
using ScreenStickyNotes.ViewModels;
using System.Windows;

namespace ScreenStickyNotes.Tests;

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
        var vm = new StickyNoteViewModel(
            new StickyNote { ExternalContentPath = @"D:\notes\todo.md" },
            new AppSettings());

        Assert.Contains(@"D:\notes\todo.md", vm.TitleIconTooltip);
        Assert.Contains(@"D:\notes\todo.md", vm.TitleTooltip);

        vm.ClearExternalContentPath();

        Assert.Null(vm.TitleIconTooltip);
        Assert.Null(vm.TitleTooltip);
    }
}
