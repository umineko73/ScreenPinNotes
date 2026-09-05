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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfGrid = System.Windows.Controls.Grid;
using WpfBorder = System.Windows.Controls.Border;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfScrollViewer = System.Windows.Controls.ScrollViewer;
using WpfScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfImage = System.Windows.Controls.Image;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfSolidBrush = System.Windows.Media.SolidColorBrush;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace ScreenPinNotes.Views;

/// <summary>
/// 設定画面。タスクトレイのメニューに項目が増えすぎたので、一覧できる場所へ移した。
/// 変更はその場で反映して保存する。トレイのチェック項目がそうだったので、
/// OK / キャンセルは持たない。
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly App _app;
    private readonly WpfTextBox _notesRootBox = new();
    private bool _loading = true;

    public SettingsWindow(AppSettings settings, App app)
    {
        _settings = settings;
        _app = app;

        Title = LocalizationService.T("SettingsTitle");
        Width = 520;
        Height = 640;
        MinWidth = 460;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(BuildNoteDefaultsSection());
        panel.Children.Add(BuildAppearanceSection());
        panel.Children.Add(BuildBehaviorSection());
        panel.Children.Add(BuildDataSection());

        var scroll = new WpfScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = WpfScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = WpfScrollBarVisibility.Disabled,
        };

        var close = new WpfButton
        {
            Content = LocalizationService.T("Close"),
            Width = 96,
            Height = 30,
            Margin = new Thickness(18, 0, 18, 16),
            HorizontalAlignment = WpfHorizontalAlignment.Right,
        };
        close.Click += (_, _) => Close();

        var root = new WpfGrid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(scroll, 0);
        Grid.SetRow(close, 1);
        root.Children.Add(scroll);
        root.Children.Add(close);
        Content = root;

        _loading = false;
    }

    // ─── 共通の部品 ──────────────────────────────────────────────

    private static WpfTextBlock SectionHeader(string key) => new()
    {
        Text = LocalizationService.T(key),
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private static WpfGrid LabeledRow(string labelKey, UIElement control)
    {
        var grid = new WpfGrid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = new WpfTextBlock
        {
            Text = LocalizationService.T(labelKey),
            VerticalAlignment = WpfVerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(label);
        grid.Children.Add(control);
        return grid;
    }

    private WpfCheckBox Toggle(string labelKey, Func<bool> read, Action<bool> write)
    {
        var box = new WpfCheckBox
        {
            Content = LocalizationService.T(labelKey),
            IsChecked = read(),
            Margin = new Thickness(0, 0, 0, 8),
        };
        void Apply(object? _, RoutedEventArgs __)
        {
            if (_loading) return;
            write(box.IsChecked == true);
            Save();
        }
        box.Checked += Apply;
        box.Unchecked += Apply;
        return box;
    }

    private void Save() => _app.ApplySettingsFromSettingsWindow();

    // ─── 新しい付箋の既定値 ──────────────────────────────────────

    private StackPanel BuildNoteDefaultsSection()
    {
        var defaults = _settings.NoteDefaults;
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        panel.Children.Add(SectionHeader("SettingsNoteDefaults"));
        panel.Children.Add(new WpfTextBlock
        {
            Text = LocalizationService.T("SettingsNoteDefaultsHint"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = WpfBrushes.Gray,
            Margin = new Thickness(0, 0, 0, 10),
        });

        panel.Children.Add(LabeledRow("SettingsDefaultColor", BuildColorPicker(defaults)));
        panel.Children.Add(LabeledRow("SettingsDefaultFont", BuildFontPicker(defaults)));
        panel.Children.Add(LabeledRow("SettingsDefaultFontSize", BuildFontSizePicker(defaults)));
        panel.Children.Add(LabeledRow("SettingsDefaultIcon", BuildIconPicker(defaults)));
        panel.Children.Add(Toggle("SettingsDefaultTitleBarHidden",
            () => defaults.TitleBarHidden, v => defaults.TitleBarHidden = v));
        return panel;
    }

    private WpfComboBox BuildColorPicker(NoteDefaultSettings defaults)
    {
        var combo = new WpfComboBox { Height = 26 };
        foreach (var (key, preset) in StickyNoteViewModel.ColorPresets)
        {
            var row = new StackPanel { Orientation = WpfOrientation.Horizontal };
            row.Children.Add(new WpfBorder
            {
                Width = 34,
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = Brush(preset.Bg),
                BorderBrush = Brush(preset.Header),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = WpfVerticalAlignment.Center,
            });
            row.Children.Add(new WpfTextBlock { Text = key, VerticalAlignment = WpfVerticalAlignment.Center });
            combo.Items.Add(new WpfComboBoxItem { Content = row, Tag = key });
        }
        SelectByTag(combo, defaults.ColorKey);
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading || combo.SelectedItem is not WpfComboBoxItem { Tag: string key }) return;
            defaults.ColorKey = key;
            Save();
        };
        return combo;
    }

    private static WpfSolidBrush Brush(string hex)
        => new((System.Windows.Media.Color)WpfColorConverter.ConvertFromString(hex));

    private WpfComboBox BuildFontPicker(NoteDefaultSettings defaults)
    {
        var combo = new WpfComboBox { Height = 26 };
        // 一覧の取得は時間がかかる。開いた直後は今の設定だけ見せ、揃ったら差し替える。
        combo.Items.Add(new WpfComboBoxItem { Content = defaults.FontFamily, Tag = defaults.FontFamily });
        combo.SelectedIndex = 0;
        _ = LoadFontsAsync(combo, defaults);
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading || combo.SelectedItem is not WpfComboBoxItem { Tag: string source }) return;
            defaults.FontFamily = source;
            Save();
        };
        return combo;
    }

    private async Task LoadFontsAsync(WpfComboBox combo, NoteDefaultSettings defaults)
    {
        FontCatalog.Entry[] fonts;
        try { fonts = await FontCatalog.FilterAsync(await FontCatalog.LoadAsync()); }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Load fonts for the settings window", ex);
            return;
        }
        if (!IsLoaded && !IsVisible) return;

        var wasLoading = _loading;
        _loading = true;
        try
        {
            combo.Items.Clear();
            foreach (var font in fonts)
                combo.Items.Add(new WpfComboBoxItem { Content = font.DisplayName, Tag = font.Source });
            if (!SelectByTag(combo, defaults.FontFamily))
            {
                // 一覧に無い書体（アンインストール済みなど）でも設定は残す。
                combo.Items.Insert(0, new WpfComboBoxItem { Content = defaults.FontFamily, Tag = defaults.FontFamily });
                combo.SelectedIndex = 0;
            }
        }
        finally { _loading = wasLoading; }
    }

    private WpfComboBox BuildFontSizePicker(NoteDefaultSettings defaults)
    {
        var combo = new WpfComboBox { Height = 26, Width = 90, HorizontalAlignment = WpfHorizontalAlignment.Left };
        // 付箋側の A- / A+ と同じ 8〜48 の範囲から、よく使う刻みだけ出す。
        foreach (var size in new[] { 8, 9, 10, 11, 12, 13, 14, 16, 18, 20, 24, 28, 32, 36, 40, 48 })
            combo.Items.Add(new WpfComboBoxItem { Content = size.ToString(), Tag = (double)size });
        if (!SelectByTag(combo, defaults.FontSize))
        {
            combo.Items.Insert(0, new WpfComboBoxItem { Content = defaults.FontSize.ToString("0.#"), Tag = defaults.FontSize });
            combo.SelectedIndex = 0;
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading || combo.SelectedItem is not WpfComboBoxItem { Tag: double size }) return;
            defaults.FontSize = size;
            Save();
        };
        return combo;
    }

    private WpfComboBox BuildIconPicker(NoteDefaultSettings defaults)
    {
        var combo = new WpfComboBox { Height = 26 };
        combo.Items.Add(new WpfComboBoxItem
        {
            Content = new WpfTextBlock { Text = LocalizationService.T("SettingsDefaultIconNone") },
            Tag = "",
        });
        foreach (var icon in _settings.IconPalette)
        {
            var image = new WpfImage { Source = EmojiRenderer.Render(icon), Width = 18, Height = 18 };
            combo.Items.Add(new WpfComboBoxItem { Content = image, Tag = icon });
        }
        SelectByTag(combo, defaults.Icon);
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading || combo.SelectedItem is not WpfComboBoxItem { Tag: string icon }) return;
            defaults.Icon = icon;
            Save();
        };
        return combo;
    }

    private static bool SelectByTag(WpfComboBox combo, object value)
    {
        foreach (WpfComboBoxItem item in combo.Items)
        {
            if (Equals(item.Tag, value))
            {
                combo.SelectedItem = item;
                return true;
            }
        }
        return false;
    }

    // ─── 表示 ────────────────────────────────────────────────────

    private StackPanel BuildAppearanceSection()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        panel.Children.Add(SectionHeader("SettingsAppearance"));
        panel.Children.Add(Toggle("TrayDarkMode",
            () => string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase),
            v => _settings.Theme = v ? "Dark" : "Light"));

        var language = new WpfComboBox { Height = 26, Width = 200, HorizontalAlignment = WpfHorizontalAlignment.Left };
        foreach (var entry in LocalizationService.Languages)
            language.Items.Add(new WpfComboBoxItem { Content = entry.NativeName, Tag = entry.Code });
        SelectByTag(language, _settings.Language);
        language.SelectionChanged += (_, _) =>
        {
            if (_loading || language.SelectedItem is not WpfComboBoxItem { Tag: string code }) return;
            if (string.Equals(code, _settings.Language, StringComparison.OrdinalIgnoreCase)) return;
            _settings.Language = code;
            Save();
        };
        panel.Children.Add(LabeledRow("TrayLanguage", language));
        return panel;
    }

    // ─── 動作 ────────────────────────────────────────────────────

    private StackPanel BuildBehaviorSection()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        panel.Children.Add(SectionHeader("SettingsBehavior"));
        // スタートアップだけはレジストリ登録を伴うので、App 側の処理を通す。
        panel.Children.Add(Toggle("TrayStartup",
            () => _settings.StartWithWindows, _app.SetStartWithWindows));
        panel.Children.Add(Toggle("TrayTitlePreviewTooltip",
            () => _settings.ShowTitlePreviewTooltip, v => _settings.ShowTitlePreviewTooltip = v));
        panel.Children.Add(Toggle("TrayFoldAnimation",
            () => _settings.EnableFoldAnimation, v => _settings.EnableFoldAnimation = v));
        panel.Children.Add(Toggle("TrayFoldButton",
            () => _settings.ShowFoldButton, v => _settings.ShowFoldButton = v));
        panel.Children.Add(Toggle("TrayDoubleClickToToggleView",
            () => _settings.DoubleClickToToggleView, v => _settings.DoubleClickToToggleView = v));
        return panel;
    }

    // ─── データ ──────────────────────────────────────────────────

    private StackPanel BuildDataSection()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("SettingsData"));

        _notesRootBox.IsReadOnly = true;
        _notesRootBox.Height = 26;
        _notesRootBox.VerticalContentAlignment = WpfVerticalAlignment.Center;
        _notesRootBox.Text = StorageService.DataRoot;
        panel.Children.Add(LabeledRow("SettingsNotesRoot", _notesRootBox));

        var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal };
        buttons.Children.Add(ActionButton("TraySelectNotesRoot", _app.SelectNotesRootFromSettings));
        buttons.Children.Add(ActionButton("TrayExportNotes", _app.ExportNotesFromSettings));
        buttons.Children.Add(ActionButton("TrayImportNotes", _app.ImportNotesFromSettings));
        panel.Children.Add(buttons);
        return panel;
    }

    private static WpfButton ActionButton(string labelKey, Action onClick)
    {
        var button = new WpfButton
        {
            Content = LocalizationService.T(labelKey),
            Height = 28,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 8, 0),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>保存先を変えたあとに表示を追従させる。</summary>
    public void RefreshNotesRoot() => _notesRootBox.Text = StorageService.DataRoot;
}
