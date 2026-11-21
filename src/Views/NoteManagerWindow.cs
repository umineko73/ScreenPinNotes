// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3 of the License.

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenPinNotes.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfListView = System.Windows.Controls.ListView;
using WpfBinding = System.Windows.Data.Binding;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfBrush = System.Windows.Media.Brush;
using WpfSolidBrush = System.Windows.Media.SolidColorBrush;
using WpfImage = System.Windows.Controls.Image;

namespace ScreenPinNotes.Views;

/// <summary>
/// 付箋の一覧。よく使う操作（表示・非表示・重なり順・削除）だけを一覧の上に並べ、
/// ほかは「…」と行の右クリックメニューに置く。行の先頭には付箋の色とアイコンを出し、
/// 状態（非表示・ロック・最前面・リマインダー・外部ファイル）はアイコンで示す。
/// 付箋が変わると一覧も自動で更新する。
/// </summary>
public sealed class NoteManagerWindow : Window
{
    private readonly System.Windows.Controls.ComboBox _searchBox = new() { IsEditable = true, IsTextSearchEnabled = false };
    private readonly TextBlock _searchPlaceholder = new();
    private readonly WpfListView _listView = new();
    private readonly TextBlock _emptyText = new();
    private readonly WpfButton _showButton = new();
    private readonly WpfButton _hideButton = new();
    private readonly WpfButton _frontButton = new();
    private readonly WpfButton _backButton = new();
    private readonly WpfButton _upButton = new();
    private readonly WpfButton _downButton = new();
    private readonly WpfButton _deleteButton = new();
    private readonly WpfButton _moreButton = new();
    private string _sortProperty = nameof(NoteRow.Layer);
    private bool _sortDescending;
    private readonly List<(GridViewColumnHeader Header, string Label, string Property)> _sortHeaders = [];
    private readonly System.Windows.Controls.ComboBox _searchMode = new();
    private readonly TextBlock _searchError = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private List<NoteRow> _allRows = [];
    private string _signature = "";
    private readonly bool _dark;

