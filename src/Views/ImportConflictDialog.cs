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
