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
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Views;

public sealed class ImportConflictDialog : Window
{
    public StorageService.ImportConflictAction Action { get; private set; } = StorageService.ImportConflictAction.Skip;

    public ImportConflictDialog(StickyNote note)
    {
        Title = LocalizationService.T("ImportConflictTitle");
        Width = 540;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        ControlTheme.Apply(this, App.Current.Settings.Theme == "Dark", dialog: true);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = string.Format(LocalizationService.T("ImportConflictMessage"), note.Title, note.Id),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });
        foreach (var (key, action) in new[]
        {
            ("ImportConflictOverwrite", StorageService.ImportConflictAction.Overwrite),
            ("ImportConflictRename", StorageService.ImportConflictAction.Rename),
            ("ImportConflictSkip", StorageService.ImportConflictAction.Skip),
        })
        {
            var button = new System.Windows.Controls.Button
            {
                Content = LocalizationService.T(key),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8),
                IsCancel = action == StorageService.ImportConflictAction.Skip,
                IsDefault = action == StorageService.ImportConflictAction.Skip,
            };
            button.Click += (_, _) => { Action = action; DialogResult = true; };
            panel.Children.Add(button);
        }
        Content = panel;
    }
}
