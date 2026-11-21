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

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using WpfBrushes     = System.Windows.Media.Brushes;
using WpfButton      = System.Windows.Controls.Button;
using WpfBitmapImage = System.Windows.Media.Imaging.BitmapImage;
using WpfCheckBox    = System.Windows.Controls.CheckBox;
using WpfColor       = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfCursors     = System.Windows.Input.Cursors;
using WpfDataFormats = System.Windows.DataFormats;
using WpfFontFamily  = System.Windows.Media.FontFamily;
using WpfImage       = System.Windows.Controls.Image;
using WpfListBox     = System.Windows.Controls.ListBox;
using WpfSolidBrush  = System.Windows.Media.SolidColorBrush;


namespace ScreenPinNotes.Views;

public partial class StickyNoteWindow
{
    // ─── カラーピッカー ──────────────────────────────────────────

    private const int ColorColumns = 11;

    private Popup BuildColorPopup()
    {
        var panel = new WrapPanel { Width = ColorColumns * (PickerCell + PickerCellGap * 2) };
        _colorPanel = panel;

        // 1行目がライト、2行目がダーク。アプリのテーマに関係なく同じ11色ずつを並べる。
        // 同じ列の色がテーマ切り替えで入れ替わる対（NoteAppearance.ThemeCounterpart）。
        foreach (var key in NoteAppearance.LightPresetKeys) panel.Children.Add(BuildColorSwatch(key));
        foreach (var key in NoteAppearance.DarkPresetKeys) panel.Children.Add(BuildColorSwatch(key));

        var heading = PickerHeading(LocalizationService.T("ColorPaletteCustom"), first: false);
        heading.Width = panel.Width - heading.Margin.Left;
        panel.Children.Add(heading);
        var custom = BuildColorSwatch(NoteAppearance.CustomColorKey);
        custom.ToolTip = LocalizationService.T("CustomColorTooltip");
        panel.Children.Add(custom);
        // 見出しが「カスタム」なので、ボタンは文字を繰り返さず編集の印だけにする。
        var edit = CreatePickerCell(GlyphText(UiIcons.Edit, 14), null, LocalizationService.T("CustomColorEdit"));
        edit.Click += (_, _) => OpenCustomColorDialog();
        panel.Children.Add(edit);

        return CreatePickerPopup(panel);
    }

    private WpfButton BuildColorSwatch(string key)
    {
        var btn = CreatePickerCell(null!, key, key);
        btn.BorderThickness = new Thickness(1);
        btn.FontFamily = UiIcons.Font;
        btn.FontSize = 13;
        PaintColorSwatch(btn, key == NoteAppearance.CustomColorKey
            ? CustomColorPreviewNote()
            : new StickyNote { ColorKey = key });
        btn.Click += (s, _) =>
        {
            if (s is not WpfButton { Tag: string k }) return;
            if (k == NoteAppearance.CustomColorKey)
                ViewModel.SetCustomColors(ViewModel.Model.CustomBackgroundColor, ViewModel.Model.CustomAccentColor);
            else
                ViewModel.ColorKey = k;
            ApplyColorChange(closePopup: true);
        };
        return btn;
    }

    private void PaintColorSwatch(WpfButton button, StickyNote note)
    {
        note.OpacityPercent = 100;
        var preview = new NoteAppearance(note, Settings);
        button.Background  = preview.BackgroundBrush;
        button.BorderBrush = preview.HeaderBrush;      // 枠線でヘッダー色も判るようにする
        button.Foreground  = preview.TextForeground;   // 配色に合わせてチェックのコントラストを確保
    }

    // カスタムの見本。まだ色を選んでいなければ今の配色から始まる。
    private StickyNote CustomColorPreviewNote()
    {
        var (background, accent) = NoteAppearance.ResolveColors(ViewModel.Model);
        return new StickyNote
        {
            ColorKey = NoteAppearance.CustomColorKey,
            CustomBackgroundColor = ViewModel.Model.CustomBackgroundColor ?? NoteAppearance.ToHex(background),
            CustomAccentColor = ViewModel.Model.CustomAccentColor ?? NoteAppearance.ToHex(accent),
        };
    }

    // 背景色とポイントカラーを続けて選べるよう、別ダイアログで編集して OK のときだけ反映する。
    private void OpenCustomColorDialog()
    {
        // ピッカーは最前面に出しているので、ダイアログを隠さないよう先に閉じる。
        if (_colorPopup != null) _colorPopup.IsOpen = false;
        var current = CustomColorPreviewNote();
        var dialog = new CustomColorDialog(this, current.CustomBackgroundColor!, current.CustomAccentColor!,
            ViewModel.IsTitleBarHidden, Settings, () => _storage.SaveSettings(Settings));
        if (dialog.ShowDialog() != true) return;
        ViewModel.SetCustomColors(dialog.BackgroundColor, dialog.AccentColor);
        ApplyColorChange(closePopup: false);
    }

