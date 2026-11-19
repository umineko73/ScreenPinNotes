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
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ScreenPinNotes.Services;
using WpfBrushes    = System.Windows.Media.Brushes;
using WpfButton     = System.Windows.Controls.Button;
using WpfColor      = System.Windows.Media.Color;
using WpfCursors    = System.Windows.Input.Cursors;
using WpfSolidBrush = System.Windows.Media.SolidColorBrush;

namespace ScreenPinNotes.Views;

public partial class StickyNoteWindow
{
    // ─── ピッカーの共通部品（アイコン・色） ────────────────────────
    //
    // 2つのピッカーを同じ見た目にそろえる。角の丸いカードに影を付け、
    // セルも角を丸めてマウスが乗ると薄く色を敷く。開く位置は付箋の上に寄せる。

    private const double PickerCell = 32;
    private const double PickerCellGap = 2;
    /// <summary>カードの影を描く余白。ポップアップの位置はこの分だけ戻す。</summary>
    private const double PickerShadowMargin = 12;
    /// <summary>ピッカーのカードを付箋の下端に重ねる高さ。</summary>
    private const double PickerOverlap = 24;

    private static ControlTemplate? _pickerCellTemplate;

    /// <summary>
    /// 角の丸いセル。マウスが乗ると文字色を薄く敷き、押すと少し濃くする。
    /// 文字色を使うのでライト・ダークのどちらでも同じ見え方になる。
    /// </summary>
    private static ControlTemplate PickerCellTemplate => _pickerCellTemplate ??= (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         TargetType="Button">
          <Grid>
            <Border CornerRadius="6" Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"/>
            <Border x:Name="Hover" CornerRadius="6" Background="{TemplateBinding Foreground}" Opacity="0"/>
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" Margin="{TemplateBinding Padding}"/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Hover" Property="Opacity" Value="0.08"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="Hover" Property="Opacity" Value="0.16"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.35"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);

    private WpfColor PickerAccent()
        => IsDarkTheme() ? WpfColor.FromRgb(0x47, 0x9E, 0xF5) : WpfColor.FromRgb(0x0F, 0x6C, 0xBD);

    private WpfSolidBrush PickerSubtleBrush(double opacity)
    {
        var brush = PopupForegroundBrush();
        brush.Opacity = opacity;
        return brush;
    }

    private WpfButton CreatePickerCell(object content, string? tag, string tooltip)
    {
        var button = new WpfButton
        {
            Template = PickerCellTemplate,
            Width = PickerCell, Height = PickerCell, Margin = new Thickness(PickerCellGap),
            Padding = new Thickness(0),
            Background = WpfBrushes.Transparent, BorderBrush = WpfBrushes.Transparent,
            BorderThickness = new Thickness(1.5),
            Foreground = PopupForegroundBrush(),
            Content = content, Tag = tag, ToolTip = tooltip,
            Cursor = WpfCursors.Hand, Focusable = false,
        };
        return button;
    }

    private static TextBlock GlyphText(string glyph, double size) => new()
    {
        Text = glyph, FontFamily = UiIcons.Font, FontSize = size,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        VerticalAlignment = System.Windows.VerticalAlignment.Center,
    };

    private FrameworkElement VerticalDivider() => new System.Windows.Shapes.Rectangle
    {
        Width = 1, Height = 20, Margin = new Thickness(4, 0, 4, 0),
        Fill = PickerSubtleBrush(0.15), VerticalAlignment = System.Windows.VerticalAlignment.Center,
    };


    /// <summary>
    /// 角の丸いカードに中身を載せたポップアップを作る。影を描くため周りに余白を取るが、
    /// 余白は当たり判定を持たず、外側を押したかどうかはカード（<see cref="PickerCard"/>）で判断する。
    /// </summary>
    private Popup CreatePickerPopup(UIElement content)
    {
        var card = new Border
        {
            Background = PopupBackgroundBrush(),
            BorderBrush = PickerSubtleBrush(IsDarkTheme() ? 0.18 : 0.12),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6), Child = content,
            Effect = new DropShadowEffect
            {
                BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = IsDarkTheme() ? 0.5 : 0.18,
                Color = Colors.Black,
            },
        };
        return new Popup
        {
            Child = new Border { Padding = new Thickness(PickerShadowMargin), Child = card },
            AllowsTransparency = true,
            Placement = PlacementMode.Relative, StaysOpen = false,
        };
    }

    /// <summary>ポップアップの中のカード本体。影の余白を含まない。</summary>
    private static FrameworkElement? PickerCard(Popup popup)
        => popup.Child is Border { Child: FrameworkElement card } ? card : popup.Child as FrameworkElement;

    private TextBlock PickerHeading(string text, bool first) => new()
    {
        Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(6, first ? 4 : 10, 0, 2),
        Foreground = PickerSubtleBrush(0.7),
    };

    /// <summary>
    /// ピッカーを付箋の下端にまたがるように開く。カードの上側 <see cref="PickerOverlap"/> だけを
    /// 付箋に重ね、残りは下へ出す。付箋の中身を大きく隠さず、それでいて付箋から
    /// 離れた場所には出ない。横は <paramref name="anchorOnScreen"/>（押したボタンの下端や
    /// マウスの位置、デバイスピクセル）の近くに出し、付箋の幅に収まるなら収める。
    /// 画面の下に入りきらないときは WPF がポップアップを上へずらす。押したボタンは
    /// <see cref="Popup.Tag"/> に覚え、そのボタンをもう一度押したときは閉じるだけにする。
    /// </summary>
    private void OpenPickerOverNote(Popup popup, System.Windows.Point anchorOnScreen, WpfButton? trigger)
    {
        const double Inset = 8;
        var anchor = RootBorder.PointFromScreen(anchorOnScreen);
        var card = PickerCard(popup);
        card?.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = card?.DesiredSize ?? default;
        var maxX = Math.Max(Inset, RootBorder.ActualWidth - size.Width - Inset);
        var x = Math.Clamp(anchor.X - 16, Inset, maxX);
        var y = Math.Max(Inset, RootBorder.ActualHeight - PickerOverlap);

        popup.Tag = trigger;
        popup.PlacementTarget = RootBorder;
        popup.Placement = PlacementMode.Relative;
        popup.HorizontalOffset = x - PickerShadowMargin;
        popup.VerticalOffset = y - PickerShadowMargin;
        popup.IsOpen = true;
    }

    /// <summary>ボタンの左下（画面座標）。ツールバーのボタンは別のポップアップの中にあるので、画面座標で受け渡す。</summary>
    private static System.Windows.Point BottomLeftOnScreen(UIElement element)
        => element.PointToScreen(new System.Windows.Point(0, element.RenderSize.Height));

    private static System.Windows.Point CursorOnScreen()
    {
        var cursor = System.Windows.Forms.Cursor.Position;   // デバイスピクセル
        return new System.Windows.Point(cursor.X, cursor.Y);
    }
}
