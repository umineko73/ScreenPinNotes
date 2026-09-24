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
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

public class LinkEditDialogTests
{
    [WpfFact]
    public void LongUrlWrapsAndExpandsWithDialog()
    {
        WpfApplicationFixture.Ensure();
        var owner = new Window();
        owner.Show();
        var target = "https://example.com/" + new string('a', 2000);
        var dialog = new LinkEditDialog(owner, "label", target);
        try
        {
            dialog.Show();
            dialog.UpdateLayout();
            var grid = Assert.IsType<Grid>(dialog.Content);
            var url = grid.Children.OfType<TextBox>().Single(box => box.Text == target);
            Assert.Equal(TextWrapping.Wrap, url.TextWrapping);
            Assert.Equal(ScrollBarVisibility.Disabled, url.HorizontalScrollBarVisibility);
            var width = url.ActualWidth;
            dialog.Width += 120;
            dialog.UpdateLayout();
            Assert.True(url.ActualWidth > width + 100);
            Assert.Equal(target, dialog.LinkTarget);
        }
        finally { dialog.Close(); owner.Close(); }
    }
}
