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

    private const double ColorSwatch = 28;
    private const double ColorSwatchGap = 3;

    private Popup BuildColorPopup()
    {
        const int Columns = 10;

        // 端数で列が折り返さないよう僅かに余裕を持たせる
        var panel = new WrapPanel { Width = Columns * (ColorSwatch + ColorSwatchGap * 2) + 2 };
        _colorPanel = panel;

        // 1行目がライト、2行目がダーク。アプリのテーマに関係なく同じ10色ずつを並べる。
        foreach (var key in NoteAppearance.LightPresetKeys) panel.Children.Add(BuildColorSwatch(key));
        foreach (var key in NoteAppearance.DarkPresetKeys) panel.Children.Add(BuildColorSwatch(key));

        panel.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("ColorPaletteCustom"), Width = panel.Width,
            Margin = new Thickness(3, 6, 0, 2), FontSize = 12,
            Foreground = PopupForegroundBrush(), Opacity = 0.8,
            FontWeight = FontWeights.SemiBold,
        });
        var custom = BuildColorSwatch(NoteAppearance.CustomColorKey);
        custom.ToolTip = LocalizationService.T("CustomColorTooltip");
        panel.Children.Add(custom);
        var edit = new WpfButton
        {
            // 見出しが「カスタム」なので、ボタンは文字を繰り返さず「…」だけにする。
            Content = "…",
            ToolTip = LocalizationService.T("CustomColorEdit"),
            Width = ColorSwatch, Height = ColorSwatch, Margin = new Thickness(ColorSwatchGap),
            Padding = new Thickness(0),
            Background = PopupBackgroundBrush(), Foreground = PopupForegroundBrush(),
            BorderBrush = PopupBorderBrush(), BorderThickness = new Thickness(1),
            Cursor = WpfCursors.Hand,
        };
        edit.Click += (_, _) => OpenCustomColorDialog();
        panel.Children.Add(edit);

        return new Popup
        {
            Child = new Border
            {
                Background = PopupBackgroundBrush(), BorderBrush = PopupBorderBrush(),
                BorderThickness = new Thickness(1), Padding = new Thickness(4), Child = panel,
            },
            Placement = PlacementMode.Bottom, StaysOpen = false,
        };
    }

    private WpfButton BuildColorSwatch(string key)
    {
        var btn = new WpfButton
        {
            Width = ColorSwatch, Height = ColorSwatch, Margin = new Thickness(ColorSwatchGap),
            Padding         = new Thickness(0),
            BorderThickness = new Thickness(1),
            FontFamily      = UiIcons.Font,
            FontSize        = 13,
            Tag             = key,
            ToolTip         = key,
            Cursor          = WpfCursors.Hand,
        };
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

    private void ApplyColorChange(bool closePopup)
    {
        if (!_isEditMode) LoadContent(ViewModel.Content);
        if (closePopup && _colorPopup != null) _colorPopup.IsOpen = false;
        RequestSave();
    }

    // ─── アイコンピッカー ────────────────────────────────────────

    private Popup BuildIconPopup()
    {
        const double Cell    = 28;
        const double Gap     = 3;
        const int    Columns = 8;

        var panel = new WrapPanel { Width = Columns * (Cell + Gap * 2) + 2 };
        _iconPanel = panel;

        var available = IconList.Distinct().ToHashSet();
        var ordered = new List<(string Icon, string? Heading)> { ("", null) };
        foreach (var group in AppSettings.IconGroups)
        {
            var icons = group.Icons.Where(available.Contains).ToArray();
            if (icons.Length == 0) continue;
            ordered.AddRange(icons.Select((icon, index) => (icon, index == 0 ? group.Key : null)));
        }
        var known = AppSettings.IconGroups.SelectMany(group => group.Icons).ToHashSet();
        ordered.AddRange(available.Where(icon => icon.Length > 0 && !known.Contains(icon))
            .Select((icon, index) => (icon, index == 0 ? "IconOther" : null)));
        foreach (var (icon, heading) in ordered)
        {
            if (heading != null)
                panel.Children.Add(new TextBlock
                {
                    Text = LocalizationService.T(heading), Width = panel.Width,
                    Margin = new Thickness(3, 8, 0, 3), FontSize = 12,
                    Foreground = IsDarkTheme() ? WpfBrushes.WhiteSmoke : WpfBrushes.DimGray,
                    FontWeight = FontWeights.SemiBold,
                });
            bool isNone = icon.Length == 0;
            var btn = new WpfButton
            {
                Width = Cell, Height = Cell, Margin = new Thickness(Gap),
                Padding    = new Thickness(0),
                Content    = isNone
                    ? new TextBlock
                    {
                        Text = UiIcons.Cancel, FontFamily = UiIcons.Font, FontSize = 11, Foreground = WpfBrushes.Gray,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    }
                    : new System.Windows.Controls.Image
                    {
                        Source = RenderEmoji(icon), Width = 20, Height = 20,
                        Stretch = Stretch.Uniform,
                    },
                Tag        = icon,
                ToolTip    = isNone ? LocalizationService.T("NoIconTooltip") : null,
                Cursor     = WpfCursors.Hand,
            };

            // マウスが乗ったときだけ拡大する。RenderTransform なのでレイアウトは
            // 動かず、隣のアイコンを押しのけずに手前で大きくなる。
            var scale = new ScaleTransform(1, 1);
            btn.RenderTransform       = scale;
            btn.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            btn.MouseEnter += (s, _) => ZoomIconButton((UIElement)s, scale, 1.6);
            btn.MouseLeave += (s, _) => ZoomIconButton((UIElement)s, scale, 1.0);
            btn.Click += (s, _) =>
            {
                if (s is WpfButton b && b.Tag is string k)
                {
                    ViewModel.Icon = k;
                    if (_iconPopup != null) _iconPopup.IsOpen = false;
                    RequestSave();
                }
            };
            panel.Children.Add(btn);
        }

        return new Popup
        {
            Child = new Border
            {
                Background = PopupBackgroundBrush(), BorderBrush = PopupBorderBrush(),
                BorderThickness = new Thickness(1), Padding = new Thickness(4), Child = new ScrollViewer
                {
                    MaxHeight = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = panel,
                },
            },
            Placement = PlacementMode.Bottom, StaysOpen = false,
        };
    }

    private static void ZoomIconButton(UIElement button, ScaleTransform scale, double to)
    {
        // 拡大中は手前に出す。WrapPanel は後の要素が上に描画されるため、
        // ZIndex を上げないと右隣・下隣のアイコンに欠けて見える。
        System.Windows.Controls.Panel.SetZIndex(button, to > 1 ? 1 : 0);

        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(App.Current.Settings.Timings.ToolbarFadeMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    // 選択中のアイコンだけ枠線を付けて示す
    private void UpdateIconSelection()
    {
        if (_iconPanel == null) return;
        foreach (var child in _iconPanel.Children)
        {
            if (child is not WpfButton b) continue;
            bool selected = (b.Tag as string) == ViewModel.Icon;
            b.BorderThickness = new Thickness(selected ? 2 : 1);
            b.BorderBrush     = selected ? WpfBrushes.CornflowerBlue : PopupBorderBrush();
        }
    }

    private static void SetFontSizeButtonContent(WpfButton button, string label, int delta)
    {
        button.Width = 24;
        var face = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Height = 22,
        };
        face.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = delta > 0 ? 16 : 12,
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        var chevron = new System.Windows.Shapes.Path
        {
            Data = System.Windows.Media.Geometry.Parse(delta > 0 ? "M0,3 L3,0 L6,3" : "M0,0 L3,3 L6,0"),
            StrokeThickness = 1.2,
            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
            Width = 7, Height = 5,
            Margin = new Thickness(1, 2, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        chevron.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,
            new System.Windows.Data.Binding(nameof(WpfButton.Foreground)) { Source = button });
        face.Children.Add(chevron);
        button.Content = face;
    }

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
            ("A", "FontLargerTooltip", false, 1),
            ("A", "FontSmallerTooltip", false, -1),
            ("T", "TitleLargerTooltip", true, 1),
            ("T", "TitleSmallerTooltip", true, -1),
        })
        {
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
            panel.Children.Add(button);
            if (delta > 0)
            {
                // Plain, non-interactive text distinguishes the current size from buttons.
                var size = new TextBlock
                {
                    MinWidth = 22,
                    Margin = new Thickness(2, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 12,
                    Foreground = PopupForegroundBrush(),
                    Opacity = 0.75,
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