    /// <summary>
    /// アプリのテーマが切り替わったとき、対になる色へ書き換える（ダークならライト色をダーク色に、
    /// ライトならダーク色をライト色に）。カスタムなど対のない色はそのまま。
    /// </summary>
    public void SwitchColorForTheme(bool darkTheme)
    {
        var key = NoteAppearance.ColorKeyForTheme(ViewModel.ColorKey, darkTheme);
        if (key == ViewModel.ColorKey) return;
        ViewModel.ColorKey = key;
        ApplyColorChange(closePopup: false);
    }

    private void ApplyColorChange(bool closePopup)
    {
        if (!_isEditMode) LoadContent(ViewModel.Content);
        if (closePopup && _colorPopup != null) _colorPopup.IsOpen = false;
        RequestSave();
    }

    /// <summary>
    /// 文字の大きさを1つ変えるボタン。「A」に上下の矢印を付けた形は上付き・下付き文字に見えて
    /// 意味が伝わらなかったので、何の大きさかは隣の文字（本文・タイトル）で示し、
    /// ボタンは − と ＋ にする。
    /// </summary>
    private static void SetFontSizeButtonContent(WpfButton button, string label, int delta)
    {
        button.Width = 24;
        button.Content = new TextBlock
        {
            Text = delta > 0 ? UiIcons.Add : UiIcons.Remove,
            FontFamily = UiIcons.Font,
            FontSize = 12,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private TextBlock SizeGroupLabel(string key, double leftMargin) => new()
    {
        Text = LocalizationService.T(key),
        Margin = new Thickness(leftMargin, 0, 2, 0),
        FontSize = 11,
        Opacity = 0.7,
        Foreground = PopupForegroundBrush(),
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false,
        Focusable = false,
    };

    // メニューと同じ Popup 内に置き、マウスキャプチャを共有する。
    private Border BuildQuickActionsRow(out WpfButton iconButton)
    {
        WpfButton MakeButton(string content, string tooltip, Action onClick, bool icon = false)
        {
            var btn = new WpfButton
            {
                Content = content,
                Style = (Style)FindResource("EditToolbarButton"),
                Foreground = PopupForegroundBrush(),
                ToolTip = tooltip,
                Focusable = false,
            };
            if (icon)
            {
                btn.FontFamily = UiIcons.Font;
                btn.FontSize = 16;
            }
            btn.Click += (_, e) => { onClick(); e.Handled = true; };
            return btn;
        }

        var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        foreach (var (label, key, title, delta) in new[]
        {
            ("A", "FontSmallerTooltip", false, -1),
            ("A", "FontLargerTooltip", false, 1),
            ("T", "TitleSmallerTooltip", true, -1),
            ("T", "TitleLargerTooltip", true, 1),
        })
        {
            if (delta < 0)
                panel.Children.Add(SizeGroupLabel(title ? "SizeGroupTitle" : "SizeGroupBody", title ? 2 : 4));
            var button = MakeButton(label, "", () =>
            {
                if (title) SetTitleFontSize(ViewModel.TitleFontSize + delta);
                else SetBodyFontSize(ViewModel.FontSize + delta);
            });
            SetFontSizeButtonContent(button, label, delta);
            button.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(title ? "TitleFontSize" : "FontSize")
            {
                Source = ViewModel, StringFormat = LocalizationService.T(key),
            });
            if (delta < 0)
            {
                panel.Children.Add(button);
                // Plain, non-interactive text distinguishes the current size from buttons.
                var size = new TextBlock
                {
                    MinWidth = 20,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 12,
                    Foreground = PopupForegroundBrush(),
                    IsHitTestVisible = false,
                    Focusable = false,
                };
                size.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(title ? "TitleFontSize" : "FontSize")
                {
                    Source = ViewModel, StringFormat = "{0:0.#}",
                });
                panel.Children.Add(size);
            }
            else
            {
                panel.Children.Add(button);
                panel.Children.Add(new Border
                {
                    Width = 1, Height = 16,
                    Margin = new Thickness(5, 0, 5, 0),
                    Background = PopupBorderBrush(),
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                });
            }
        }
        var fontButton = MakeButton("Aa", LocalizationService.T("FontTooltip"),
            () => RunQuickAction(() =>
            {
                if (_fontPopup == null) return;
                ClosePickerPopups(except: _fontPopup);
                var (x, y) = PickerOffsetNearNote();
                _fontPopup.PlacementTarget = RootBorder;
                _fontPopup.Placement = PlacementMode.Relative;
                _fontPopup.HorizontalOffset = x;
                _fontPopup.VerticalOffset = y;
                _fontPopup.IsOpen = true;
            }));
        fontButton.Width = 24;
        panel.Children.Add(fontButton);
        iconButton = MakeButton(UiIcons.Emoji, LocalizationService.T("IconTooltip"),
            () => RunQuickAction(OpenIconPickerAtMouse), icon: true);
        panel.Children.Add(iconButton);
        panel.Children.Add(MakeButton(UiIcons.Palette, LocalizationService.T("ColorTooltip"),
            () => RunQuickAction(OpenColorPickerAtMouse), icon: true));

        var border = new Border
        {
            DataContext = ViewModel,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(3),
            Child = panel,
        };
        border.Background = PopupBackgroundBrush();
        border.SetValue(TextElement.ForegroundProperty, PopupForegroundBrush());
        return border;
    }
    // ─── フォントピッカー ────────────────────────────────────────

