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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class ControlThemeTests
{
    [WpfFact]
    public void ControlsUseWindowPaletteAndKeepTheirInteractiveParts()
    {
        WpfApplicationFixture.Ensure();
        var window = new Window { Width = 400, Height = 300 };
        var vertical = new ScrollBar { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 100, Value = 30, ViewportSize = 20, Height = 120 };
        var horizontal = new ScrollBar { Orientation = Orientation.Horizontal, Minimum = 0, Maximum = 100, Value = 40, ViewportSize = 20, Width = 200 };
        var header = new GridViewColumnHeader { Content = "Title", Width = 200 };
        var editor = new TextBox { Text = "日本語", Height = 30 };
        var check = new CheckBox { Content = "Enabled", IsChecked = true };
        var panel = new StackPanel();
        foreach (var item in new Control[] { vertical, horizontal, header, editor, check }) panel.Children.Add(item);
        window.Content = panel;
        try
        {
            ControlTheme.Apply(window, true, dialog: true);
            window.Show(); window.UpdateLayout();
            foreach (var bar in new[] { vertical, horizontal })
            {
                var track = Assert.IsType<Track>(bar.Template.FindName("PART_Track", bar));
                Assert.Equal(bar.Orientation, track.Orientation);
                Assert.Equal(bar.Value, track.Value);
                Assert.Equal(bar.Maximum, track.Maximum);
                Assert.NotNull(track.Thumb);
                Assert.NotNull(track.DecreaseRepeatButton.Command);
                Assert.Same(window.Resources["SettingsSurface"], bar.Background);
            }
            Assert.IsType<Thumb>(header.Template.FindName("PART_HeaderGripper", header));
            Assert.IsType<ScrollViewer>(editor.Template.FindName("PART_ContentHost", editor));
            Assert.Equal(Visibility.Visible, ((FrameworkElement)check.Template.FindName("Check", check)).Visibility);
            var dark = Assert.IsType<SolidColorBrush>(header.Background).Color;
            ControlTheme.Apply(window, false, dialog: true);
            window.UpdateLayout();
            Assert.NotEqual(dark, Assert.IsType<SolidColorBrush>(header.Background).Color);
            Assert.Same(window.Resources["SettingsText"], editor.CaretBrush);
            Assert.Same(window.Resources["SettingsSurface"], horizontal.Background);
        }
        finally { window.Close(); }
    }
}
