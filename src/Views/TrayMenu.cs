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

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenPinNotes.Services;
using WpfColor = System.Windows.Media.Color;
using WpfImage = System.Windows.Controls.Image;
using WpfSolidBrush = System.Windows.Media.SolidColorBrush;

namespace ScreenPinNotes.Views;

/// <summary>トレイメニューの1項目。Glyph は Segoe Fluent Icons の字、Children があれば子メニューになる。</summary>
public sealed record TrayMenuEntry(
    string Header,
    Action? Run = null,
    string? Glyph = null,
    ImageSource? Image = null,
    bool Enabled = true,
    IReadOnlyList<TrayMenuEntry>? Children = null)
{
    public static readonly TrayMenuEntry Separator = new("-");
    public bool IsSeparator => ReferenceEquals(this, Separator);
}

/// <summary>
/// タスクトレイのメニュー。WinForms の ContextMenuStrip はアプリのテーマにもダークにも従わず、
/// 付箋の右クリックメニューと見た目が違ったので、付箋と同じ WPF のメニュー
/// （<c>NoteMenuItemStyle</c>）で作る。開くたびに作り直すので、非表示の付箋の一覧は常に最新。
/// </summary>
public static class TrayMenu
{
    private static ResourceDictionary? _styles;

    private static ResourceDictionary Styles => _styles ??= new ResourceDictionary
    {
        Source = new Uri("pack://application:,,,/ScreenPinNotes;component/Resources/Styles.xaml"),
    };

    /// <summary>メニューを組み立てる（開かない）。テストからも呼ぶ。</summary>
    public static ContextMenu Build(IEnumerable<TrayMenuEntry> entries, bool dark)
    {
        var background = new WpfSolidBrush(dark ? WpfColor.FromRgb(31, 41, 55) : Colors.White);
        var foreground = new WpfSolidBrush(dark ? WpfColor.FromRgb(245, 245, 245) : Colors.Black);
        var border = new WpfSolidBrush(dark ? WpfColor.FromRgb(75, 85, 99) : WpfColor.FromRgb(211, 211, 211));
        var highlight = foreground.Color;
        highlight.A = 32;

        var menu = new ContextMenu
        {
            Template = MenuTemplate,
            Background = background,
            Foreground = foreground,
            BorderBrush = border,
            FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI"),
            FontSize = 13,
            Placement = PlacementMode.MousePoint,
            HasDropShadow = false,
        };
        menu.Resources.MergedDictionaries.Add(Styles);
        menu.Resources[typeof(MenuItem)] = Styles["NoteMenuItemStyle"];
        menu.Resources[typeof(Separator)] = Styles["NoteMenuSeparatorStyle"];
        menu.Resources["NoteMenuBackground"] = background;
        menu.Resources["NoteMenuForeground"] = foreground;
        menu.Resources["NoteMenuBorder"] = border;
        menu.Resources["NoteMenuHighlight"] = new WpfSolidBrush(highlight);

        foreach (var entry in entries)
            menu.Items.Add(BuildItem(entry));
        return menu;
    }

    private static object BuildItem(TrayMenuEntry entry)
    {
        if (entry.IsSeparator) return new Separator();
        var item = new MenuItem
        {
            Header = entry.Header,
            IsEnabled = entry.Enabled,
            Padding = new Thickness(0, 2, 0, 2),
            MinWidth = 220,
        };
        if (entry.Image != null)
            item.Icon = new WpfImage { Source = entry.Image, Width = 16, Height = 16 };
        else if (entry.Glyph != null)
            item.Icon = new TextBlock { Text = entry.Glyph, FontFamily = UiIcons.Font, FontSize = 14 };
        if (entry.Children != null)
        {
            foreach (var child in entry.Children)
                item.Items.Add(BuildItem(child));
        }
        else if (entry.Run is { } run)
        {
            item.Click += (_, _) => run();
        }
        return item;
    }

    /// <summary>マウスの位置にメニューを開く。</summary>
    public static void Show(IEnumerable<TrayMenuEntry> entries, bool dark)
    {
        var menu = Build(entries, dark);
        // トレイから開いたメニューは、アプリが前面にいないと外側のクリックで閉じない。
        // メニュー自身のウィンドウを前面にして、外側を押せば閉じるようにする。
        menu.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(menu) is HwndSource source)
                SetForegroundWindow(source.Handle);
            menu.Focus();
        };
        menu.IsOpen = true;
    }

    private static ControlTemplate? _menuTemplate;

    private static ControlTemplate MenuTemplate => _menuTemplate ??= (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ContextMenu">
          <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="1" Padding="4" CornerRadius="6">
            <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/>
          </Border>
        </ControlTemplate>
        """);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
