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
using SkiaSharp;
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

    private Popup BuildColorPopup()
    {
        const double Swatch  = 28;
        const double Gap     = 3;
        const int    Columns = 6;

        // 端数で列が折り返さないよう僅かに余裕を持たせる
        var panel = new WrapPanel { Width = Columns * (Swatch + Gap * 2) + 2 };
        _colorPanel = panel;

        foreach (var (key, preset) in StickyNoteViewModel.ColorPresets)
        {
            var header = new WpfSolidBrush((WpfColor)WpfColorConverter.ConvertFromString(preset.Header));
            var btn = new WpfButton
            {
                Width = Swatch, Height = Swatch, Margin = new Thickness(Gap),
                Padding         = new Thickness(0),
                Background      = new WpfSolidBrush((WpfColor)WpfColorConverter.ConvertFromString(preset.Bg)),
                BorderThickness = new Thickness(1),
                BorderBrush     = header,   // 枠線でヘッダー色も判るようにする
                Foreground      = header,   // 選択中のチェック記号の色
                FontWeight      = FontWeights.Bold,
                FontSize        = 13,
                Tag             = key,
                ToolTip         = key,
                Cursor          = WpfCursors.Hand,
            };
            btn.Click += (s, _) =>
            {
                if (s is WpfButton b && b.Tag is string k)
                {
                    ViewModel.ColorKey = k;
                    if (_colorPopup != null) _colorPopup.IsOpen = false;
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
                BorderThickness = new Thickness(1), Padding = new Thickness(4), Child = panel,
            },
            Placement = PlacementMode.Bottom, StaysOpen = false,
        };
    }

    // ─── アイコンピッカー ────────────────────────────────────────

    private Popup BuildIconPopup()
    {
        const double Cell    = 28;
        const double Gap     = 3;
        const int    Columns = 8;

        var panel = new WrapPanel { Width = Columns * (Cell + Gap * 2) + 2 };
        _iconPanel = panel;

        foreach (var icon in IconList)
        {
            bool isNone = icon.Length == 0;
            var btn = new WpfButton
            {
                Width = Cell, Height = Cell, Margin = new Thickness(Gap),
                Padding    = new Thickness(0),
                Content    = isNone
                    ? new TextBlock
                    {
                        Text = "✕", FontSize = 11, Foreground = WpfBrushes.Gray,
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
                BorderThickness = new Thickness(1), Padding = new Thickness(4), Child = panel,
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

    // ─── フォントピッカー ────────────────────────────────────────

    private Popup BuildFontPopup()
    {
        var listBox = new WpfListBox
        {
            Width = 180,
            MaxHeight = 260,
            BorderThickness = new Thickness(0),
            Background = PopupBackgroundBrush(),
            Foreground = ViewModel.TextForeground,
        };
        foreach (var font in FontList)
        {
            listBox.Items.Add(new ListBoxItem
            {
                Content = font,
                FontFamily = new WpfFontFamily(font),
                FontSize = 13,
                Tag = font,
            });
        }
        listBox.SelectionChanged += (_, _) =>
        {
            if (listBox.SelectedItem is ListBoxItem item && item.Tag is string font)
            {
                ViewModel.FontFamily = font;
                if (_fontPopup != null) _fontPopup.IsOpen = false;
                RequestSave();
            }
        };
        return new Popup
        {
            Child = new Border
            {
                Background = PopupBackgroundBrush(), BorderBrush = PopupBorderBrush(),
                BorderThickness = new Thickness(1), Child = listBox,
            },
            Placement = PlacementMode.Bottom, StaysOpen = false,
        };
    }
}
