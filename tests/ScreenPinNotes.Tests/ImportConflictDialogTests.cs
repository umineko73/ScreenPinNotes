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
using ScreenPinNotes.Services;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

public class ImportConflictDialogTests
{
    [WpfFact]
    public void ClosingWithoutChoosingDefaultsToSkip()
    {
        WpfApplicationFixture.Ensure();
        var dialog = new ImportConflictDialog(new StickyNote { Title = "Incoming" });
        Assert.Equal(StorageService.ImportConflictAction.Skip, dialog.Action);
        var panel = Assert.IsType<System.Windows.Controls.StackPanel>(dialog.Content);
        Assert.Equal(3, panel.Children.OfType<System.Windows.Controls.Button>().Count());
        dialog.Close();
    }

    [Theory]
    [InlineData("ja", "ImportConflictOverwrite", "上書きする")]
    [InlineData("ja", "ImportConflictRename", "名前を変える（別名で追加）")]
    [InlineData("ja", "ImportConflictSkip", "インポートしない")]
    [InlineData("en", "ImportConflictOverwrite", "Overwrite")]
    [InlineData("en", "ImportConflictRename", "Rename (keep both)")]
    [InlineData("en", "ImportConflictSkip", "Do not import")]
    public void ChoicesAreLocalized(string culture, string key, string expected)
        => Assert.Equal(expected, LocalizationService.T(key, culture));
}