    private Popup BuildFontPopup()
    {
        var listBox = new WpfListBox
        {
            Width = 300, Height = 360,
            BorderThickness = new Thickness(0),
            Background = PopupBackgroundBrush(),
            Foreground = PopupForegroundBrush(),
            DisplayMemberPath = "DisplayName",
            FontFamily = new WpfFontFamily("Yu Gothic UI"),
            FontSize = 13,
        };
        VirtualizingPanel.SetIsVirtualizing(listBox, true);
        VirtualizingPanel.SetVirtualizationMode(listBox, VirtualizationMode.Recycling);
        var status = new TextBlock { Margin = new Thickness(6), TextWrapping = TextWrapping.Wrap };
        var retry = new WpfButton { Content = LocalizationService.T("Retry"), Visibility = Visibility.Collapsed };
        var panel = new StackPanel();
        panel.Children.Add(status);
        panel.Children.Add(retry);
        panel.Children.Add(listBox);
        var popup = new Popup
        {
            Child = new Border { Background = PopupBackgroundBrush(), BorderBrush = PopupBorderBrush(),
                BorderThickness = new Thickness(1), Child = panel },
            Placement = PlacementMode.Bottom, StaysOpen = false,
        };
        bool updating = false;
        bool loading = false;
        FontCatalog.Entry[]? current = null;

        void ShowFonts(FontCatalog.Entry[] entries)
        {
            updating = true;
            try
            {
                var frequent = entries.Where(f => Settings.FontUsage.GetValueOrDefault(f.Source) > 0)
                    .OrderByDescending(f => Settings.FontUsage.GetValueOrDefault(f.Source)).Take(5).ToArray();
                var rows = frequent.Select(f => new FontCatalog.Entry(f.Source, "★ " + f.DisplayName))
                    .Concat(entries).ToArray();
                listBox.ItemsSource = rows;
                listBox.SelectedIndex = -1;
                // The picker always opens at its first row, even after a prior selection.
                Dispatcher.BeginInvoke(() =>
                {
                    if (listBox.Items.Count > 0)
                        listBox.ScrollIntoView(listBox.Items[0]);
                }, System.Windows.Threading.DispatcherPriority.Loaded);
                status.Text = "";
                status.Visibility = Visibility.Collapsed;
            }
            finally { updating = false; }
        }
        async Task Load()
        {
            if (loading) return;
            if (current != null) { ShowFonts(current); return; }
            loading = true;
            status.Visibility = Visibility.Visible;
            status.Text = LocalizationService.T("FontsLoading");
            retry.Visibility = Visibility.Collapsed;
            try
            {
                // Show names immediately; symbol metadata inspection never blocks the first list.
                current = await FontCatalog.LoadAsync();
                ShowFonts(current);
                current = await FontCatalog.FilterAsync(current);
                ShowFonts(current);
            }
            catch (Exception ex)
            {
                status.Visibility = Visibility.Visible;
                status.Text = LocalizationService.T("FontsLoadFailed");
                retry.Visibility = Visibility.Visible;
                ErrorReporter.ReportNonFatal("Load installed fonts", ex);
            }
            finally { loading = false; }
        }
        listBox.SelectionChanged += (_, _) =>
        {
            if (updating || listBox.SelectedItem is not FontCatalog.Entry font) return;
            ViewModel.FontFamily = font.Source;
            var count = Settings.FontUsage.GetValueOrDefault(font.Source);
            Settings.FontUsage[font.Source] = (int)Math.Min(int.MaxValue, (long)Math.Max(0, count) + 1);
            _storage.SaveSettings(Settings);
            popup.IsOpen = false;
            RequestSave();
        };
        popup.Opened += async (_, _) => await Load();
        retry.Click += async (_, _) => { current = null; await Load(); };
        return popup;
    }
}
