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

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using ScreenPinNotes.Services;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfImage = System.Windows.Controls.Image;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace ScreenPinNotes.Views;

/// <summary>
/// 開いた位置と畳んだ位置を分けた付箋を開いているあいだ、畳んだときの置き場所に
/// 出しておく灰色の影。「畳むとどこへ戻るか」を見えるようにする。
///
/// クリックでその場所へ畳み、ドラッグで畳んだときの位置だけを動かし、右クリックで
/// 位置をそろえ直せる。マウスを乗せると本体の付箋を強調して、どれの影かを示す。
///
/// フォーカスは取らない（ShowActivated=false と WM_MOUSEACTIVATE への MA_NOACTIVATE）。
/// タスクバーにも Alt+Tab にも出ないのは ShowInTaskbar=false のおかげ ―― WPF はそのとき
/// 見えない親ウィンドウの下に作るので、Alt+Tab の一覧からも外れる。拡張スタイルを
/// 拡張スタイルを SetWindowLongPtr で後から書き換える（WS_EX_TOOLWINDOW / WS_EX_NOACTIVATE）
/// 作りにしていた版は Microsoft Defender の振る舞い検知（Behavior:Win32/DefenseEvasion.A!ml）で
/// 隔離された。それが決め手かは分からないが、「ウィンドウを隠す」振る舞いに見えるので避ける。
/// 重なり順は本体が動くたびに本体のすぐ後ろへ置き直してもらう。
/// Owner にしないのは、所有されたウィンドウは常に所有者より前に出るため。
/// </summary>
internal sealed class FoldedPositionGhost : Window
{
    private readonly StickyNoteWindow _note;
    private readonly WpfRectangle _frame;
    private readonly WpfImage _icon;
    private readonly TextBlock _title;
    private readonly WpfMenuItem _resetItem;

    private bool _pressed;
    private bool _moved;
    private System.Drawing.Point _pressCursor;
    private RECT _pressRect;

