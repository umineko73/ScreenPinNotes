// ScreenStickyNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ScreenStickyNotes.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfListView = System.Windows.Controls.ListView;
using WpfPanel = System.Windows.Controls.Panel;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfBinding = System.Windows.Data.Binding;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace ScreenStickyNotes.Views;

public sealed class NoteManagerWindow : Window
{
    private readonly WpfTextBox _searchBox = new();
    private readonly WpfListView _listView = new();
    private readonly WpfButton _showHideButton = new();
    private readonly WpfButton _deleteButton = new();
    private readonly WpfButton _openExternalButton = new();
    private readonly WpfButton _openFolderButton = new();
    private readonly WpfButton _convertButton = new();
    private readonly WpfButton _reminderButton = new();
    private List<NoteRow> _allRows = [];

    public NoteManagerWindow()
    {
        Title = LocalizationService.T("NoteManagerTitle");
        Width = 920;
        Height = 520;
        MinWidth = 720;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = BuildContent();
        RefreshNotes();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(10) };

        var searchPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(searchPanel, Dock.Top);
        root.Children.Add(searchPanel);

        var searchLabel = new TextBlock
        {
            Text = LocalizationService.T("NoteManagerSearch"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        DockPanel.SetDock(searchLabel, Dock.Left);
        searchPanel.Children.Add(searchLabel);

        _searchBox.ToolTip = LocalizationService.T("NoteManagerSearchPlaceholder");
        _searchBox.TextChanged += (_, _) => ApplyFilter();
        searchPanel.Children.Add(_searchBox);

        var buttons = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        AddButton(buttons, _showHideButton, LocalizationService.T("NoteManagerShow"), (_, _) => ToggleSelectedVisibility());
        AddButton(buttons, _deleteButton, LocalizationService.T("NoteManagerDelete"), (_, _) => DeleteSelected());
        AddButton(buttons, _openExternalButton, LocalizationService.T("OpenExternalFile"), (_, _) => SelectedWindow()?.OpenExternalFile());
        AddButton(buttons, _openFolderButton, LocalizationService.T("OpenExternalFolder"), (_, _) => SelectedWindow()?.OpenExternalFolder());
        AddButton(buttons, _convertButton, LocalizationService.T("ConvertExternalToNormal"), (_, _) => ConvertSelectedExternalNote());
        AddButton(buttons, _reminderButton, LocalizationService.T("ReminderMenu"), (_, _) => SetSelectedReminder());
        AddButton(buttons, new WpfButton(), LocalizationService.T("TrayOpenExternalNote"), (_, _) => OpenExternalNoteFromDialog());
        AddButton(buttons, new WpfButton(), LocalizationService.T("NoteManagerRefresh"), (_, _) => RefreshNotes());

        _listView.View = BuildGridView();
        _listView.MouseDoubleClick += (_, _) => ShowSelected();
        _listView.SelectionChanged += (_, _) => UpdateButtons();
        root.Children.Add(_listView);

        return root;
    }

    private static void AddButton(WpfPanel panel, WpfButton button, string text, RoutedEventHandler click)
    {
        button.Content = text;
        button.MinWidth = 92;
        button.Margin = new Thickness(0, 0, 6, 0);
        button.Padding = new Thickness(8, 3, 8, 3);
        button.Click += click;
        panel.Children.Add(button);
    }

    private static GridView BuildGridView()
    {
        var grid = new GridView();
        AddColumn(grid, LocalizationService.T("NoteManagerStateColumn"), nameof(NoteRow.State), 70);
        AddColumn(grid, LocalizationService.T("NoteManagerTypeColumn"), nameof(NoteRow.Type), 70);
        AddColumn(grid, LocalizationService.T("NoteManagerTitleColumn"), nameof(NoteRow.Title), 180);
        AddColumn(grid, LocalizationService.T("NoteManagerSnippetColumn"), nameof(NoteRow.Snippet), 260);
        AddColumn(grid, LocalizationService.T("NoteManagerReminderColumn"), nameof(NoteRow.Reminder), 130);
        AddColumn(grid, LocalizationService.T("NoteManagerUpdatedColumn"), nameof(NoteRow.UpdatedAt), 130);
        AddColumn(grid, LocalizationService.T("NoteManagerPathColumn"), nameof(NoteRow.Path), 260);
        return grid;
    }

    private static void AddColumn(GridView grid, string header, string property, double width)
    {
        grid.Columns.Add(new GridViewColumn
        {
            Header = header,
            DisplayMemberBinding = new WpfBinding(property),
            Width = width,
        });
    }

    public void RefreshNotes()
    {
        _allRows = App.Current.NoteWindows
            .Select(win => NoteRow.FromWindow(win))
            .OrderBy(row => row.CreatedAtValue)
            .ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = _searchBox.Text.Trim();
        var rows = string.IsNullOrWhiteSpace(query)
            ? _allRows
            : _allRows.Where(row => row.Matches(query)).ToList();

        _listView.ItemsSource = rows;
        UpdateButtons();
    }

    private StickyNoteWindow? SelectedWindow()
    {
        if (_listView.SelectedItem is not NoteRow row)
            return null;

        return App.Current.NoteWindows.FirstOrDefault(w => w.ViewModel.Model.Id == row.Id);
    }

    private void ShowSelected()
    {
        if (_listView.SelectedItem is NoteRow row)
        {
            App.Current.ShowNote(row.Id);
            RefreshNotes();
        }
    }

    private void ToggleSelectedVisibility()
    {
        if (_listView.SelectedItem is not NoteRow row)
            return;

        if (row.IsHidden)
            App.Current.ShowNote(row.Id);
        else
            App.Current.HideNote(row.Id);
        RefreshNotes();
    }

    private void DeleteSelected()
    {
        if (_listView.SelectedItem is not NoteRow row)
            return;

        var result = WpfMessageBox.Show(
            LocalizationService.T(row.IsExternal ? "UnlinkExternalConfirmMessage" : "DeleteConfirmMessage"),
            LocalizationService.T("DeleteConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        App.Current.RemoveNoteFromManager(row.Id);
        RefreshNotes();
    }

    private void ConvertSelectedExternalNote()
    {
        SelectedWindow()?.ConvertExternalToNormalNote();
        App.Current.SaveAll();
        RefreshNotes();
    }

    private void SetSelectedReminder()
    {
        if (_listView.SelectedItem is not NoteRow row)
            return;

        var currentAt = SelectedWindow()?.ViewModel.Model.Reminder?.NextAt;
        var result = ReminderDialog.ShowFor(this, currentAt);
        if (!result.Accepted)
            return;

        App.Current.SetReminder(row.Id, result.ClearRequested ? null : result.NextAt);
        RefreshNotes();
    }

    private void OpenExternalNoteFromDialog()
    {
        using var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Title = LocalizationService.T("TrayOpenExternalNote"),
            Filter = LocalizationService.T("ExternalNoteFileFilter"),
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            App.Current.AddExternalFileNote(dialog.FileName);
        RefreshNotes();
    }

    private void UpdateButtons()
    {
        var selected = _listView.SelectedItem as NoteRow;
        var hasSelection = selected != null;
        var external = selected?.IsExternal == true;
        var canDelete = selected is { IsLocked: false } || external;

        _showHideButton.IsEnabled = hasSelection;
        _deleteButton.IsEnabled = hasSelection && canDelete;
        _deleteButton.Content = external
            ? LocalizationService.T("UnlinkExternalNote")
            : LocalizationService.T("NoteManagerDelete");
        _openExternalButton.IsEnabled = external;
        _openFolderButton.IsEnabled = external;
        _convertButton.IsEnabled = external;
        _reminderButton.IsEnabled = hasSelection;
        _showHideButton.Content = selected?.IsHidden == true
            ? LocalizationService.T("NoteManagerShow")
            : LocalizationService.T("NoteManagerHide");
    }

    private sealed record NoteRow(
        string Id,
        string State,
        string Type,
        string Title,
        string Snippet,
        string Reminder,
        string UpdatedAt,
        string Path,
        DateTime CreatedAtValue,
        bool IsHidden,
        bool IsExternal,
        bool IsLocked,
        string SearchText)
    {
        public static NoteRow FromWindow(StickyNoteWindow window)
        {
            var note = window.ViewModel.Model;
            var content = note.Content.Replace("\r\n", "\n").Replace("\r", "\n");
            var snippet = content.Split('\n')
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Length > 0) ?? "";
            if (snippet.Length > 120)
                snippet = snippet[..120] + "...";

            return new NoteRow(
                note.Id,
                note.IsHidden ? LocalizationService.T("NoteManagerHidden") : LocalizationService.T("NoteManagerVisible"),
                note.IsExternalContent ? LocalizationService.T("NoteManagerTypeExternal") : LocalizationService.T("NoteManagerTypeNormal"),
                window.ViewModel.DisplayTitle,
                snippet,
                note.Reminder?.NextAt?.ToString("yyyy/MM/dd HH:mm") ?? "",
                note.UpdatedAt.ToString("yyyy/MM/dd HH:mm"),
                note.ExternalContentPath ?? "",
                note.CreatedAt,
                note.IsHidden,
                note.IsExternalContent,
                note.IsReadOnly,
                content);
        }

        public bool Matches(string query)
        {
            return Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   Snippet.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   Reminder.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   SearchText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   Path.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
    }
}