    public NoteManagerWindow()
    {
        Title = LocalizationService.T("NoteManagerTitle");
        Width = 980;
        Height = 560;
        MinWidth = 720;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI");
        FontSize = 13;
        _dark = App.Current.Settings.IsDarkTheme;
        Background = Brush(_dark ? "#202020" : "#F3F3F3");
        Foreground = Brush(_dark ? "#EEEEEE" : "#1B1B1B");
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/ScreenPinNotes;component/Resources/SettingsStyles.xaml") });
        ControlTheme.Apply(this, _dark);
        Resources["SettingsText"] = Foreground;
        Resources["SettingsSurface"] = Brush(_dark ? "#2B2B2B" : "#FFFFFF");
        Resources["SettingsBorder"] = Brush(_dark ? "#3A3A3A" : "#E0E0E0");
        Resources["SettingsHover"] = Brush(_dark ? "#3A3A3A" : "#F0F0F0");
        Content = BuildContent();
        RefreshNotes();
        Closing += (_, _) => RememberSearch();

        // 付箋の側で何か変わったら一覧にも出す。ほかの画面から変えたときも追いつくよう、
        // 開いている間だけ軽く見比べ、変わったときだけ作り直す。
        _refreshTimer.Tick += (_, _) => RefreshIfChanged();
        IsVisibleChanged += (_, _) => { if (IsVisible) _refreshTimer.Start(); else _refreshTimer.Stop(); };
        Activated += (_, _) => RefreshIfChanged();
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private static WpfSolidBrush Brush(string color)
        => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 20) };

        // 見出しの右に、付箋を増やす操作。
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(header, Dock.Top);
        var actions = new StackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        var newNote = CommandButton(UiIcons.Add, LocalizationService.T("TrayNewNote"), () => { App.Current.AddNewNote(); RefreshNotes(); });
        newNote.SetResourceReference(StyleProperty, "AccentButton");
        actions.Children.Add(newNote);
        var openExternal = CommandButton(UiIcons.OpenFile, LocalizationService.T("TrayOpenExternalNote"), OpenExternalNoteFromDialog);
        openExternal.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(openExternal);
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(new TextBlock { Text = Title, FontSize = 24, FontWeight = FontWeights.SemiBold, VerticalAlignment = System.Windows.VerticalAlignment.Center });
        root.Children.Add(header);

        root.Children.Add(BuildSearchRow());

        _searchError.Foreground = System.Windows.Media.Brushes.IndianRed;
        _searchError.TextWrapping = TextWrapping.Wrap;
        _searchError.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(_searchError, Dock.Top);
        root.Children.Add(_searchError);

        root.Children.Add(BuildCommandBar());

        _listView.SelectionMode = System.Windows.Controls.SelectionMode.Extended;
        _listView.Background = (WpfBrush)Resources["SettingsSurface"];
        _listView.Foreground = Foreground;
        _listView.BorderBrush = (WpfBrush)Resources["SettingsBorder"];
        _listView.ToolTip = null;
        var rowStyle = new Style(typeof(System.Windows.Controls.ListViewItem));
        rowStyle.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(6, 5, 6, 5)));
        // 非表示の付箋は薄く描き、表示中のものと見分けられるようにする。
        var hiddenTrigger = new DataTrigger { Binding = new WpfBinding(nameof(NoteRow.IsHidden)), Value = true };
        hiddenTrigger.Setters.Add(new Setter(OpacityProperty, 0.55));
        rowStyle.Triggers.Add(hiddenTrigger);
        _listView.ItemContainerStyle = rowStyle;
        _listView.View = BuildGridView();
        _listView.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source &&
                ItemsControl.ContainerFromElement(_listView, source) is System.Windows.Controls.ListViewItem)
                ShowSelected();
        };
        _listView.SelectionChanged += (_, _) => UpdateButtons();
        _listView.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
            else if (e.Key == Key.Enter) { ShowSelected(); e.Handled = true; }
        };
        _listView.ContextMenu = new ContextMenu();
        _listView.ContextMenuOpening += (_, e) =>
        {
            if (_listView.SelectedItems.Count == 0) { e.Handled = true; return; }
            _listView.ContextMenu = TrayMenu.Build(RowMenuEntries(), _dark);
        };

        _emptyText.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        _emptyText.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        _emptyText.IsHitTestVisible = false;
        _emptyText.Opacity = 0.6;
        var listHost = new Grid();
        listHost.Children.Add(_listView);
        listHost.Children.Add(_emptyText);
        root.Children.Add(listHost);
        return root;
    }

    private UIElement BuildSearchRow()
    {
        var searchPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(searchPanel, Dock.Top);

        _searchMode.ItemsSource = new[] { LocalizationService.T("SearchPlain"), LocalizationService.T("SearchWildcard"), LocalizationService.T("SearchRegex") };
        _searchMode.SelectedIndex = 0;
        _searchMode.Width = 150;
        _searchMode.Margin = new Thickness(8, 0, 0, 0);
        _searchMode.SelectionChanged += (_, _) => ApplyFilter();
        DockPanel.SetDock(_searchMode, Dock.Right);
        searchPanel.Children.Add(_searchMode);

        _searchBox.MinHeight = 34;
        _searchBox.Padding = new Thickness(8, 4, 8, 4);
        _searchBox.ToolTip = LocalizationService.T("SearchHistoryHint");
        _searchBox.ItemsSource = App.Current.Settings.SearchHistory.ToArray();
        _searchBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => ApplyFilter()));
        _searchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { RememberSearch(); e.Handled = true; }
            else if (e.Key == Key.Escape && _searchBox.Text.Length > 0) { ClearSearch(); e.Handled = true; }
        };
        _searchBox.LostKeyboardFocus += (_, _) => { if (!_searchBox.IsKeyboardFocusWithin) RememberSearch(); };
        _searchBox.DropDownOpened += (_, _) =>
        {
            var query = _searchBox.Text;
            _searchBox.ItemsSource = App.Current.Settings.SearchHistory.ToArray();
            _searchBox.Text = query;
        };

        // 入力欄の中に「検索」と薄く出す（ラベルを外に置かない）。Esc でも消せる。
        _searchPlaceholder.Text = LocalizationService.T("NoteManagerSearchPlaceholder");
        _searchPlaceholder.IsHitTestVisible = false;
        _searchPlaceholder.Opacity = 0.5;
        _searchPlaceholder.Margin = new Thickness(12, 0, 0, 0);
        _searchPlaceholder.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        var clear = new WpfButton
        {
            Content = new TextBlock { Text = UiIcons.Cancel, FontFamily = UiIcons.Font, FontSize = 10 },
            Width = 26, Height = 26, Margin = new Thickness(0, 0, 30, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = System.Windows.VerticalAlignment.Center,
            ToolTip = LocalizationService.T("NoteManagerClearSearch"), Visibility = Visibility.Collapsed,
            BorderThickness = new Thickness(0),
        };
        clear.Click += (_, _) => ClearSearch();
        _searchBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) =>
            {
                var empty = string.IsNullOrEmpty(_searchBox.Text);
                _searchPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
                clear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            }));
        var host = new Grid();
        host.Children.Add(_searchBox);
        host.Children.Add(_searchPlaceholder);
        host.Children.Add(clear);
        searchPanel.Children.Add(host);
        return searchPanel;
    }

    private void ClearSearch()
    {
        _searchBox.IsDropDownOpen = false;
        _searchBox.SelectedIndex = -1;
        _searchBox.Text = "";
        ApplyFilter();
        _searchBox.Focus();
    }

    /// <summary>一覧の上の操作。アイコンと短い名前のボタンを、意味のまとまりごとに区切って並べる。</summary>
    private UIElement BuildCommandBar()
    {
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(bar, Dock.Top);
        void Add(WpfButton button, string glyph, string text, Action click, bool iconOnly = false)
        {
            SetCommandContent(button, glyph, iconOnly ? null : text);
            button.ToolTip = text;
            button.Margin = new Thickness(0, 0, 4, 4);
            button.Click += (_, _) => click();
            System.Windows.Automation.AutomationProperties.SetName(button, text);
            bar.Children.Add(button);
        }
        void Divider() => bar.Children.Add(new Border
        {
            Width = 1, Height = 20, Margin = new Thickness(6, 0, 10, 4), VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Background = (WpfBrush)Resources["SettingsBorder"],
        });

        Add(_showButton, UiIcons.View, LocalizationService.T("NoteManagerShow"), () => SetSelectedVisibility(true));
        Add(_hideButton, UiIcons.Hide, LocalizationService.T("NoteManagerHide"), () => SetSelectedVisibility(false));
        Divider();
        Add(_frontButton, UiIcons.ToEnd, LocalizationService.T("BringToFront"), () => ChangeSelectedZOrder(LayerMove.Top), iconOnly: true);
        Add(_upButton, UiIcons.ArrowUp, LocalizationService.T("LayerUp"), () => ChangeSelectedZOrder(LayerMove.Up), iconOnly: true);
        Add(_downButton, UiIcons.ArrowDown, LocalizationService.T("LayerDown"), () => ChangeSelectedZOrder(LayerMove.Down), iconOnly: true);
        Add(_backButton, UiIcons.ToEnd, LocalizationService.T("SendToBack"), () => ChangeSelectedZOrder(LayerMove.Bottom), iconOnly: true);
        // 「前面へ」は「末尾へ」の矢印を上下に返して使う。
        if (_frontButton.Content is StackPanel { Children: [TextBlock frontGlyph, ..] })
        {
            frontGlyph.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            frontGlyph.RenderTransform = new ScaleTransform(1, -1);
        }
        foreach (var layer in new[] { _frontButton, _upButton, _downButton, _backButton })
            ToolTipService.SetShowOnDisabled(layer, true);
        Divider();
        Add(_deleteButton, UiIcons.Delete, LocalizationService.T("NoteManagerDelete"), DeleteSelected);
        Add(_moreButton, UiIcons.More, LocalizationService.T("NoteManagerMore"), OpenMoreMenu, iconOnly: true);
        return bar;
    }

    private static void SetCommandContent(WpfButton button, string glyph, string? text)
    {
        var face = new StackPanel { Orientation = WpfOrientation.Horizontal };
        face.Children.Add(new TextBlock { Text = glyph, FontFamily = UiIcons.Font, FontSize = 14, VerticalAlignment = System.Windows.VerticalAlignment.Center });
        if (text != null)
            face.Children.Add(new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center });
        button.Content = face;
        button.Height = 32;
        button.MinWidth = 32;
        button.Padding = new Thickness(text == null ? 0 : 10, 0, text == null ? 0 : 12, 0);
    }

    private static WpfButton CommandButton(string glyph, string text, Action click)
    {
        var button = new WpfButton();
        SetCommandContent(button, glyph, text);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>選んでいる付箋に使える操作の一覧。「…」と行の右クリックで同じものを出す。</summary>
    private IReadOnlyList<TrayMenuEntry> RowMenuEntries(bool includeCommon = true)
    {
        var rows = _listView.SelectedItems.Cast<NoteRow>().ToList();
        var single = rows.Count == 1 ? rows[0] : null;
        var external = single?.IsExternal == true;
        var entries = new List<TrayMenuEntry>();
        if (includeCommon)
        {
            entries.Add(new(LocalizationService.T("NoteManagerShow"), () => SetSelectedVisibility(true), UiIcons.View, Enabled: rows.Any(r => r.IsHidden)));
            entries.Add(new(LocalizationService.T("NoteManagerHide"), () => SetSelectedVisibility(false), UiIcons.Hide, Enabled: rows.Any(r => !r.IsHidden)));
            entries.Add(TrayMenuEntry.Separator);
        }
        entries.Add(new(LocalizationService.T("ReminderMenu"), SetSelectedReminder, UiIcons.Clock, Enabled: single != null));
        entries.Add(new(LocalizationService.T("OpenExternalFile"), () => SelectedWindow()?.OpenExternalFile(), UiIcons.OpenFile, Enabled: external));
        entries.Add(new(LocalizationService.T("OpenExternalFolder"), () => SelectedWindow()?.OpenExternalFolder(), UiIcons.Folder, Enabled: external));
        entries.Add(new(LocalizationService.T("ConvertExternalToNormal"), ConvertSelectedExternalNote, UiIcons.Edit, Enabled: external));
        if (includeCommon)
        {
            entries.Add(TrayMenuEntry.Separator);
            entries.Add(new(DeleteLabel(), DeleteSelected, UiIcons.Delete, Enabled: CanDelete()));
        }
        return entries;
    }

    private void OpenMoreMenu()
    {
        var menu = TrayMenu.Build(RowMenuEntries(includeCommon: false), _dark);
        menu.PlacementTarget = _moreButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private GridView BuildGridView()
    {
        var grid = new GridView();
        AddSwatchColumn(grid);
        AddColumn(grid, LocalizationService.T("NoteManagerTitleColumn"), nameof(NoteRow.Title), 220);
        AddStatusColumn(grid);
        AddColumn(grid, LocalizationService.T("NoteManagerSnippetColumn"), nameof(NoteRow.Snippet), 280);
        AddColumn(grid, LocalizationService.T("NoteManagerReminderColumn"), nameof(NoteRow.Reminder), 130);
        AddColumn(grid, LocalizationService.T("NoteManagerUpdatedColumn"), nameof(NoteRow.UpdatedAt), 130);
        AddColumn(grid, LocalizationService.T("ZOrder"), nameof(NoteRow.Layer), 80);
        AddColumn(grid, LocalizationService.T("NoteManagerPathColumn"), nameof(NoteRow.Path), 220);
        UpdateSortHeaders();
        return grid;
    }

    /// <summary>行の先頭の色見本。付箋の地の色にアクセント色の縁を付け、アイコンを載せる。</summary>
    private static void AddSwatchColumn(GridView grid)
    {
        var swatch = new FrameworkElementFactory(typeof(Border));
        swatch.SetValue(WidthProperty, 24.0);
        swatch.SetValue(HeightProperty, 24.0);
        swatch.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        swatch.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        swatch.SetBinding(Border.BackgroundProperty, new WpfBinding(nameof(NoteRow.Background)));
        swatch.SetBinding(Border.BorderBrushProperty, new WpfBinding(nameof(NoteRow.Accent)));
        var icon = new FrameworkElementFactory(typeof(WpfImage));
        icon.SetValue(WidthProperty, 16.0);
        icon.SetValue(HeightProperty, 16.0);
        icon.SetBinding(WpfImage.SourceProperty, new WpfBinding(nameof(NoteRow.Icon)));
        swatch.AppendChild(icon);
        grid.Columns.Add(new GridViewColumn
        {
            Header = new GridViewColumnHeader { Content = "", IsHitTestVisible = false },
            Width = 44,
            CellTemplate = new DataTemplate { VisualTree = swatch },
        });
    }

    /// <summary>状態は文字ではなくアイコンで並べる。何の印かはツールチップで読める。</summary>
    private void AddStatusColumn(GridView grid)
    {
        var header = LocalizationService.T("NoteManagerStateColumn");
        var columnHeader = SortableHeader(header, nameof(NoteRow.State));
        var cell = new FrameworkElementFactory(typeof(TextBlock));
        cell.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(NoteRow.StatusGlyphs)));
        cell.SetBinding(ToolTipProperty, new WpfBinding(nameof(NoteRow.StatusText)));
        cell.SetValue(TextBlock.FontFamilyProperty, UiIcons.Font);
        cell.SetValue(TextBlock.FontSizeProperty, 13.0);
        grid.Columns.Add(new GridViewColumn
        {
            Header = columnHeader, Width = 110,
            CellTemplate = new DataTemplate { VisualTree = cell },
        });
    }

    private GridViewColumnHeader SortableHeader(string header, string property)
    {
        var columnHeader = new GridViewColumnHeader
        {
            Content = header, Tag = property, ToolTip = header, Padding = new Thickness(4, 5, 4, 5),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
        };
        columnHeader.Click += (_, _) =>
        {
            _sortDescending = property != nameof(NoteRow.Layer) && (_sortProperty == property ? !_sortDescending : property == nameof(NoteRow.UpdatedAt));
            _sortProperty = property;
            UpdateSortHeaders();
            ApplyFilter();
        };
        _sortHeaders.Add((columnHeader, header, property));
        return columnHeader;
    }

    private void AddColumn(GridView grid, string header, string property, double width)
    {
        grid.Columns.Add(new GridViewColumn
        {
            Header = SortableHeader(header, property),
            Width = width,
            DisplayMemberBinding = new WpfBinding(property),
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
        nameof(NoteRow.State) => row.StatusGlyphs,
        nameof(NoteRow.Title) => row.Title,
        nameof(NoteRow.Snippet) => row.Snippet,
        nameof(NoteRow.Reminder) => row.ReminderAtValue ?? DateTime.MinValue,
        nameof(NoteRow.UpdatedAt) => row.UpdatedAtValue,
        nameof(NoteRow.Path) => row.Path,
        _ => row.Layer,
    };

    public void RefreshNotes()
    {
        var windows = App.Current.NoteWindows;
        var byId = windows.ToDictionary(w => w.ViewModel.Model.Id);
        var monochrome = App.Current.Settings.MonochromeIcons;
        _allRows = NoteLayers.Ordered(windows.Select(w => w.ViewModel.Model))
            .Select((note, index) => NoteRow.FromWindow(byId[note.Id], monochrome) with { Layer = index + 1 }).ToList();
        _signature = Signature();
        ApplyFilter();
    }

    /// <summary>一覧に出している内容が変わったかを見るための要約。変わったときだけ作り直す。</summary>
    private static string Signature()
    {
        var builder = new System.Text.StringBuilder();
        foreach (var note in NoteLayers.Ordered(App.Current.NoteWindows.Select(w => w.ViewModel.Model)))
            builder.Append(note.Id).Append('|').Append(note.UpdatedAt.Ticks).Append('|').Append(note.IsHidden)
                .Append(note.IsReadOnly).Append(note.IsTopmost).Append(note.ColorKey).Append(note.Icon)
                .Append(note.Title).Append(note.Reminder?.NextAt?.Ticks).Append('\n');
        return builder.ToString();
    }

    private void RefreshIfChanged()
    {
        if (Signature() != _signature) RefreshNotes();
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
            _emptyText.Text = rows.Count > 0 ? ""
                : LocalizationService.T(_allRows.Count == 0 ? "NoteManagerEmpty" : "NoteManagerNoMatch");
        }
        catch (Exception ex) when (ex is ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            _listView.ItemsSource = Array.Empty<NoteRow>();
            _searchError.Text = LocalizationService.T("SearchInvalid");
            _emptyText.Text = "";
        }
        _searchError.Visibility = _searchError.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
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

    private bool CanChangeLayers() => _sortProperty == nameof(NoteRow.Layer) && string.IsNullOrEmpty(_searchBox.Text);

    private void ChangeSelectedZOrder(LayerMove move)
    {
        if (!CanChangeLayers()) return;
        var ids = _listView.SelectedItems.Cast<NoteRow>().Select(r => r.Id).ToHashSet();
        App.Current.MoveNoteLayers(ids, move);
        RefreshNotes();
        if (_listView.SelectedItem is { } selected) _listView.ScrollIntoView(selected);
    }

    private bool CanDelete()
        => _listView.SelectedItems.Count == 1 && _listView.SelectedItem is NoteRow row && (!row.IsLocked || row.IsExternal);

    private string DeleteLabel()
        => _listView.SelectedItems.Count == 1 && _listView.SelectedItem is NoteRow { IsExternal: true }
            ? LocalizationService.T("UnlinkExternalNote")
            : LocalizationService.T("NoteManagerDelete");

    private void DeleteSelected()
    {
        if (!CanDelete() || _listView.SelectedItem is not NoteRow row)
            return;

        if (!ConfirmationDialog.ShowFor(this,
                LocalizationService.T(row.IsExternal ? "UnlinkExternalConfirmMessage" : "DeleteConfirmMessage"),
                LocalizationService.T("DeleteConfirmTitle")))
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
        var rows = _listView.SelectedItems.Cast<NoteRow>().ToList();
        _showButton.IsEnabled = rows.Any(r => r.IsHidden);
        _hideButton.IsEnabled = rows.Any(r => !r.IsHidden);
        var layers = rows.Count > 0 && CanChangeLayers();
        _frontButton.IsEnabled = _backButton.IsEnabled = _upButton.IsEnabled = _downButton.IsEnabled = layers;
        // 重なり順を変えられないときは、その理由（並べ替えか検索）をツールチップで伝える。
        foreach (var (button, key) in new[] { (_frontButton, "BringToFront"), (_upButton, "LayerUp"), (_downButton, "LayerDown"), (_backButton, "SendToBack") })
            button.ToolTip = rows.Count > 0 && !layers
                ? LocalizationService.T(key) + "\n" + LocalizationService.T("NoteManagerLayerDisabledHint")
                : LocalizationService.T(key);
        _deleteButton.IsEnabled = CanDelete();
        SetCommandContent(_deleteButton, UiIcons.Delete, DeleteLabel());
        _moreButton.IsEnabled = rows.Count > 0;
    }

    private sealed record NoteRow(
        string Id,
        string Title,
        string Snippet,
        string Reminder,
        string UpdatedAt,
        string Path,
        bool IsHidden,
        bool IsExternal,
        bool IsLocked,
        string SearchText)
    {
        public int Layer { get; init; }
        public WpfBrush Background { get; init; } = System.Windows.Media.Brushes.White;
        public WpfBrush Accent { get; init; } = System.Windows.Media.Brushes.Gray;
        public ImageSource? Icon { get; init; }
        public string StatusGlyphs { get; init; } = "";
        public string? StatusText { get; init; }
        public string State => StatusGlyphs;
        public DateTime UpdatedAtValue { get; init; }
        public DateTime? ReminderAtValue { get; init; }

        public static NoteRow FromWindow(StickyNoteWindow window, bool monochrome)
        {
            var note = window.ViewModel.Model;
            var content = note.Content.Replace("\r\n", "\n").Replace("\r", "\n");
            var snippet = content.Split('\n')
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Length > 0) ?? "";
            if (snippet.Length > 120)
                snippet = snippet[..120] + "...";

            var marks = new List<(string Glyph, string Text)>();
            if (note.IsHidden) marks.Add((UiIcons.Hide, LocalizationService.T("NoteManagerHidden")));
            if (note.IsTopmost) marks.Add((UiIcons.Pin, LocalizationService.T("TopmostTooltip")));
            if (note.IsReadOnly) marks.Add((UiIcons.Lock, LocalizationService.T("NoteManagerLocked")));
            if (note.Reminder?.NextAt != null) marks.Add((UiIcons.Clock, LocalizationService.T("NoteManagerReminderColumn")));
            if (note.IsExternalContent) marks.Add((UiIcons.Link, LocalizationService.T("NoteManagerTypeExternal")));

            var (background, accent) = NoteAppearance.ResolveColors(note);
            return new NoteRow(
                note.Id,
                window.ViewModel.DisplayTitle,
                snippet,
                note.Reminder?.NextAt?.ToString("yyyy/MM/dd HH:mm") ?? "",
                note.UpdatedAt.ToString("yyyy/MM/dd HH:mm"),
                note.ExternalContentPath ?? "",
                note.IsHidden,
                note.IsExternalContent,
                note.IsReadOnly,
                content)
            {
                UpdatedAtValue = note.UpdatedAt,
                ReminderAtValue = note.Reminder?.NextAt,
                Background = new WpfSolidBrush(background),
                Accent = new WpfSolidBrush(accent),
                Icon = string.IsNullOrEmpty(note.Icon) ? null : EmojiRenderer.Render(note.Icon, monochrome),
                StatusGlyphs = string.Join("  ", marks.Select(m => m.Glyph)),
                // 印が無い行は空のツールチップを出さない。
                StatusText = marks.Count == 0 ? null : string.Join("\n", marks.Select(m => m.Text)),
            };
        }
    }
}
