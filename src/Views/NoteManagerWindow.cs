// ScreenPinNotes - a desktop sticky notes app for Windows 11
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
using ScreenPinNotes.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfListView = System.Windows.Controls.ListView;
using WpfPanel = System.Windows.Controls.Panel;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfBinding = System.Windows.Data.Binding;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace ScreenPinNotes.Views;

public sealed class NoteManagerWindow : Window
{
    private readonly System.Windows.Controls.ComboBox _searchBox = new() { IsEditable = true, IsTextSearchEnabled = false };
    private readonly WpfListView _listView = new();
    private readonly WpfButton _showHideButton = new();
    private readonly WpfButton _hideButton = new();
    private readonly WpfButton _frontButton = new();
    private readonly WpfButton _backButton = new();
    private readonly WpfButton _upButton = new();
    private readonly WpfButton _downButton = new();
    private string _sortProperty = nameof(NoteRow.Layer);
    private bool _sortDescending;
    private readonly List<(GridViewColumnHeader Header, string Label, string Property)> _sortHeaders = [];
    private readonly System.Windows.Controls.ComboBox _searchMode = new();
    private readonly TextBlock _searchError = new();
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

        FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI");
        FontSize = 13;
        var dark = App.Current.Settings.Theme == "Dark";
        System.Windows.Media.Brush Brush(string color) => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        Background = Brush(dark ? "#202020" : "#F5F7FA");
        Foreground = Brush(dark ? "#EEEEEE" : "#242424");
        Resources["SettingsText"] = Foreground;
        Resources["SettingsSurface"] = Brush(dark ? "#303030" : "#FFFFFF");
        Resources["SettingsBorder"] = Brush(dark ? "#555555" : "#D6DCE5");
        Resources["SettingsHover"] = Brush(dark ? "#444444" : "#EAF0F8");
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/ScreenPinNotes;component/Resources/SettingsStyles.xaml") });
        Content = BuildContent();
        RefreshNotes();
        Closing += (_, _) => RememberSearch();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(20) };

        var heading = new TextBlock { Text = Title, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
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

        _searchMode.ItemsSource = new[] { LocalizationService.T("SearchPlain"), LocalizationService.T("SearchWildcard"), LocalizationService.T("SearchRegex") };
        _searchMode.SelectedIndex = 0;
        _searchMode.Width = 150;
        _searchMode.Margin = new Thickness(8, 0, 0, 0);
        _searchMode.SelectionChanged += (_, _) => ApplyFilter();
        DockPanel.SetDock(_searchMode, Dock.Right);
        searchPanel.Children.Add(_searchMode);
        var clearSearch = new WpfButton
        {
            Content = LocalizationService.T("NoteManagerClearSearch"),
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 4, 12, 4),
        };
        clearSearch.Click += (_, _) =>
        {
            _searchBox.IsDropDownOpen = false;
            _searchBox.SelectedIndex = -1;
            _searchBox.Text = "";
            ApplyFilter();
            _searchBox.Focus();
        };
        DockPanel.SetDock(clearSearch, Dock.Right);
        searchPanel.Children.Add(clearSearch);
        _searchBox.MinHeight = 34;
        _searchBox.Padding = new Thickness(8, 4, 8, 4);
        _searchBox.ToolTip = LocalizationService.T("SearchHistoryHint");
        _searchBox.ItemsSource = App.Current.Settings.SearchHistory.ToArray();
        _searchBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => ApplyFilter()));
        _searchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { RememberSearch(); e.Handled = true; } };
        _searchBox.LostKeyboardFocus += (_, _) => { if (!_searchBox.IsKeyboardFocusWithin) RememberSearch(); };
        _searchBox.DropDownOpened += (_, _) =>
        {
            var query = _searchBox.Text;
            _searchBox.ItemsSource = App.Current.Settings.SearchHistory.ToArray();
            _searchBox.Text = query;
        };
        searchPanel.Children.Add(_searchBox);

        var sortHint = new TextBlock { Text = LocalizationService.T("LayerSortHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(sortHint, Dock.Top);
        root.Children.Add(sortHint);

        _searchError.Foreground = System.Windows.Media.Brushes.IndianRed;
        _searchError.TextWrapping = TextWrapping.Wrap;
        DockPanel.SetDock(_searchError, Dock.Top);
        root.Children.Add(_searchError);
        var hint = new TextBlock { Text = LocalizationService.T("NoteManagerSelectionHint"), Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(hint, Dock.Bottom);
        root.Children.Add(hint);
        var buttons = new WrapPanel
        {
            Orientation = WpfOrientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        AddButton(buttons, _showHideButton, LocalizationService.T("NoteManagerShow"), (_, _) => SetSelectedVisibility(true));
        AddButton(buttons, _hideButton, LocalizationService.T("NoteManagerHide"), (_, _) => SetSelectedVisibility(false));
        AddButton(buttons, _frontButton, LocalizationService.T("BringToFront"), (_, _) => ChangeSelectedZOrder(LayerMove.Top));
        AddButton(buttons, _upButton, LocalizationService.T("LayerUp"), (_, _) => ChangeSelectedZOrder(LayerMove.Up));
        AddButton(buttons, _downButton, LocalizationService.T("LayerDown"), (_, _) => ChangeSelectedZOrder(LayerMove.Down));
        AddButton(buttons, _backButton, LocalizationService.T("SendToBack"), (_, _) => ChangeSelectedZOrder(LayerMove.Bottom));
        AddButton(buttons, _deleteButton, LocalizationService.T("NoteManagerDelete"), (_, _) => DeleteSelected());
        AddButton(buttons, _openExternalButton, LocalizationService.T("OpenExternalFile"), (_, _) => SelectedWindow()?.OpenExternalFile());
        AddButton(buttons, _openFolderButton, LocalizationService.T("OpenExternalFolder"), (_, _) => SelectedWindow()?.OpenExternalFolder());
        AddButton(buttons, _convertButton, LocalizationService.T("ConvertExternalToNormal"), (_, _) => ConvertSelectedExternalNote());
        AddButton(buttons, _reminderButton, LocalizationService.T("ReminderMenu"), (_, _) => SetSelectedReminder());
        AddButton(buttons, new WpfButton(), LocalizationService.T("TrayOpenExternalNote"), (_, _) => OpenExternalNoteFromDialog());
        AddButton(buttons, new WpfButton(), LocalizationService.T("NoteManagerRefresh"), (_, _) => RefreshNotes());

        _listView.SelectionMode = System.Windows.Controls.SelectionMode.Extended;
        _listView.Background = (System.Windows.Media.Brush)Resources["SettingsSurface"];
        _listView.Foreground = Foreground;
        _listView.BorderBrush = (System.Windows.Media.Brush)Resources["SettingsBorder"];
        var rowStyle = new Style(typeof(System.Windows.Controls.ListViewItem));
        rowStyle.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(8, 7, 8, 7)));
        _listView.ItemContainerStyle = rowStyle;
        _listView.View = BuildGridView();
        _listView.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source &&
                ItemsControl.ContainerFromElement(_listView, source) is System.Windows.Controls.ListViewItem)
                ShowSelected();
        };
        _listView.SelectionChanged += (_, _) => UpdateButtons();
        root.Children.Add(_listView);

        return root;
    }

    private static void AddButton(WpfPanel panel, WpfButton button, string text, RoutedEventHandler click)
    {
        button.Content = text;
        button.MinWidth = 92;
        button.Margin = new Thickness(0, 0, 8, 8);
        button.Padding = new Thickness(12, 7, 12, 7);
        button.Click += click;
        panel.Children.Add(button);
    }

    private GridView BuildGridView()
    {
        var grid = new GridView();
        AddColumn(grid, LocalizationService.T("ZOrder"), nameof(NoteRow.Layer), 70);
        AddColumn(grid, LocalizationService.T("TopmostTooltip"), nameof(NoteRow.Pinned), 55);
        AddColumn(grid, LocalizationService.T("NoteManagerStateColumn"), nameof(NoteRow.State), 70);
        AddColumn(grid, LocalizationService.T("NoteManagerLockColumn"), nameof(NoteRow.LockState), 90);
        AddColumn(grid, LocalizationService.T("NoteManagerTypeColumn"), nameof(NoteRow.Type), 70);
        AddColumn(grid, LocalizationService.T("NoteManagerTitleColumn"), nameof(NoteRow.Title), 180);
        AddColumn(grid, LocalizationService.T("NoteManagerSnippetColumn"), nameof(NoteRow.Snippet), 260);
        AddColumn(grid, LocalizationService.T("NoteManagerReminderColumn"), nameof(NoteRow.Reminder), 130);
        AddColumn(grid, LocalizationService.T("NoteManagerUpdatedColumn"), nameof(NoteRow.UpdatedAt), 130);
        AddColumn(grid, LocalizationService.T("NoteManagerPathColumn"), nameof(NoteRow.Path), 260);
        UpdateSortHeaders();
        return grid;
    }

    private void AddColumn(GridView grid, string header, string property, double width)
    {
        var columnHeader = new GridViewColumnHeader { Content = header, Tag = property, ToolTip = header, Padding = new Thickness(4, 5, 4, 5) };
        columnHeader.Click += (_, _) =>
        {
            _sortDescending = property != nameof(NoteRow.Layer) && (_sortProperty == property ? !_sortDescending : property == nameof(NoteRow.UpdatedAt));
            _sortProperty = property;
            UpdateSortHeaders();
            ApplyFilter();
        };
        _sortHeaders.Add((columnHeader, header, property));
        grid.Columns.Add(new GridViewColumn
        {
            Header = columnHeader,
            DisplayMemberBinding = new WpfBinding(property),
            Width = width,
        });
    }

    private void UpdateSortHeaders()
    {
        foreach (var (header, label, property) in _sortHeaders)
            header.Content = label + (property == _sortProperty ? (_sortDescending ? " ▼" : " ▲") : "");
    }

    private object SortValue(NoteRow row) => _sortProperty switch
    {
        nameof(NoteRow.Layer) => row.Layer,
        nameof(NoteRow.Pinned) => row.Pinned,
        nameof(NoteRow.State) => row.IsHidden,
        nameof(NoteRow.LockState) => row.IsLocked,
        nameof(NoteRow.Type) => row.IsExternal,
        nameof(NoteRow.Title) => row.Title,
        nameof(NoteRow.Snippet) => row.Snippet,
        nameof(NoteRow.Reminder) => row.ReminderAtValue ?? DateTime.MinValue,
        nameof(NoteRow.UpdatedAt) => row.UpdatedAtValue,
        nameof(NoteRow.Path) => row.Path,
        _ => row.Layer,
    };

    public void RefreshNotes()
    {
        var byId = App.Current.NoteWindows.ToDictionary(w => w.ViewModel.Model.Id);
        _allRows = NoteLayers.Ordered(App.Current.NoteWindows.Select(w => w.ViewModel.Model))
            .Select((note, index) => NoteRow.FromWindow(byId[note.Id]) with { Layer = index + 1, Pinned = note.IsTopmost ? "📌" : "" }).ToList();
        ApplyFilter();
    }

    private void RememberSearch()
    {
        if (string.IsNullOrEmpty(_searchError.Text)) App.Current.RememberNoteSearch(_searchBox.Text);
    }

    private void ApplyFilter()
    {
        var selectedIds = _listView.SelectedItems.Cast<NoteRow>().Select(r => r.Id).ToHashSet();
        try
        {
            var matches = NoteSearch.CreateMatcher(_searchBox.Text.Trim(), (NoteSearchMode)Math.Max(0, _searchMode.SelectedIndex));
            var rows = _allRows.Where(row => new[] { row.Title, row.SearchText, row.Reminder, row.Path }.Any(matches)).ToList();
            var comparer = Comparer<object>.Create((a, b) => a is string text && b is string other
                ? StringComparer.CurrentCultureIgnoreCase.Compare(text, other)
                : System.Collections.Comparer.DefaultInvariant.Compare(a, b));
            rows = (_sortDescending
                ? rows.OrderByDescending(SortValue, comparer)
                : rows.OrderBy(SortValue, comparer)).ToList();
            _listView.ItemsSource = rows;
            foreach (var row in rows.Where(r => selectedIds.Contains(r.Id))) _listView.SelectedItems.Add(row);
            _searchError.Text = "";
        }
        catch (Exception ex) when (ex is ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            _listView.ItemsSource = Array.Empty<NoteRow>();
            _searchError.Text = LocalizationService.T("SearchInvalid");
        }
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

    private void SetSelectedVisibility(bool show)
    {
        var ids = _listView.SelectedItems.Cast<NoteRow>().Select(r => r.Id).ToArray();
        foreach (var id in ids)
        {
            if (show) App.Current.ShowNote(id);
            else App.Current.HideNote(id);
        }
        RefreshNotes();
    }

    private void ChangeSelectedZOrder(LayerMove move)
    {
        if (_sortProperty != nameof(NoteRow.Layer) || !string.IsNullOrEmpty(_searchBox.Text)) return;
        var ids = _listView.SelectedItems.Cast<NoteRow>().Select(r => r.Id).ToHashSet();
        App.Current.MoveNoteLayers(ids, move);
        RefreshNotes();
        if (_listView.SelectedItem is { } selected) _listView.ScrollIntoView(selected);
    }

    private void DeleteSelected()
    {
        if (_listView.SelectedItem is not NoteRow row)
            return;

        var result = WpfMessageBox.Show(
            this,
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

        var currentAt = SelectedWindow()?.ViewModel.Model.Reminder;
        var result = ReminderDialog.ShowFor(this, currentAt);
        if (!result.Accepted)
            return;

        App.Current.SetReminder(row.Id, result.ClearRequested ? null : result.NextAt, result.Settings);
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
        var hasSelection = _listView.SelectedItems.Count == 1;
        var external = hasSelection && selected?.IsExternal == true;
        var canDelete = selected is { IsLocked: false } || external;

        _showHideButton.IsEnabled = _listView.SelectedItems.Cast<NoteRow>().Any(r => r.IsHidden);
        _hideButton.IsEnabled = _listView.SelectedItems.Cast<NoteRow>().Any(r => !r.IsHidden);
        _frontButton.IsEnabled = _backButton.IsEnabled = _upButton.IsEnabled = _downButton.IsEnabled =
            _listView.SelectedItems.Count > 0 && _sortProperty == nameof(NoteRow.Layer) && string.IsNullOrEmpty(_searchBox.Text);
        _deleteButton.IsEnabled = hasSelection && canDelete;
        _deleteButton.Content = external
            ? LocalizationService.T("UnlinkExternalNote")
            : LocalizationService.T("NoteManagerDelete");
        _openExternalButton.IsEnabled = external;
        _openFolderButton.IsEnabled = external;
        _convertButton.IsEnabled = external;
        _reminderButton.IsEnabled = hasSelection;

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
        public int Layer { get; init; }
        public string Pinned { get; init; } = "";
        public string LockState => LocalizationService.T(IsLocked ? "NoteManagerLocked" : "NoteManagerUnlocked");
        public DateTime UpdatedAtValue { get; init; }
        public DateTime? ReminderAtValue { get; init; }
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
                content) { UpdatedAtValue = note.UpdatedAt, ReminderAtValue = note.Reminder?.NextAt };
        }

    }
}
