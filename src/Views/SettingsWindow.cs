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
using System.Windows.Media.Effects;
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
/// 見た目は Windows 11 の設定アプリにそろえ、1項目を1枚のカードにする。
/// カードの左に見出しと1行の説明、右に操作部品を置く。
/// </summary>
public sealed class SettingsWindow : Window
{
    /// <summary>文字の選択肢を出す欄の幅。最も長い「全付箋の表示・非表示を切り替え」が切れない幅。</summary>
    internal const double WidePickerWidth = 260;
    /// <summary>数字だけを選ぶ欄の幅。</summary>
    internal const double NarrowPickerWidth = 120;

    private readonly AppSettings _settings;
    private readonly App _app;
    private readonly WpfTextBox _notesRootBox = new();
    private readonly List<WpfTextBox> _hotkeyBoxes = [];
    public bool IsCapturingHotkey => _hotkeyBoxes.Any(box => box.IsKeyboardFocusWithin);
    private bool _loading = true;

    public SettingsWindow(AppSettings settings, App app)
    {
        _settings = settings;
        _app = app;

        Title = LocalizationService.T("SettingsTitle");
        Width = 760;
        Height = 820;
        MinWidth = 560;
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
            Text = Title, FontSize = 26, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 18),
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
        content.Margin = new Thickness(0, 8, 0, 8);
        var category = new Expander
        {
            Header = new WpfTextBlock
            {
                Text = LocalizationService.T(key), FontSize = 15, FontWeight = FontWeights.SemiBold,
            },
            Content = content,
            IsExpanded = expanded, HorizontalContentAlignment = WpfHorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 14),
        };
        category.SetResourceReference(ForegroundProperty, "SettingsText");
        return category;
    }

    private void ApplyTheme()
    {
        var dark = _settings.IsDarkTheme;
        ControlTheme.Apply(this, dark);
        // Windows 11 の設定と同じく、地を少し暗くしてカードを白く浮かせる。
        Resources["SettingsBackground"] = Brush(dark ? "#202020" : "#F3F3F3");
        Resources["SettingsCard"] = Brush(dark ? "#2B2B2B" : "#FFFFFF");
        Resources["SettingsCardBorder"] = Brush(dark ? "#1D1D1D" : "#E5E5E5");
        Resources["SettingsSurface"] = Brush(dark ? "#373737" : "#FBFBFB");
        Resources["SettingsText"] = Brush(dark ? "#EEEEEE" : "#1B1B1B");
        Resources["SettingsBorder"] = Brush(dark ? "#4A4A4A" : "#D1D1D1");
        Resources["SettingsDivider"] = Brush(dark ? "#3A3A3A" : "#EAEAEA");
        Resources["SettingsHover"] = Brush(dark ? "#444444" : "#F0F0F0");
        Resources["SettingsFooter"] = Brush(dark ? "#1C1C1C" : "#EDEDED");
        Resources["SettingsMuted"] = Brush(dark ? "#ABABAB" : "#616161");
        Resources["SettingsAccent"] = Brush(dark ? "#4CC2FF" : "#005FB8");
        Resources["SettingsOnAccent"] = Brush(dark ? "#000000" : "#FFFFFF");
        Resources["SettingsSwitchOff"] = Brush(dark ? "#CFCFCF" : "#5D5D5D");
    }

    /// <summary>Windows のテーマが変わったとき（「システムに合わせる」のとき）に App から呼ぶ。</summary>
    public void RefreshTheme() => ApplyTheme();

    private static WpfComboBox Picker(double width = WidePickerWidth) => new()
    {
        Height = 32, Width = width,
        VerticalContentAlignment = WpfVerticalAlignment.Center,
    };

    private static WpfTextBlock MutedBlock(string text)
    {
        var block = new WpfTextBlock
        {
            Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        };
        block.SetResourceReference(WpfTextBlock.ForegroundProperty, "SettingsMuted");
        return block;
    }

    /// <summary>
    /// 1項目のカード。左に見出しと説明、右に操作部品。<paramref name="below"/> は
    /// カードの幅いっぱいに見出しの下へ置く（色見本・切り替えの並び・状態の文など）。
    /// </summary>
    private static WpfBorder Card(string titleKey, string? descriptionKey, UIElement? control, UIElement? below = null)
        => CardWithText(LocalizationService.T(titleKey),
            descriptionKey == null ? null : LocalizationService.T(descriptionKey), control, below);

    private static WpfBorder CardWithText(string title, string? description, UIElement? control, UIElement? below = null)
    {
        var grid = new WpfGrid { MinHeight = 36 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = WpfVerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new WpfTextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(description)) text.Children.Add(MutedBlock(description));
        grid.Children.Add(text);

        if (control is FrameworkElement element)
        {
            element.VerticalAlignment = WpfVerticalAlignment.Center;
            element.HorizontalAlignment = WpfHorizontalAlignment.Right;
            Grid.SetColumn(element, 1);
            grid.Children.Add(element);
        }
        if (below is FrameworkElement extra)
        {
            Grid.SetRow(extra, 1);
            Grid.SetColumnSpan(extra, 2);
            grid.Children.Add(extra);
        }

        var card = new WpfBorder
        {
            CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 0, 0, 4),
            Child = grid,
        };
        card.SetResourceReference(WpfBorder.BackgroundProperty, "SettingsCard");
        card.SetResourceReference(WpfBorder.BorderBrushProperty, "SettingsCardBorder");
        return card;
    }

    /// <summary>
    /// 1枚のカードの中に並べる小項目（左に名前、右に操作部品）。区切り線で上と分ける。
    /// </summary>
    private static WpfBorder SubRow(string title, UIElement control)
    {
        var grid = new WpfGrid { MinHeight = 36 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new WpfTextBlock
        {
            Text = title, TextWrapping = TextWrapping.Wrap, VerticalAlignment = WpfVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
        };
        grid.Children.Add(label);
        if (control is FrameworkElement element)
        {
            element.VerticalAlignment = WpfVerticalAlignment.Center;
            element.HorizontalAlignment = WpfHorizontalAlignment.Right;
        }
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        var row = new WpfBorder
        {
            BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 6),
            Margin = new Thickness(0, 8, 0, 0), Child = grid,
        };
        row.SetResourceReference(WpfBorder.BorderBrushProperty, "SettingsDivider");
        return row;
    }

    private static StackPanel SubRows(params UIElement[] rows)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, -6) };
        foreach (var row in rows) panel.Children.Add(row);
        return panel;
    }

    /// <summary>オン・オフのスイッチ。文字は読み上げとテスト用に Content に持ち、画面にはカードの見出しで出す。</summary>
    private WpfCheckBox Switch(string labelKey, Func<bool> read, Action<bool> write)
    {
        var box = new WpfCheckBox
        {
            Content = new WpfTextBlock { Text = LocalizationService.T(labelKey) },
            IsChecked = read(),
        };
        box.SetResourceReference(StyleProperty, "SettingsSwitch");
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

    /// <summary>見出しとスイッチだけのカード。説明はスイッチの文言を使う。</summary>
    private WpfBorder SwitchCard(string titleKey, string switchKey, Func<bool> read, Action<bool> write)
        => Card(titleKey, switchKey, Switch(switchKey, read, write));

    /// <summary>カードの中で、スイッチの文言を左に、スイッチを右に置く小項目。</summary>
    private WpfBorder SwitchRow(string switchKey, Func<bool> read, Action<bool> write)
        => SubRow(LocalizationService.T(switchKey), Switch(switchKey, read, write));

    private void Save()
    {
        ApplyTheme();
        _app.ApplySettingsFromSettingsWindow();
    }

    private static WpfSolidBrush Brush(string hex)
        => new((System.Windows.Media.Color)WpfColorConverter.ConvertFromString(hex));

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

    /// <summary>
    /// 決まった選択肢の欄。今の値が選択肢に無い（settings.json に直接書いたなど）ときは、
    /// その値を足して選んだ状態で見せ、選び直せるようにする。
    /// </summary>
    private WpfComboBox ChoicePicker<T>(double width, IEnumerable<(string Label, T Value)> choices,
        Func<T> read, Action<T> write, Func<T, string>? describeUnknown = null, bool unknownLast = false)
        where T : notnull
    {
        var combo = Picker(width);
        foreach (var (label, value) in choices)
            combo.Items.Add(new WpfComboBoxItem { Content = label, Tag = value });
        if (!SelectByTag(combo, read()))
        {
            var item = new WpfComboBoxItem
            {
                Content = describeUnknown?.Invoke(read()) ?? read().ToString(), Tag = read(),
            };
            if (unknownLast) combo.Items.Add(item); else combo.Items.Insert(0, item);
            combo.SelectedItem = item;
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading || combo.SelectedItem is not WpfComboBoxItem { Tag: T value }) return;
            if (Equals(value, read())) return;
            write(value);
            Save();
        };
        return combo;
    }

    private static IEnumerable<(string, double)> Numbers(params int[] values)
        => values.Select(v => (v.ToString(), (double)v));

    // ─── 新しい付箋の既定値 ──────────────────────────────────────

    private StackPanel BuildNoteDefaultsSection()
    {
        var defaults = _settings.NoteDefaults;
        var panel = new StackPanel();
        var hint = MutedBlock(LocalizationService.T("SettingsNoteDefaultsHint"));
        hint.Margin = new Thickness(2, 0, 0, 10);
        panel.Children.Add(hint);

        panel.Children.Add(Card("SettingsDefaultColor", null, null, BuildColorSwatches(defaults)));
        panel.Children.Add(Card("SettingsDefaultFont", null, BuildFontPicker(defaults)));
        panel.Children.Add(Card("SettingsDefaultFontSize", null, ChoicePicker(NarrowPickerWidth,
            Numbers(8, 9, 10, 11, 12, 13, 14, 16, 18, 20, 24, 28, 32, 36, 40, 48),
            () => defaults.FontSize, v => defaults.FontSize = v, v => v.ToString("0.#"))));
        panel.Children.Add(Card("SettingsDefaultIcon", null, BuildIconPicker(defaults)));
        panel.Children.Add(SwitchCard("SettingsTitleBar", "SettingsDefaultTitleBarHidden",
            () => defaults.TitleBarHidden, v => defaults.TitleBarHidden = v));
        return panel;
    }

    /// <summary>
    /// 付箋の色パレットと同じ色見本。ライト11色とダーク11色を2段に並べ、選んだ色に印を付ける。
    /// 内部の名前（yellow など）は画面に出さない。パレットを切り替えたら並べ直す。
    /// </summary>
    private FrameworkElement BuildColorSwatches(NoteDefaultSettings defaults)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Name = "DefaultColorSwatches" };
        var swatches = new List<WpfButton>();
        _rebuildColorSwatches = Fill;

        void Refresh()
        {
            foreach (var swatch in swatches)
            {
                var selected = Equals(swatch.Tag, defaults.ColorKey);
                swatch.BorderThickness = new Thickness(selected ? 2 : 1);
                ((WpfTextBlock)swatch.Content).Visibility = selected ? Visibility.Visible : Visibility.Hidden;
            }
        }

        WrapPanel Row(IEnumerable<string> keys)
        {
            var row = new WrapPanel();
            foreach (var key in keys)
            {
                var (bg, header) = NoteAppearance.Presets[key];
                var swatch = new WpfButton
                {
                    Template = SwatchTemplate,
                    Width = 30, Height = 30, Margin = new Thickness(0, 0, 6, 6),
                    Background = NoteAppearance.SwatchBrush(Brush(bg).Color, Brush(header).Color),
                    BorderBrush = Brush(header),
                    Content = new WpfTextBlock
                    {
                        Text = UiIcons.CheckMark, FontFamily = UiIcons.Font, FontSize = 13, Foreground = Brush(header),
                        HorizontalAlignment = WpfHorizontalAlignment.Center, VerticalAlignment = WpfVerticalAlignment.Center,
                    },
                    Tag = key, Cursor = System.Windows.Input.Cursors.Hand,
                };
                System.Windows.Automation.AutomationProperties.SetName(swatch, key);
                swatch.Click += (_, _) =>
                {
                    if (_loading || Equals(defaults.ColorKey, key)) return;
                    defaults.ColorKey = key;
                    Refresh();
                    Save();
                };
                swatches.Add(swatch);
                row.Children.Add(swatch);
            }
            return row;
        }

        void Fill()
        {
            panel.Children.Clear();
            swatches.Clear();
            panel.Children.Add(Row(NoteAppearance.LightPresetKeys(_settings.ColorPalette)));
            panel.Children.Add(Row(NoteAppearance.DarkPresetKeys(_settings.ColorPalette)));
            Refresh();
        }

        Fill();
        return panel;
    }

    private Action? _rebuildColorSwatches;

    /// <summary>
    /// 色のパレット。切り替えると付箋の色ピッカーと既定色の見本が入れ替わる。既定色は
    /// 切り替え先の同じ色相の色にする。すでに塗った付箋の色はそのまま。
    /// </summary>
    private WpfBorder BuildColorPaletteCard()
    {
        var picker = ChoicePicker(WidePickerWidth,
            NoteAppearance.Palettes.Select(p => (LocalizationService.T(p.NameKey), p.Id)),
            () => _settings.ColorPalette,
            v =>
            {
                _settings.ColorPalette = v;
                _settings.NoteDefaults.ColorKey = NoteAppearance.ColorKeyForPalette(_settings.NoteDefaults.ColorKey, v);
                _rebuildColorSwatches?.Invoke();
            });
        picker.Name = "ColorPalettePicker";
        return Card("SettingsColorPalette", "SettingsColorPaletteHint", picker);
    }

    private static ControlTemplate? _swatchTemplate;

    private static ControlTemplate SwatchTemplate => _swatchTemplate ??= (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Border x:Name="Face" CornerRadius="6"
                  Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Face" Property="Opacity" Value="0.85"/></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Face" Property="BorderThickness" Value="2"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);

    private WpfComboBox BuildFontPicker(NoteDefaultSettings defaults)
    {
        var combo = Picker();
        // 一覧の取得は時間がかかる。開いた直後は今の設定だけ見せ、揃ったら差し替える。
        combo.Items.Add(new WpfComboBoxItem { Content = defaults.FontFamily, Tag = defaults.FontFamily });
        combo.SelectedIndex = 0;
        combo.Name = "DefaultFontPicker";
        // 一覧は一度読むと使い回すので、2回目以降はここで同期的に揃ってしまう。
        // 窓が出る前だと閉じた窓と区別できずに捨てていたため、出てから詰める。
        Loaded += (_, _) => _ = LoadFontsAsync(combo, defaults);
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
        if (!IsLoaded) return; // 読み終える前に閉じた

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

    /// <summary>
    /// アイコンの既定値。今のアイコンを見せるボタンを押すと、付箋のアイコンピッカーと同じく
    /// 角の丸いカードに一覧が開く。先頭は「なし」。
    /// </summary>
    private WpfButton BuildIconPicker(NoteDefaultSettings defaults)
    {
        var face = new StackPanel { Orientation = WpfOrientation.Horizontal };
        var current = new WpfImage { Width = 20, Height = 20, Name = "DefaultIconImage" };
        var none = new WpfTextBlock { Text = LocalizationService.T("SettingsDefaultIconNone"), VerticalAlignment = WpfVerticalAlignment.Center };
        var chevron = new WpfTextBlock
        {
            Text = UiIcons.ChevronDown, FontFamily = UiIcons.Font, FontSize = 10,
            VerticalAlignment = WpfVerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
        };
        face.Children.Add(current);
        face.Children.Add(none);
        face.Children.Add(chevron);
        var button = new WpfButton
        {
            Content = face, Height = 32, MinWidth = NarrowPickerWidth,
            Padding = new Thickness(10, 0, 10, 0), Name = "DefaultIconPicker",
        };

        void ShowCurrent()
        {
            var source = string.IsNullOrEmpty(defaults.Icon) ? null : EmojiRenderer.Render(defaults.Icon, _settings.MonochromeIcons);
            current.Source = source;
            current.Visibility = source == null ? Visibility.Collapsed : Visibility.Visible;
            none.Visibility = source == null ? Visibility.Visible : Visibility.Collapsed;
        }
        ShowCurrent();

        Popup? popup = null;
        button.Click += (_, _) =>
        {
            popup ??= BuildIconPopup(button, defaults, () => { ShowCurrent(); popup!.IsOpen = false; });
            popup.IsOpen = !popup.IsOpen;
        };
        return button;
    }

    private Popup BuildIconPopup(UIElement anchor, NoteDefaultSettings defaults, Action chosen)
    {
        var grid = new UniformGrid { Columns = 10 };

        WpfButton Cell(object content, string icon, string tooltip)
        {
            var cell = new WpfButton
            {
                Width = 34, Height = 34, Margin = new Thickness(1), Padding = new Thickness(0),
                Content = content, ToolTip = tooltip, Tag = icon,
            };
            cell.Click += (_, _) =>
            {
                if (_loading) return;
                if (defaults.Icon != icon)
                {
                    defaults.Icon = icon;
                    Save();
                }
                chosen();
            };
            return cell;
        }

        grid.Children.Add(Cell(new WpfTextBlock
        {
            Text = UiIcons.Cancel, FontFamily = UiIcons.Font, FontSize = 13,
            HorizontalAlignment = WpfHorizontalAlignment.Center, VerticalAlignment = WpfVerticalAlignment.Center,
        }, "", LocalizationService.T("SettingsDefaultIconNone")));
        foreach (var icon in _settings.IconPalette.Append(defaults.Icon).Where(i => !string.IsNullOrEmpty(i)).Distinct())
            grid.Children.Add(Cell(new WpfImage { Source = EmojiRenderer.Render(icon, _settings.MonochromeIcons), Width = 20, Height = 20 }, icon, icon));

        var card = new WpfBorder
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(6),
            Child = new WpfScrollViewer
            {
                Content = grid, MaxHeight = 300,
                VerticalScrollBarVisibility = WpfScrollBarVisibility.Auto,
            },
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.2, Color = Colors.Black },
        };
        card.SetResourceReference(WpfBorder.BackgroundProperty, "SettingsCard");
        card.SetResourceReference(WpfBorder.BorderBrushProperty, "SettingsBorder");
        return new Popup
        {
            Child = new WpfBorder { Padding = new Thickness(12), Child = card },
            PlacementTarget = anchor, Placement = PlacementMode.Bottom,
            HorizontalOffset = -12, VerticalOffset = -8,
            AllowsTransparency = true, StaysOpen = false,
        };
    }

    // ─── 表示 ────────────────────────────────────────────────────

    private StackPanel BuildAppearanceSection()
    {
        var panel = new StackPanel();
        panel.Children.Add(Card("SettingsTheme", null, ChoicePicker(WidePickerWidth,
            [
                (LocalizationService.T("SettingsThemeLight"), AppSettings.ThemeLight),
                (LocalizationService.T("SettingsThemeDark"), AppSettings.ThemeDark),
                (LocalizationService.T("SettingsThemeSystem"), AppSettings.ThemeSystem),
            ],
            () => _settings.Theme, v => _settings.Theme = v)));

        panel.Children.Add(Card("TrayLanguage", null, ChoicePicker(WidePickerWidth,
            LocalizationService.Languages.Select(entry => (entry.NativeName, entry.Code)),
            () => _settings.Language, v => _settings.Language = v)));

        // 0 は数字より「角のまま」と書いたほうが伝わる。
        panel.Children.Add(BuildColorPaletteCard());

        panel.Children.Add(Card("SettingsNoteCorner", null, ChoicePicker(NarrowPickerWidth,
            new[] { 0, 2, 4, 6, 8, 12, 16 }.Select(r =>
                (r == 0 ? LocalizationService.T("SettingsNoteCornerSquare") : r.ToString(), (double)r)),
            () => _settings.Layout.NoteCornerRadius, v => _settings.Layout.NoteCornerRadius = v,
            v => v.ToString("0.#"))));

        // 決め打ちの3種類だけ並べる。settings.json に直接 "#RRGGBB" を書いた人は、
        // その色を選択済みの項目として足し、選び直せるようにする。
        panel.Children.Add(Card("SettingsNoteBorder", null, ChoicePicker(WidePickerWidth,
            [
                (LocalizationService.T("SettingsNoteBorderNone"), AppSettings.NoteBorderNone),
                (LocalizationService.T("SettingsNoteBorderGray"), AppSettings.NoteBorderGray),
                (LocalizationService.T("SettingsNoteBorderNoteColor"), AppSettings.NoteBorderNoteColor),
            ],
            () => _settings.NoteBorderColor, v => _settings.NoteBorderColor = v, unknownLast: true)));

        panel.Children.Add(SwitchCard("SettingsIconColor", "SettingsMonochromeIcons",
            () => _settings.MonochromeIcons, v => _settings.MonochromeIcons = v));
        panel.Children.Add(BuildTitleBarSpineCard());
        panel.Children.Add(SwitchCard("SettingsMarkdown", "SettingsJoinMarkdownLines",
            () => _settings.JoinMarkdownLines, v => _settings.JoinMarkdownLines = v));
        return panel;
    }

    // タイトルバーを隠している付箋の左端に出す帯。太さと置き場所の選択は、
    // 帯そのものを出さない設定のときは触れないようにする（効かない欄が残ると迷う）。
    // 端からの距離はさらに、端に貼り付ける従来の置き方では意味を持たないので伏せる。
    private WpfBorder BuildTitleBarSpineCard()
    {
        var width = ChoicePicker(NarrowPickerWidth, Numbers(1, 2, 3, 4, 5, 6, 8, 10, 12),
            () => _settings.Layout.TitleBarHiddenSpineWidth, v => _settings.Layout.TitleBarHiddenSpineWidth = v,
            v => v.ToString("0.#"));
        var inset = ChoicePicker(NarrowPickerWidth, Numbers(0, 1, 2, 3, 4, 5, 6, 8, 10, 12),
            () => _settings.Layout.TitleBarHiddenSpineInset, v => _settings.Layout.TitleBarHiddenSpineInset = v,
            v => v.ToString("0.#"));
        var style = ChoicePicker(WidePickerWidth,
            [
                (LocalizationService.T("SettingsTitleBarSpineStyleInset"), AppSettings.SpineStyleInset),
                (LocalizationService.T("SettingsTitleBarSpineStyleEdge"), AppSettings.SpineStyleEdge),
            ],
            () => _settings.TitleBarHiddenSpineStyle, v => _settings.TitleBarHiddenSpineStyle = v);
        // 画像1枚だけの付箋は縁まで画像を広げるので、帯の居場所が残らない。
        var onImage = ChoicePicker(WidePickerWidth,
            [
                (LocalizationService.T("SettingsTitleBarSpineOnImageGutter"), AppSettings.ImageSpineGutter),
                (LocalizationService.T("SettingsTitleBarSpineOnImageOverlay"), AppSettings.ImageSpineOverlay),
                (LocalizationService.T("SettingsTitleBarSpineOnImageHidden"), AppSettings.ImageSpineHidden),
            ],
            () => _settings.ImageOnlySpineStyle, v => _settings.ImageOnlySpineStyle = v);

        var toggle = Switch("SettingsShowTitleBarSpine",
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
        style.SelectionChanged += (_, _) => SyncEnabled();
        toggle.Checked += (_, _) => SyncEnabled();
        toggle.Unchecked += (_, _) => SyncEnabled();
        SyncEnabled();

        return Card("SettingsTitleBarSpine", "SettingsShowTitleBarSpine", toggle, SubRows(
            SubRow(LocalizationService.T("SettingsTitleBarSpineStyle"), style),
            SubRow(LocalizationService.T("SettingsTitleBarSpineWidth"), width),
            SubRow(LocalizationService.T("SettingsTitleBarSpineInset"), inset),
            SubRow(LocalizationService.T("SettingsTitleBarSpineOnImage"), onImage)));
    }

    // ─── 動作 ────────────────────────────────────────────────────

    private StackPanel BuildBehaviorSection()
    {
        var panel = new StackPanel();
        // スタートアップだけはレジストリ登録を伴うので、App 側の処理を通す。
        panel.Children.Add(Card("SettingsStartup", null, null, SubRows(
            SwitchRow("TrayStartup", () => _settings.StartWithWindows, _app.SetStartWithWindows),
            SwitchRow("SettingsStartHidden", () => _settings.StartHidden, v => _settings.StartHidden = v))));
        panel.Children.Add(SwitchCard("SettingsReminder", "SettingsBringReminderNoteToFront",
            () => _settings.BringReminderNoteToFront, v => _settings.BringReminderNoteToFront = v));
        panel.Children.Add(Card("SettingsReminderSound", "SettingsReminderSoundHint", BuildReminderSoundPicker()));
        panel.Children.Add(Card("SettingsReminderAlertSeconds", "SettingsReminderAlertSecondsHint", NumberBox(
            () => _settings.ReminderAlertSeconds, v => _settings.ReminderAlertSeconds = v,
            min: 1, max: AppSettings.MaxReminderAlertSeconds)));
        panel.Children.Add(SwitchCard("SettingsTaskbar", "TrayShowInTaskbar",
            () => _settings.ShowNotesInTaskbar, v => _settings.ShowNotesInTaskbar = v));

        panel.Children.Add(Card("SettingsTrayClick", null, ChoicePicker(WidePickerWidth,
            [
                (LocalizationService.T("TrayClickToggleAll"), "ToggleAll"),
                (LocalizationService.T("TrayClickNewNote"), "NewNote"),
            ],
            () => _settings.TrayClickAction, v => _settings.TrayClickAction = v)));
        panel.Children.Add(BuildHotkeyCard("SettingsNewNoteHotkey", "HotkeyHint",
            s => s.NewNoteHotkey, v => _settings.NewNoteHotkey = v, _app.TrySetNewNoteHotkey,
            () => _app.NewNoteHotkeyError, GlobalNoteHotkey.DefaultGesture));
        panel.Children.Add(BuildHotkeyCard("SettingsClipboardNoteHotkey", "ClipboardHotkeyHint",
            s => s.ClipboardNoteHotkey, v => _settings.ClipboardNoteHotkey = v, _app.TrySetClipboardNoteHotkey,
            () => _app.ClipboardNoteHotkeyError, GlobalNoteHotkey.DefaultClipboardGesture));

        panel.Children.Add(Card("SettingsFolding", null, null, SubRows(
            SwitchRow("TrayTitlePreviewTooltip", () => _settings.ShowTitlePreviewTooltip, v => _settings.ShowTitlePreviewTooltip = v),
            SwitchRow("TrayFoldAnimation", () => _settings.EnableFoldAnimation, v => _settings.EnableFoldAnimation = v),
            SwitchRow("TrayFoldButton", () => _settings.ShowFoldButton, v => _settings.ShowFoldButton = v),
            SwitchRow("TrayDoubleClickToToggleView", () => _settings.DoubleClickToToggleView, v => _settings.DoubleClickToToggleView = v),
            SwitchRow("SettingsShowFoldedPositionGhost", () => _settings.ShowFoldedPositionGhost, v => _settings.ShowFoldedPositionGhost = v))));

        panel.Children.Add(Card("SettingsExternalFiles", null, null, SubRows(
            SubRow(LocalizationService.T("SettingsExternalFileMinRefreshInterval"), NumberBox(
                () => _settings.ExternalFile.MinRefreshIntervalMs,
                v => _settings.ExternalFile.MinRefreshIntervalMs = v, min: 0, max: 60_000)),
            SubRow(LocalizationService.T("SettingsExternalFileTailLineCount"), NumberBox(
                () => _settings.ExternalFile.TailLineCount,
                v => _settings.ExternalFile.TailLineCount = v, min: 1, max: 100_000)),
            SubRow(LocalizationService.T("SettingsExternalFileBackgroundCheckInterval"), NumberBox(
                () => _settings.ExternalFile.BackgroundCheckIntervalMs,
                v => _settings.ExternalFile.BackgroundCheckIntervalMs = v, min: 0, max: 3_600_000)))));
        return panel;
    }

    /// <summary>
    /// リマインダーで鳴らす音の選択欄。選び直すたびに一度鳴らし、試聴ボタンでも確かめられる。
    /// settings.json に直接書いたパスや、この PC に無い音も、そのまま選ばれた状態で見せる。
    /// </summary>
    private StackPanel BuildReminderSoundPicker()
    {
        var combo = ChoicePicker(WidePickerWidth,
            ReminderSound.ListSystemSounds().Select(sound => (ReminderSound.DisplayName(sound), sound)),
            () => _settings.ReminderSound, v =>
            {
                _settings.ReminderSound = v;
                ReminderSound.Play(v);
            },
            ReminderSound.DisplayName);
        combo.Name = "ReminderSoundPicker";

        var preview = new WpfButton
        {
            Content = LocalizationService.T("SettingsReminderSoundPreview"),
            Height = 32,
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(8, 0, 0, 0),
        };
        preview.Click += (_, _) => ReminderSound.Play(_settings.ReminderSound);

        var row = new StackPanel { Orientation = WpfOrientation.Horizontal };
        row.Children.Add(combo);
        row.Children.Add(preview);
        return row;
    }

    /// <summary>
    /// 数値だけを受け付ける設定欄。チェックボックスと同じく、フォーカスが
    /// 外れた時点（もしくは Enter）で即座に反映して保存する。
    /// </summary>
    private WpfTextBox NumberBox(Func<int> read, Action<int> write, int min, int max)
    {
        var box = new WpfTextBox
        {
            Text = read().ToString(System.Globalization.CultureInfo.InvariantCulture),
            Width = NarrowPickerWidth,
            Height = 32,
            Padding = new Thickness(6, 0, 6, 0),
            VerticalContentAlignment = WpfVerticalAlignment.Center,
        };
        void Apply()
        {
            if (_loading) return;
            if (!int.TryParse(box.Text, out var value))
                value = read();
            value = Math.Clamp(value, min, max);
            var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (box.Text != text) box.Text = text;
            if (value == read()) return;
            write(value);
            Save();
        }
        box.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        box.LostFocus += (_, _) => Apply();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) Apply();
        };
        return box;
    }

    /// <summary>
    /// ショートカットの指定欄。欄を選んでキーを押すと、その場で登録する（ほかの設定と同じく
    /// 「適用」は無い）。登録できなかったときは前のキーを残し、カードの下に理由を出す。
    /// 新規作成とクリップボードからの作成で同じ部品を使う。
    /// </summary>
    /// <param name="gesture">設定から今のキーを読む。</param>
    /// <param name="storeGesture">登録できたキーをこの画面の設定へ書く。</param>
    private WpfBorder BuildHotkeyCard(string titleKey, string hintKey, Func<AppSettings, string> gesture,
        Action<string> storeGesture, Func<string, bool> trySet, Func<string> error, string defaultGesture)
    {
        var hotkeyBox = new WpfTextBox
        {
            IsReadOnly = true, Width = 180, Height = 32, Padding = new Thickness(6, 0, 6, 0),
            VerticalContentAlignment = WpfVerticalAlignment.Center,
            IsReadOnlyCaretVisible = false, Cursor = System.Windows.Input.Cursors.Hand,
        };
        _hotkeyBoxes.Add(hotkeyBox);
        hotkeyBox.Text = gesture(_settings);
        hotkeyBox.ToolTip = LocalizationService.T(hintKey);
        var status = MutedBlock(error());
        status.Margin = new Thickness(0, 6, 0, 0);
        status.Visibility = status.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        status.Tag = "HotkeyStatus";

        void ShowStatus(string text)
        {
            status.Text = text;
            status.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        void Apply(string value)
        {
            if (trySet(value))
            {
                storeGesture(gesture(_app.Settings));
                hotkeyBox.Text = gesture(_settings);
                ShowStatus(LocalizationService.T(hotkeyBox.Text.Length == 0 ? "HotkeyDisabled" : "HotkeyApplied"));
            }
            else
            {
                hotkeyBox.Text = gesture(_settings);
                ShowStatus(error());
            }
        }

        hotkeyBox.PreviewKeyDown += (sender, e) =>
        {
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            if (key == System.Windows.Input.Key.Tab) return;
            e.Handled = true;
            if (key == System.Windows.Input.Key.Escape) { hotkeyBox.Text = gesture(_settings); ShowStatus(""); return; }
            if (key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift) return;
            var modifiers = System.Windows.Input.Keyboard.Modifiers;
            var keyName = key is >= System.Windows.Input.Key.D0 and <= System.Windows.Input.Key.D9 ? ((char)('0' + key - System.Windows.Input.Key.D0)).ToString() : key.ToString();
            var pressed = (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control) ? "Ctrl+" : "") + (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt) ? "Alt+" : "") + (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift) ? "Shift+" : "") + keyName;
            if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows) || !GlobalNoteHotkey.TryParse(pressed, out _, out _, out var normalized))
            {
                ShowStatus(LocalizationService.T("HotkeyInvalid"));
                return;
            }
            if (normalized == gesture(_settings)) { ShowStatus(""); return; }
            Apply(normalized);
        };

        WpfButton IconButton(string glyph, string tooltipKey, Action onClick)
        {
            var button = new WpfButton
            {
                Content = new WpfTextBlock { Text = glyph, FontFamily = UiIcons.Font, FontSize = 13 },
                Width = 32, Height = 32, Margin = new Thickness(6, 0, 0, 0),
                ToolTip = LocalizationService.T(tooltipKey),
            };
            System.Windows.Automation.AutomationProperties.SetName(button, LocalizationService.T(tooltipKey));
            button.Click += (_, _) => onClick();
            return button;
        }

        var controls = new StackPanel { Orientation = WpfOrientation.Horizontal };
        controls.Children.Add(hotkeyBox);
        controls.Children.Add(IconButton(UiIcons.Reset, "HotkeyDefault", () => Apply(defaultGesture)));
        controls.Children.Add(IconButton(UiIcons.Blocked, "HotkeyDisable", () => Apply("")));
        return Card(titleKey, hintKey, controls, status);
    }

    // ─── データ ──────────────────────────────────────────────────

    private StackPanel BuildDataSection()
    {
        var panel = new StackPanel();

        _notesRootBox.IsReadOnly = true;
        _notesRootBox.BorderThickness = new Thickness(0);
        _notesRootBox.Background = WpfBrushes.Transparent;
        _notesRootBox.Padding = new Thickness(0);
        _notesRootBox.FontSize = 12;
        _notesRootBox.Margin = new Thickness(-2, 2, 0, 0);
        _notesRootBox.SetResourceReference(ForegroundProperty, "SettingsMuted");
        _notesRootBox.Text = StorageService.DataRoot;
        var choose = ActionButton("TraySelectNotesRoot", _app.SelectNotesRootFromSettings);
        panel.Children.Add(Card("SettingsNotesRoot", null, choose, _notesRootBox));

        var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal };
        buttons.Children.Add(ActionButton("TrayExportNotes", _app.ExportNotesFromSettings));
        var import = ActionButton("TrayImportNotes", _app.ImportNotesFromSettings);
        import.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(import);
        panel.Children.Add(Card("SettingsBackup", null, buttons));
        return panel;
    }

    private static WpfButton ActionButton(string labelKey, Action onClick)
    {
        var button = new WpfButton
        {
            Content = LocalizationService.T(labelKey),
            Height = 32,
            Padding = new Thickness(12, 0, 12, 0),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>保存先を変えたあとに表示を追従させる。</summary>
    public void RefreshNotesRoot() => _notesRootBox.Text = StorageService.DataRoot;
}
