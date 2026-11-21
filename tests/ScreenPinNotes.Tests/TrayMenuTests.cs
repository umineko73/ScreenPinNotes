// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3 of the License.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Windows.Controls;
using System.Windows.Media;
using ScreenPinNotes.Services;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

/// <summary>
/// タスクトレイのメニュー。付箋の右クリックメニューと同じ WPF のメニューで、テーマに従い、
/// 主な項目には線のアイコンが付く。
/// </summary>
public class TrayMenuTests
{
    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrayMenu_UsesTheNoteMenuStyleAndTheme(bool dark)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var menu = TrayMenu.Build(app.BuildTrayMenuEntries(), dark);

        Assert.Equal(dark ? Color.FromRgb(31, 41, 55) : Colors.White, ((SolidColorBrush)menu.Background).Color);
        Assert.Same(menu.Resources["NoteMenuItemStyle"], menu.Resources[typeof(MenuItem)]);

        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Contains(items, item => Equals(item.Header, LocalizationService.T("TraySettings")));
        Assert.Contains(items, item => Equals(item.Header, LocalizationService.T("TrayExit")));
        // 子メニューの「非表示の付箋」以外は、どれも線のアイコンを持つ。
        Assert.All(items.Where(item => !Equals(item.Header, LocalizationService.T("TrayHiddenNotes"))),
            item => Assert.IsType<TextBlock>(item.Icon));
        Assert.Contains(menu.Items.OfType<Separator>(), _ => true);
    }

    [WpfFact]
    public void TrayMenuEntry_RunsItsActionWhenClicked()
    {
        WpfApplicationFixture.Ensure();
        var ran = false;
        var menu = TrayMenu.Build([new TrayMenuEntry("x", () => ran = true, UiIcons.Add)], dark: false);
        var item = Assert.IsType<MenuItem>(Assert.Single(menu.Items));
        item.RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(ran);
    }
}
