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
    private readonly WpfTextBox _hotkeyBox = new() { IsReadOnly = true, MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
    public bool IsCapturingHotkey => _hotkeyBox.IsKeyboardFocusWithin;
    private bool _loading = true;

    public SettingsWindow(AppSettings settings, App app)
    {
        _settings = settings;
        _app = app;

        Title = LocalizationService.T("SettingsTitle");
        Width = 720;
        Height = 820;
        MinWidth = 540;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI");
        FontSize = 13;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ScreenPinNotes;component/Resources/SettingsStyles.xaml"),
        });
        SetResourceReference(BackgroundProperty, "SettingsBackground");
        SetResourceReference(ForegroundProperty, "SettingsText");
        ApplyTheme();
        var panel = new StackPanel { Margin = new Thickness(28, 22, 28, 20) };
        panel.Children.Add(new WpfTextBlock
        {
            Text = Title, FontSize = 23, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 20),
        });
        panel.Children.Add(Category("SettingsNoteDefaults", BuildNoteDefaultsSection(), true));
        panel.Children.Add(Category("SettingsAppearance", BuildAppearanceSection()));
        panel.Children.Add(Category("SettingsBehavior", BuildBehaviorSection()));
        panel.Children.Add(Category("SettingsData", BuildDataSection()));

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
            Height = 32,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
        };
        close.Click += (_, _) => Close();
        var footer = new WpfBorder
        {
            Background = Brush("#FAFAFA"), BorderBrush = Brush("#E5E5E5"),
            BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 12, 20, 12),
            Child = close,
        };
        footer.SetResourceReference(WpfBorder.BackgroundProperty, "SettingsFooter");
        footer.SetResourceReference(WpfBorder.BorderBrushProperty, "SettingsDivider");

        var root = new WpfGrid();
        root.SetResourceReference(WpfGrid.BackgroundProperty, "SettingsBackground");
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(scroll, 0);
        Grid.SetRow(footer, 1);
        root.Children.Add(scroll);
        root.Children.Add(footer);
        Content = root;

        _loading = false;
    }

    // ─── 共通の部品 ──────────────────────────────────────────────

    private static Expander Category(string key, StackPanel content, bool expanded = false)
    {
        // The category header replaces the section's former inline heading.
        content.Children.RemoveAt(0);
        content.Margin = new Thickness(0, 12, 0, 12);
        var category = new Expander
        {
            Header = LocalizationService.T(key), Content = content,
            IsExpanded = expanded, HorizontalContentAlignment = WpfHorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 12),
        };
        category.SetResourceReference(ForegroundProperty, "SettingsText");
        return category;
    }

    private void ApplyTheme()
    {
        var dark = string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase);
        Resources["SettingsBackground"] = Brush(dark ? "#202020" : "#FFFFFF");
        Resources["SettingsSurface"] = Brush(dark ? "#303030" : "#FFFFFF");
        Resources["SettingsText"] = Brush(dark ? "#EEEEEE" : "#242424");
        Resources["SettingsBorder"] = Brush(dark ? "#555555" : "#CCCCCC");
        Resources["SettingsDivider"] = Brush(dark ? "#404040" : "#E5E5E5");
        Resources["SettingsHover"] = Brush(dark ? "#444444" : "#EEEEEE");
        Resources["SettingsFooter"] = Brush(dark ? "#252525" : "#FAFAFA");
        Resources["SettingsMuted"] = Brush(dark ? "#AAAAAA" : "#737373");
    }

    private static WpfComboBox Picker(double width = 200) => new()
    {
        Height = 32, MaxWidth = width, HorizontalAlignment = WpfHorizontalAlignment.Stretch,
        VerticalContentAlignment = WpfVerticalAlignment.Center,
    };

    private static WpfTextBlock SectionHeader(string key) => new()
    {
        Text = LocalizationService.T(key),
        FontWeight = FontWeights.SemiBold,
        FontSize = 14,
        Margin = new Thickness(0, 0, 0, 12),
    };

    private static WpfGrid LabeledRow(string labelKey, UIElement control)
    {
        var grid = new WpfGrid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(152) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = new WpfTextBlock
        {
            Text = LocalizationService.T(labelKey),
            VerticalAlignment = WpfVerticalAlignment.Top,
            Margin = new Thickness(0, 6, 12, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        // A left-aligned container caps picker widths while allowing narrow windows to shrink them.
        var host = new WpfGrid { HorizontalAlignment = WpfHorizontalAlignment.Left };
        if (control is WpfComboBox picker)
        {
            host.SetBinding(MaxWidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = grid,
                Converter = new PickerAvailableWidthConverter() });
            host.Width = picker.MaxWidth;
        }
        else host.HorizontalAlignment = WpfHorizontalAlignment.Stretch;
        host.Children.Add(control);
        Grid.SetColumn(label, 0);
        Grid.SetColumn(host, 1);
        grid.Children.Add(label);
        grid.Children.Add(host);
        return grid;
    }

    private WpfCheckBox Toggle(string labelKey, Func<bool> read, Action<bool> write)
    {
        var box = new WpfCheckBox
        {
            Content = new WpfTextBlock { Text = LocalizationService.T(labelKey), TextWrapping = TextWrapping.Wrap },
            IsChecked = read(),
            MinHeight = 30,
            VerticalContentAlignment = WpfVerticalAlignment.Center,
            Padding = new Thickness(4, 0, 0, 0),
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

    private void Save()
    {
        ApplyTheme();
        _app.ApplySettingsFromSettingsWindow();
    }

    private sealed class PickerAvailableWidthConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => Math.Max(0, (double)value - 152);
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    // ─── 新しい付箋の既定値 ──────────────────────────────────────

    private StackPanel BuildNoteDefaultsSection()
    {
        var defaults = _settings.NoteDefaults;
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("SettingsNoteDefaults"));
        var hint = new WpfTextBlock
        {
            Text = LocalizationService.T("SettingsNoteDefaultsHint"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = WpfBrushes.Gray,
            Margin = new Thickness(0, 0, 0, 10),
        };
        hint.SetResourceReference(WpfTextBlock.ForegroundProperty, "SettingsMuted");
        panel.Children.Add(hint);

        panel.Children.Add(LabeledRow("SettingsDefaultColor", BuildColorPicker(defaults)));
        panel.Children.Add(LabeledRow("SettingsDefaultFont", BuildFontPicker(defaults)));
        panel.Children.Add(LabeledRow("SettingsDefaultFontSize", BuildFontSizePicker(defaults)));
        panel.Children.Add(LabeledRow("SettingsDefaultIcon", BuildIconPicker(defaults)));
        panel.Children.Add(LabeledRow("SettingsTitleBar", Toggle("SettingsDefaultTitleBarHidden",
            () => defaults.TitleBarHidden, v => defaults.TitleBarHidden = v)));
        return panel;
    }

    private WpfComboBox BuildColorPicker(NoteDefaultSettings defaults)
    {
        var combo = Picker();
        foreach (var (key, preset) in NoteAppearance.Presets)
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
        var combo = Picker(260);
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
        var combo = Picker(84);
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
        var combo = Picker();
        var iconTemplate = new DataTemplate();
        var imageFactory = new FrameworkElementFactory(typeof(WpfImage));
        imageFactory.SetBinding(WpfImage.SourceProperty, new System.Windows.Data.Binding());
        imageFactory.SetValue(WidthProperty, 18.0);
        imageFactory.SetValue(HeightProperty, 18.0);
        iconTemplate.VisualTree = imageFactory;
        combo.Items.Add(new WpfComboBoxItem
        {
            Content = new WpfTextBlock { Text = LocalizationService.T("SettingsDefaultIconNone") },
            Tag = "",
        });
        foreach (var icon in _settings.IconPalette.Append(defaults.Icon).Where(icon => !string.IsNullOrEmpty(icon)).Distinct())
        {
            combo.Items.Add(new WpfComboBoxItem
            {
                Content = EmojiRenderer.Render(icon), ContentTemplate = iconTemplate, Tag = icon,
            });
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
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("SettingsAppearance"));
        panel.Children.Add(LabeledRow("SettingsTheme", Toggle("TrayDarkMode",
            () => string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase),
            v => _settings.Theme = v ? "Dark" : "Light")));

        var language = Picker();
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
        panel.Children.Add(LabeledRow("SettingsNoteCorner", BuildNoteCornerPicker()));
        panel.Children.Add(LabeledRow("SettingsNoteBorder", BuildNoteBorderPicker()));
        panel.Children.Add(LabeledRow("SettingsIconColor", Toggle("SettingsMonochromeIcons",
            () => _settings.MonochromeIcons, v => _settings.MonochromeIcons = v)));
        AddTitleBarSpineRows(panel);
        return panel;
    }

    private WpfComboBox BuildNoteCornerPicker()
    {
        var combo = Picker(120);
        foreach (var radius in new[] { 0, 2, 4, 6, 8, 12, 16 })
            combo.Items.Add(new WpfComboBoxItem
            {
                // 0 は数字より「角のまま」と書いたほうが伝わる。
                Content = radius == 0 ? LocalizationService.T("SettingsNoteCornerSquare") : radius.ToString(),
                Tag = (double)radius,
            });
        if (!SelectByTag(combo, _settings.Layout.NoteCornerRadius))
        {
            combo.Items.Insert(0, new WpfComboBoxItem
            {
                Content = _settings.Layout.NoteCornerRadius.ToString("0.#"),
                Tag = _settings.Layout.NoteCornerRadius,
            });
            combo.SelectedIndex = 0;
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading || combo.SelectedItem is not WpfComboBoxItem { Tag: double radius }) return;
            _settings.Layout.NoteCornerRadius = radius;
            Save();
        };
        return combo;
    }

    // 決め打ちの3種類だけ並べる。settings.json に直接 "#RRGGBB" を書いた人は、
    // その色を選択済みの項目として足し、選び直せるようにする。
    private WpfComboBox BuildNoteBorderPicker()
    {
        var combo = Picker(160);
        foreach (var (key, value) in new[]
                 {
                     ("SettingsNoteBorderNone", AppSettings.NoteBorderNone),
                     ("SettingsNoteBorderGray", AppSettings.NoteBorderGray),
                     ("SettingsNoteBorderNoteColor", AppSettings.NoteBorderNoteColor),
                 })
            combo.Items.Add(new WpfComboBoxItem { Content = LocalizationService.T(key), Tag = value });
        if (!SelectByTag(combo, _settings.NoteBorderColor))
        {
            combo.Items.Add(new WpfComboBoxItem
            {
                Content = _settings.NoteBorderColor,
                Tag = _settings.NoteBorderColor,
            });
            combo.SelectedIndex = combo.Items.Count - 1;
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading || combo.SelectedItem is not WpfComboBoxItem { Tag: string value }) return;
            _settings.NoteBorderColor = value;
            Save();
        };
        return combo;
    }

    // タイトルバーを隠している付箋の左端に出す帯。太さと置き場所の選択は、
    // 帯そのものを出さない設定のときは触れないようにする（効かない欄が残ると迷う）。
    // 端からの距離はさらに、端に貼り付ける従来の置き方では意味を持たないので伏せる。
    // 行を分けているのは、この画面の入力欄が1本の左端にそろえてあるため。
    private void AddTitleBarSpineRows(StackPanel section)
    {
        var width = Picker(84);
        foreach (var size in new[] { 1, 2, 3, 4, 5, 6, 8, 10, 12 })
            width.Items.Add(new WpfComboBoxItem { Content = size.ToString(), Tag = (double)size });
        if (!SelectByTag(width, _settings.Layout.TitleBarHiddenSpineWidth))
        {
            width.Items.Insert(0, new WpfComboBoxItem
            {
                Content = _settings.Layout.TitleBarHiddenSpineWidth.ToString("0.#"),
                Tag = _settings.Layout.TitleBarHiddenSpineWidth,
            });
            width.SelectedIndex = 0;
        }
        width.SelectionChanged += (_, _) =>
        {
            if (_loading || width.SelectedItem is not WpfComboBoxItem { Tag: double size }) return;
            _settings.Layout.TitleBarHiddenSpineWidth = size;
            Save();
        };

        var inset = Picker(84);
        foreach (var gap in new[] { 0, 1, 2, 3, 4, 5, 6, 8, 10, 12 })
            inset.Items.Add(new WpfComboBoxItem { Content = gap.ToString(), Tag = (double)gap });
        if (!SelectByTag(inset, _settings.Layout.TitleBarHiddenSpineInset))
        {
            inset.Items.Insert(0, new WpfComboBoxItem
            {
                Content = _settings.Layout.TitleBarHiddenSpineInset.ToString("0.#"),
                Tag = _settings.Layout.TitleBarHiddenSpineInset,
            });
            inset.SelectedIndex = 0;
        }
        inset.SelectionChanged += (_, _) =>
        {
            if (_loading || inset.SelectedItem is not WpfComboBoxItem { Tag: double gap }) return;
            _settings.Layout.TitleBarHiddenSpineInset = gap;
            Save();
        };

        var style = Picker(160);
        foreach (var (key, value) in new[]
                 {
                     ("SettingsTitleBarSpineStyleInset", AppSettings.SpineStyleInset),
                     ("SettingsTitleBarSpineStyleEdge", AppSettings.SpineStyleEdge),
                 })
            style.Items.Add(new WpfComboBoxItem { Content = LocalizationService.T(key), Tag = value });
        SelectByTag(style, _settings.TitleBarHiddenSpineStyle);

        // 画像1枚だけの付箋は縁まで画像を広げるので、帯の居場所が残らない。
        var onImage = Picker(160);
        foreach (var (key, value) in new[]
                 {
                     ("SettingsTitleBarSpineOnImageGutter", AppSettings.ImageSpineGutter),
                     ("SettingsTitleBarSpineOnImageOverlay", AppSettings.ImageSpineOverlay),
                     ("SettingsTitleBarSpineOnImageHidden", AppSettings.ImageSpineHidden),
                 })
            onImage.Items.Add(new WpfComboBoxItem { Content = LocalizationService.T(key), Tag = value });
        SelectByTag(onImage, _settings.ImageOnlySpineStyle);
        onImage.SelectionChanged += (_, _) =>
        {
            if (_loading || onImage.SelectedItem is not WpfComboBoxItem { Tag: string value }) return;
            if (string.Equals(value, _settings.ImageOnlySpineStyle, StringComparison.OrdinalIgnoreCase)) return;
            _settings.ImageOnlySpineStyle = value;
            Save();
        };

        var toggle = Toggle("SettingsShowTitleBarSpine",
            () => _settings.ShowTitleBarHiddenSpine, v => _settings.ShowTitleBarHiddenSpine = v);

        void SyncEnabled()
        {
            var shown = toggle.IsChecked == true;
            width.IsEnabled = shown;
            style.IsEnabled = shown;
            onImage.IsEnabled = shown;
            inset.IsEnabled = shown
                && style.SelectedItem is WpfComboBoxItem { Tag: string tag }
                && !string.Equals(tag, AppSettings.SpineStyleEdge, StringComparison.OrdinalIgnoreCase);
        }

        style.SelectionChanged += (_, _) =>
        {
            SyncEnabled();
            if (_loading || style.SelectedItem is not WpfComboBoxItem { Tag: string value }) return;
            if (string.Equals(value, _settings.TitleBarHiddenSpineStyle, StringComparison.OrdinalIgnoreCase)) return;
            _settings.TitleBarHiddenSpineStyle = value;
            Save();
        };
        toggle.Checked += (_, _) => SyncEnabled();
        toggle.Unchecked += (_, _) => SyncEnabled();
        SyncEnabled();

        section.Children.Add(LabeledRow("SettingsTitleBarSpine", toggle));
        section.Children.Add(LabeledRow("SettingsTitleBarSpineStyle", style));
        section.Children.Add(LabeledRow("SettingsTitleBarSpineWidth", width));
        section.Children.Add(LabeledRow("SettingsTitleBarSpineInset", inset));
        section.Children.Add(LabeledRow("SettingsTitleBarSpineOnImage", onImage));
    }

    // ─── 動作 ────────────────────────────────────────────────────

    private StackPanel BuildBehaviorSection()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("SettingsBehavior"));
        // スタートアップだけはレジストリ登録を伴うので、App 側の処理を通す。
        panel.Children.Add(LabeledRow("SettingsStartup", Toggle("TrayStartup",
            () => _settings.StartWithWindows, _app.SetStartWithWindows)));
        panel.Children.Add(LabeledRow("SettingsTaskbar", Toggle("TrayShowInTaskbar",
            () => _settings.ShowNotesInTaskbar, v => _settings.ShowNotesInTaskbar = v)));

        var trayClick = Picker();
        trayClick.Items.Add(new WpfComboBoxItem { Content = LocalizationService.T("TrayClickToggleAll"), Tag = "ToggleAll" });
        trayClick.Items.Add(new WpfComboBoxItem { Content = LocalizationService.T("TrayClickNewNote"), Tag = "NewNote" });
        SelectByTag(trayClick, _settings.TrayClickAction);
        trayClick.SelectionChanged += (_, _) =>
        {
            if (_loading || trayClick.SelectedItem is not WpfComboBoxItem { Tag: string action }) return;
            if (string.Equals(action, _settings.TrayClickAction, StringComparison.OrdinalIgnoreCase)) return;
            _settings.TrayClickAction = action;
            Save();
        };
        panel.Children.Add(LabeledRow("SettingsTrayClick", trayClick));
        panel.Children.Add(LabeledRow("SettingsNewNoteHotkey", BuildHotkeyEditor()));

        var folding = new StackPanel();
        folding.Children.Add(Toggle("TrayTitlePreviewTooltip",
            () => _settings.ShowTitlePreviewTooltip, v => _settings.ShowTitlePreviewTooltip = v));
        folding.Children.Add(Toggle("TrayFoldAnimation",
            () => _settings.EnableFoldAnimation, v => _settings.EnableFoldAnimation = v));
        folding.Children.Add(Toggle("TrayFoldButton",
            () => _settings.ShowFoldButton, v => _settings.ShowFoldButton = v));
        folding.Children.Add(Toggle("TrayDoubleClickToToggleView",
            () => _settings.DoubleClickToToggleView, v => _settings.DoubleClickToToggleView = v));
        panel.Children.Add(LabeledRow("SettingsFolding", folding));
        return panel;
    }

    private StackPanel BuildHotkeyEditor()
    {
        var panel = new StackPanel();
        _hotkeyBox.Text = _settings.NewNoteHotkey;
        _hotkeyBox.ToolTip = LocalizationService.T("HotkeyHint");
        var status = new WpfTextBlock { Text = _app.NewNoteHotkeyError, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        status.SetResourceReference(ForegroundProperty, "SettingsText");
        _hotkeyBox.PreviewKeyDown += (sender, e) =>
        {
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            if (key == System.Windows.Input.Key.Tab) return;
            e.Handled = true;
            if (key == System.Windows.Input.Key.Escape) { _hotkeyBox.Text = _settings.NewNoteHotkey; return; }
            if (key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift) return;
            var modifiers = System.Windows.Input.Keyboard.Modifiers;
            var keyName = key is >= System.Windows.Input.Key.D0 and <= System.Windows.Input.Key.D9 ? ((char)('0' + key - System.Windows.Input.Key.D0)).ToString() : key.ToString();
            var gesture = (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control) ? "Ctrl+" : "") + (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt) ? "Alt+" : "") + (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift) ? "Shift+" : "") + keyName;
            if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows) || !GlobalNoteHotkey.TryParse(gesture, out _, out _, out var normalized))
                status.Text = LocalizationService.T("HotkeyInvalid");
            else { _hotkeyBox.Text = normalized; status.Text = ""; }
        };
        panel.Children.Add(_hotkeyBox);
        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        void Apply(string gesture)
        {
            if (_app.TrySetNewNoteHotkey(gesture))
            {
                _settings.NewNoteHotkey = _app.Settings.NewNoteHotkey;
                _hotkeyBox.Text = _settings.NewNoteHotkey;
                status.Text = LocalizationService.T(_settings.NewNoteHotkey.Length == 0 ? "HotkeyDisabled" : "HotkeyApplied");
            }
            else status.Text = _app.NewNoteHotkeyError;
        }
        foreach (var (label, action) in new (string, Action)[]
        {
            ("HotkeyApply", () => Apply(_hotkeyBox.Text)),
            ("HotkeyDefault", () => Apply(GlobalNoteHotkey.DefaultGesture)),
            ("HotkeyDisable", () => Apply("")),
        })
        {
            var button = new WpfButton { Content = LocalizationService.T(label), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 6) };
            button.Click += (_, _) => action();
            buttons.Children.Add(button);
        }
        panel.Children.Add(buttons);
        panel.Children.Add(new WpfTextBlock { Text = LocalizationService.T("HotkeyHint"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(status);
        return panel;
    }

    // ─── データ ──────────────────────────────────────────────────

    private StackPanel BuildDataSection()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("SettingsData"));

        _notesRootBox.IsReadOnly = true;
        _notesRootBox.Height = 32;
        _notesRootBox.Padding = new Thickness(8, 0, 8, 0);
        _notesRootBox.VerticalContentAlignment = WpfVerticalAlignment.Center;
        _notesRootBox.Text = StorageService.DataRoot;
        var folder = new StackPanel();
        folder.Children.Add(_notesRootBox);
        var choose = ActionButton("TraySelectNotesRoot", _app.SelectNotesRootFromSettings);
        choose.HorizontalAlignment = WpfHorizontalAlignment.Left;
        choose.Margin = new Thickness(0, 8, 0, 0);
        folder.Children.Add(choose);
        panel.Children.Add(LabeledRow("SettingsNotesRoot", folder));

        var buttons = new WrapPanel();
        buttons.Children.Add(ActionButton("TrayExportNotes", _app.ExportNotesFromSettings));
        buttons.Children.Add(ActionButton("TrayImportNotes", _app.ImportNotesFromSettings));
        panel.Children.Add(LabeledRow("SettingsBackup", buttons));
        return panel;
    }

    private static WpfButton ActionButton(string labelKey, Action onClick)
    {
        var button = new WpfButton
        {
            Content = LocalizationService.T(labelKey),
            MinHeight = 32,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(0, 0, 8, 4),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>保存先を変えたあとに表示を追従させる。</summary>
    public void RefreshNotesRoot() => _notesRootBox.Text = StorageService.DataRoot;
}
