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
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ScreenPinNotes.Services;
using WpfButton = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;
using WpfColor = System.Windows.Media.Color;
using WpfSolidBrush = System.Windows.Media.SolidColorBrush;

namespace ScreenPinNotes.Views;

/// <summary>
/// 画面の下に少しの間だけ出る通知。「付箋を削除しました［元に戻す］」のように、
/// 済んだ操作を取り消す手段を出す。フォーカスは奪わず、時間が来るか × で消える。
/// 次の通知が来たら前のものは閉じる（取り消せるのは直前の操作だけ）。
/// </summary>
public sealed class UndoToast : Window
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(8);

    private static UndoToast? _current;

    /// <summary>いま出ている通知。無ければ null。</summary>
    internal static UndoToast? Current => _current;
    private readonly DispatcherTimer _timer = new() { Interval = Duration };

    internal WpfButton ActionButton { get; }

    private UndoToast(string message, string actionLabel, Action action, bool dark)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI");
        FontSize = 13;
        ControlTheme.Apply(this, dark, dialog: true);

        var foreground = new WpfSolidBrush(dark ? WpfColor.FromRgb(0xEE, 0xEE, 0xEE) : WpfColor.FromRgb(0x1B, 0x1B, 0x1B));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = message, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 360, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 16, 0),
        });
        ActionButton = new WpfButton
        {
            Content = actionLabel, Height = 30, MinWidth = 88, Padding = new Thickness(12, 0, 12, 0),
        };
        ActionButton.SetResourceReference(StyleProperty, "AccentButton");
        ActionButton.Click += (_, _) =>
        {
            Close();
            action();
        };
        row.Children.Add(ActionButton);
        var close = new WpfButton
        {
            Content = new TextBlock { Text = UiIcons.Cancel, FontFamily = UiIcons.Font, FontSize = 10 },
            Width = 30, Height = 30, Margin = new Thickness(6, 0, 0, 0),
            Background = Brushes.Transparent, ToolTip = LocalizationService.T("Close"),
        };
        close.Click += (_, _) => Close();
        row.Children.Add(close);

        Content = new Border
        {
            Margin = new Thickness(16),   // 影の分の余白
            Padding = new Thickness(16, 10, 10, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = new WpfSolidBrush(dark ? WpfColor.FromRgb(0x2B, 0x2B, 0x2B) : Colors.White),
            BorderBrush = new WpfSolidBrush(dark ? WpfColor.FromRgb(0x3A, 0x3A, 0x3A) : WpfColor.FromRgb(0xE0, 0xE0, 0xE0)),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = dark ? 0.5 : 0.18 },
            Child = row,
        };

        _timer.Tick += (_, _) => Close();
        Loaded += (_, _) => PlaceAtBottomOfWorkArea();
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => { _timer.Stop(); _timer.Start(); };
        Closed += (_, _) =>
        {
            _timer.Stop();
            if (ReferenceEquals(_current, this)) _current = null;
        };
    }

    /// <summary>通知を出す。前の通知は閉じる。</summary>
    public static UndoToast Show(string message, string actionLabel, Action action, bool dark)
    {
        // 前の通知は、それを作ったスレッドで閉じる（テストではスレッドが分かれることがある）。
        if (_current is { } previous)
        {
            if (previous.Dispatcher.CheckAccess()) previous.Close();
            else previous.Dispatcher.BeginInvoke(previous.Close);
        }
        var toast = new UndoToast(message, actionLabel, action, dark);
        _current = toast;
        toast.Show();
        toast._timer.Start();
        return toast;
    }

    /// <summary>マウスのある画面の作業領域の下中央（タスクバーの上）。</summary>
    private void PlaceAtBottomOfWorkArea()
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;
        var source = PresentationSource.FromVisual(this);
        var toDip = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = toDip.Transform(new System.Windows.Point(screen.Left, screen.Top));
        var bottomRight = toDip.Transform(new System.Windows.Point(screen.Right, screen.Bottom));
        Left = topLeft.X + (bottomRight.X - topLeft.X - ActualWidth) / 2;
        Top = bottomRight.Y - ActualHeight - 8;
    }
}