    public FoldedPositionGhost(StickyNoteWindow note)
    {
        _note = note;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Opacity = 0.95;

        _frame = new WpfRectangle();
        _icon = new WpfImage { Opacity = 0.75, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        _title = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(_icon);
        row.Children.Add(_title);
        var root = new Grid { Cursor = System.Windows.Input.Cursors.Hand };
        root.Children.Add(_frame);
        root.Children.Add(row);
        Content = root;

        _resetItem = new WpfMenuItem();
        _resetItem.Click += (_, _) => _note.ResetPositionSeparationFromGhost();
        root.ContextMenu = new WpfContextMenu { Items = { _resetItem } };

        SourceInitialized += (_, _) =>
            (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(WndProc);
        MouseEnter += (_, _) => _note.SetFoldedGhostHighlight(true);
        MouseLeave += (_, _) => _note.SetFoldedGhostHighlight(false);
        MouseLeftButtonDown += OnPress;
        MouseMove += OnDrag;
        MouseLeftButtonUp += OnRelease;
        LostMouseCapture += (_, _) => Finish(commit: true, click: false);
    }

    /// <summary>見た目を付箋に合わせる。大きさは論理ピクセル（影が居るモニタの拡大率で描かれる）。</summary>
    public void Apply(string title, string? icon, double iconSize, double fontSize, WpfFontFamily font,
        double width, double height, double cornerRadius, bool dark)
    {
        Width = width;
        Height = height;
        var gray = dark ? WpfColor.FromRgb(0xC0, 0xC0, 0xC0) : WpfColor.FromRgb(0x68, 0x68, 0x68);
        _frame.Fill = Frozen(WpfColor.FromArgb(dark ? (byte)0x58 : (byte)0x50, gray.R, gray.G, gray.B));
        _frame.RadiusX = _frame.RadiusY = cornerRadius;
        _title.Foreground = Frozen(dark ? WpfColor.FromRgb(0xE0, 0xE0, 0xE0) : WpfColor.FromRgb(0x38, 0x38, 0x38));
        _title.Text = title;
        _title.FontSize = fontSize;
        _title.FontFamily = font;
        _title.MaxWidth = Math.Max(0, width - 16 - (string.IsNullOrEmpty(icon) ? 0 : iconSize + 6));
        _icon.Source = string.IsNullOrEmpty(icon) ? null : EmojiRenderer.Render(icon, monochrome: true);
        _icon.Width = _icon.Height = iconSize;
        _icon.Visibility = _icon.Source == null ? Visibility.Collapsed : Visibility.Visible;
        ToolTip = LocalizationService.T("FoldedGhostTooltip");
        _resetItem.Header = LocalizationService.T("ResetPositionSeparation");
        _resetItem.ToolTip = LocalizationService.T("ResetPositionSeparationTooltip");
        ControlTheme.Apply(this, dark);
    }

    /// <summary>左上を物理ピクセルで置く。まだ表示していなくても置ける。</summary>
    public void PlaceAt(int x, int y)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>本体のすぐ後ろへ置く。常に手前かどうかも本体に合わせる。</summary>
    public void PlaceBelow(IntPtr noteHwnd, bool topmost)
    {
        if (Topmost != topmost) Topmost = topmost;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || noteHwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, noteHwnd, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
    }

    /// <summary>ドラッグ中か。そのあいだは本体の都合で置き直さない。</summary>
    public bool IsDragging => _pressed;

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!GetWindowRect(hwnd, out _pressRect)) return;
        _pressed = true;
        _moved = false;
        _pressCursor = System.Windows.Forms.Cursor.Position;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnDrag(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_pressed) return;
        var cursor = System.Windows.Forms.Cursor.Position;
        int dx = cursor.X - _pressCursor.X, dy = cursor.Y - _pressCursor.Y;
        if (!_moved)
        {
            var threshold = App.Current.Settings.Interaction.ClickDragThresholdPx;
            if (Math.Abs(dx) < threshold && Math.Abs(dy) < threshold) return;
            _moved = true;
        }
        var target = SnapToEdges(new System.Drawing.Rectangle(_pressRect.Left + dx, _pressRect.Top + dy,
            _pressRect.Right - _pressRect.Left, _pressRect.Bottom - _pressRect.Top));
        SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
            target.X, target.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>
    /// 付箋本体のドラッグと同じく、作業領域の端と見えている付箋（自分の本体も含む）へ吸着させる。
    /// 影は拡大率の違うモニタをまたいで動くので、論理ピクセルではなく物理ピクセルで比べる。
    /// Alt を押している間は吸着しない。
    /// </summary>
    private System.Drawing.Point SnapToEdges(System.Drawing.Rectangle moving)
    {
        if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0) return moving.Location;
        var workArea = System.Windows.Forms.Screen.FromRectangle(moving).WorkingArea;
        var others = new List<System.Drawing.Rectangle>();
        foreach (var window in App.Current.NoteWindows)
        {
            if (!window.IsVisible) continue;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero && GetWindowRect(handle, out var r))
                others.Add(System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
        }
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var distance = (int)Math.Round(App.Current.Settings.Interaction.SnapDistance * scale);
        return EdgeSnap.Snap(moving, workArea, others, distance);
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Finish(commit: true, click: true);
    }

    private void Finish(bool commit, bool click)
    {
        if (!_pressed) return;
        _pressed = false;
        var moved = _moved;
        _moved = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (!commit) return;
        if (moved)
        {
            if (GetWindowRect(new WindowInteropHelper(this).Handle, out var r))
                _note.CommitFoldedGhostPosition(r.Left, r.Top);
        }
        else if (click)
        {
            _note.FoldFromGhost();
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // クリックしても前面へ出たりフォーカスを奪ったりしない。
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    private static WpfBrush Frozen(WpfColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    private const int VK_MENU = 0x12;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
