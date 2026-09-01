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
    // ─── ドラッグ & スナップ ─────────────────────────────────────
    //
    // タイトルバーは「動かさなければクリック、動かせばドラッグ」で
    // 意味が変わる。押した瞬間はまだどちらか分からないので、
    // MouseMove でしきい値を超えて初めてドラッグ確定として扱い、
    // 超えなければ MouseUp 時点で閉じた表示/開いた表示を切り替える。
    //   クリックまたはダブルクリック → 閉じた表示/開いた表示（設定で選択）
    //   ドラッグ → ウィンドウ移動（従来どおり）

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // タイトル編集欄をクリックしたときは、キャレット配置をそのまま
        // TextBox に任せる。ドラッグ開始・畳み判定もスキップし、
        // ウィンドウが動いたり編集欄が閉じたりしないようにする。
        if (_isEditMode && e.OriginalSource is DependencyObject src && IsDescendantOf(src, TitleEditBox))
            return;

        if (ShouldToggleViewOnMouseDown(e.ClickCount))
        {
            _isDragging = false;
            _dragMoved = false;
            _dragSeparatesFoldedPosition = false;
            e.Handled = true;
            ToggleFold();
            return;
        }

        _isDragging      = true;
        _dragMoved       = false;
        _dragSeparatesFoldedPosition = IsControlPressed();
        var (dpiX, dpiY) = GetDpi();
        var cur = System.Windows.Forms.Cursor.Position;
        _dragOffsetX     = cur.X / dpiX - Left;
        _dragOffsetY     = cur.Y / dpiY - Top;
        _dragStartCursor = cur;
        ((UIElement)sender).CaptureMouse();
    }

    private void TitleBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging) return;
        var cur = System.Windows.Forms.Cursor.Position;
        _dragSeparatesFoldedPosition |= IsControlPressed();

        if (!_dragMoved)
        {
            if (Math.Abs(cur.X - _dragStartCursor.X) < Settings.Interaction.ClickDragThresholdPx &&
                Math.Abs(cur.Y - _dragStartCursor.Y) < Settings.Interaction.ClickDragThresholdPx)
                return; // まだクリックの範囲内。ドラッグ確定まで動かさない
            _dragMoved = true;
            _titlePreviewTimer.Stop();
            TitlePreviewPopup.IsOpen = false;
            ShowDragMoveOverlay();
        }

        var (dpiX, dpiY) = GetDpi();
        Left = cur.X / dpiX - _dragOffsetX;
        Top  = cur.Y / dpiY - _dragOffsetY;
        SnapToAll();
        ShowDragMoveOverlay();
    }

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();

        if (_dragMoved)
        {
            SaveCurrentPositionToModel();
            RequestSave();
            _dragSeparatesFoldedPosition = false;
            return;
        }

        _dragSeparatesFoldedPosition = false;
        if (ShouldToggleViewOnMouseUp(e.ClickCount))
            ToggleFold();
    }

    private bool ShouldToggleViewOnMouseDown(int clickCount)
        => Settings.DoubleClickToToggleView && clickCount >= 2;

    private bool ShouldToggleViewOnMouseUp(int clickCount)
        => !Settings.DoubleClickToToggleView && clickCount == 1;

    private void TitleBar_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            return;

        e.Handled = true;
        var delta = e.Delta > 0 ? 1 : -1;
        SetTitleFontSize(ViewModel.TitleFontSize + delta);
    }

    private void TitleBar_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        UpdateTitleBarButtonsVisibility();
        ScheduleTitlePreview();
    }

    private void TitleBar_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        UpdateTitleBarButtonsVisibility();
        _titlePreviewTimer.Stop();
        TitlePreviewPopup.IsOpen = false;
    }

    private void ScheduleTitlePreview()
    {
        _titlePreviewTimer.Stop();
        TitlePreviewPopup.IsOpen = false;
        if (Settings.ShowTitlePreviewTooltip &&
            ViewModel.IsFolded && !_isEditMode &&
            !string.IsNullOrWhiteSpace(ViewModel.Content) && TitleBar.IsMouseOver)
            _titlePreviewTimer.Start();
    }

    private void UpdateTitlePreviewVisibility()
    {
        TitlePreviewPopup.IsOpen = Settings.ShowTitlePreviewTooltip &&
            ViewModel.IsFolded &&
            !_isEditMode &&
            !string.IsNullOrWhiteSpace(ViewModel.Content) &&
            TitleBar.IsMouseOver;
    }

    private void RootBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ViewModel.SetHovered(true);
        ShowEditToolbar();
    }

    private void RootBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ViewModel.SetHovered(false);
        ScheduleHideEditToolbar();
    }

    private void EditToolbar_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        => ShowEditToolbar();

    private void EditToolbar_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        => ScheduleHideEditToolbar();

    private (double dpiX, double dpiY) GetDpi()
    {
        var src = PresentationSource.FromVisual(this);
        return (
            src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0,
            src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0
        );
    }

    // ─── スナップ ────────────────────────────────────────────────

    private void SnapToAll()
    {
        if (IsAltPressed()) return;

        var hwnd   = new WindowInteropHelper(this).Handle;
        var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
        var wa     = screen.WorkingArea;

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null) return;
        double dpiX = source.CompositionTarget.TransformToDevice.M11;
        double dpiY = source.CompositionTarget.TransformToDevice.M22;

        double waL = wa.Left / dpiX, waT = wa.Top    / dpiY;
        double waR = wa.Right / dpiX, waB = wa.Bottom / dpiY;

        double? bestLeft = null, bestTop = null;
        double  minLD = Settings.Interaction.SnapDistance, minTD = Settings.Interaction.SnapDistance;

        TrySnap(Left,         waL, ref bestLeft, ref minLD, waL);
        TrySnap(Left + Width, waR, ref bestLeft, ref minLD, waR - Width);
        TrySnap(Top,          waT, ref bestTop,  ref minTD, waT);
        TrySnap(Top + Height, waB, ref bestTop,  ref minTD, waB - Height);

        foreach (var other in App.Current.NoteWindows)
        {
            if (other == this) continue;
            double oL = other.Left, oT = other.Top;
            double oR = oL + other.Width, oB = oT + other.Height;

            TrySnap(Left,         oL, ref bestLeft, ref minLD, oL);
            TrySnap(Left,         oR, ref bestLeft, ref minLD, oR);
            TrySnap(Left + Width, oL, ref bestLeft, ref minLD, oL - Width);
            TrySnap(Left + Width, oR, ref bestLeft, ref minLD, oR - Width);

            TrySnap(Top,          oT, ref bestTop, ref minTD, oT);
            TrySnap(Top,          oB, ref bestTop, ref minTD, oB);
            TrySnap(Top + Height, oT, ref bestTop, ref minTD, oT - Height);
            TrySnap(Top + Height, oB, ref bestTop, ref minTD, oB - Height);
        }

        if (bestLeft.HasValue) Left = bestLeft.Value;
        if (bestTop.HasValue)  Top  = bestTop.Value;
    }

    private static void TrySnap(double myEdge, double target,
        ref double? best, ref double bestDist, double snapTo)
    {
        var d = Math.Abs(myEdge - target);
        if (d < bestDist) { bestDist = d; best = snapTo; }
    }

    // ─── リサイズ中のスナップ（WM_SIZING フック） ────────────────

    private const int WM_SIZING          = 0x0214;
    private const int WM_NCLBUTTONDBLCLK = 0x00A3;
    private const int HTTOP              = 12;
    private const int HTBOTTOM           = 15;
    private const int WMSZ_LEFT          = 1;
    private const int WMSZ_RIGHT         = 2;
    private const int WMSZ_TOP           = 3;
    private const int WMSZ_TOPLEFT       = 4;
    private const int WMSZ_TOPRIGHT      = 5;
    private const int WMSZ_BOTTOM        = 6;
    private const int WMSZ_BOTTOMLEFT    = 7;
    private const int WMSZ_BOTTOMRIGHT   = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource src)
            src.AddHook(WndProc);
        ApplyRoundedCorners();
    }

    // ─── 角丸（Windows 11 の DWM に任せる） ──────────────────────
    //
    // AllowsTransparency + Border.CornerRadius でも実現できるが、
    // WindowChrome のリサイズ処理と相性が悪く描画も重くなる。
    // DWM に角を落としてもらえば OS 側の合成で切り抜かれる。

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUNDSMALL              = 3;   // 小さめの角丸

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void ApplyRoundedCorners()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int preference = DWMWCP_ROUNDSMALL;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference, sizeof(int));
        }
        catch
        {
            // Windows 10 以前では未対応。角丸にならないだけで動作に支障はない
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 上下のリサイズ枠をダブルクリックすると Windows が縦方向に最大化する。
        // 付箋では意図しない動きなので握りつぶす（ドラッグでのリサイズは残る）。
        if (msg == WM_NCLBUTTONDBLCLK)
        {
            int hit = wParam.ToInt32();
            if (hit == HTTOP || hit == HTBOTTOM)
            {
                handled = true;
                return IntPtr.Zero;
            }
        }

        // 閉じた表示でも幅の変更（左右辺）だけは許可している。上下辺は
        // SetResizeEnabled が常に 0 にしているため、閉じた表示中に届く
        // WM_SIZING は自然と左右辺のみになる。
        if (msg != WM_SIZING) return IntPtr.Zero;

        var rect = Marshal.PtrToStructure<RECT>(lParam);
        if (SnapSizingRect(ref rect, wParam.ToInt32()))
        {
            Marshal.StructureToPtr(rect, lParam, false);
            handled = true;
            return (IntPtr)1;
        }
        return IntPtr.Zero;
    }

    // ドラッグ中の矩形（デバイスpx）を論理pxに直してスナップ先を探し、書き戻す
    private bool SnapSizingRect(ref RECT r, int edge)
    {
        if (IsAltPressed()) return false;

        var (dpiX, dpiY) = GetDpi();

        double left   = r.Left   / dpiX;
        double top    = r.Top    / dpiY;
        double right  = r.Right  / dpiX;
        double bottom = r.Bottom / dpiY;

        bool movingLeft   = edge is WMSZ_LEFT   or WMSZ_TOPLEFT    or WMSZ_BOTTOMLEFT;
        bool movingRight  = edge is WMSZ_RIGHT  or WMSZ_TOPRIGHT   or WMSZ_BOTTOMRIGHT;
        bool movingTop    = edge is WMSZ_TOP    or WMSZ_TOPLEFT    or WMSZ_TOPRIGHT;
        bool movingBottom = edge is WMSZ_BOTTOM or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT;

        var xTargets = new List<double>();
        var yTargets = new List<double>();

        // 画面の作業領域
        var wa = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        double waL = wa.Left / dpiX, waR = wa.Right  / dpiX;
        double waT = wa.Top  / dpiY, waB = wa.Bottom / dpiY;

        if (movingLeft)   xTargets.Add(waL);
        if (movingRight)  xTargets.Add(waR);
        if (movingTop)    yTargets.Add(waT);
        if (movingBottom) yTargets.Add(waB);

        foreach (var other in App.Current.NoteWindows)
        {
            if (other == this || !other.IsVisible) continue;

            double oL = other.Left, oT = other.Top;
            double oR = oL + other.ActualWidth, oB = oT + other.ActualHeight;

            // 辺を他の付箋の辺に合わせる
            if (movingLeft)   { xTargets.Add(oL); xTargets.Add(oR); }
            if (movingRight)  { xTargets.Add(oL); xTargets.Add(oR); }
            if (movingTop)    { yTargets.Add(oT); yTargets.Add(oB); }
            if (movingBottom) { yTargets.Add(oT); yTargets.Add(oB); }

            // 幅・高さを他の付箋と揃える（サイズスナップ）
            if (other.ViewModel.IsFolded) continue;
            if (movingRight)  xTargets.Add(left   + other.ActualWidth);
            if (movingLeft)   xTargets.Add(right  - other.ActualWidth);
            if (movingBottom) yTargets.Add(top    + other.ActualHeight);
            if (movingTop)    yTargets.Add(bottom - other.ActualHeight);
        }

        bool changed = false;

        if (movingLeft   && TryNearest(left,   xTargets, out var nl)) { left   = nl; changed = true; }
        if (movingRight  && TryNearest(right,  xTargets, out var nr)) { right  = nr; changed = true; }
        if (movingTop    && TryNearest(top,    yTargets, out var nt)) { top    = nt; changed = true; }
        if (movingBottom && TryNearest(bottom, yTargets, out var nb)) { bottom = nb; changed = true; }

        if (!changed) return false;

        // 最小サイズを割り込むスナップは破棄する
        if (right - left < MinWidth || bottom - top < MinHeight) return false;

        r.Left   = (int)Math.Round(left   * dpiX);
        r.Right  = (int)Math.Round(right  * dpiX);
        r.Top    = (int)Math.Round(top    * dpiY);
        r.Bottom = (int)Math.Round(bottom * dpiY);
        return true;
    }

    private const int VK_MENU = 0x12;
    private const int VK_CONTROL = 0x11;
    private static readonly IntPtr HwndTop = new(0);
    private static readonly IntPtr HwndBottom = new(1);

    [Flags]
    private enum SetWindowPosFlags : uint
    {
        NoSize = 0x0001,
        NoMove = 0x0002,
        NoActivate = 0x0010,
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        SetWindowPosFlags uFlags);

    private static bool IsAltPressed()
        => (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

    private static bool IsControlPressed()
        => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

    private static bool TryNearest(double value, List<double> targets, out double snapped)
    {
        snapped = value;
        double best = App.Current.Settings.Interaction.SnapDistance;
        bool found = false;
        foreach (var t in targets)
        {
            var d = Math.Abs(value - t);
            if (d < best) { best = d; snapped = t; found = true; }
        }
        return found;
    }

}
