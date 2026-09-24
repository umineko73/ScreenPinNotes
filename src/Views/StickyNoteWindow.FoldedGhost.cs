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
using System.Windows.Interop;
using System.Windows.Threading;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Views;

/// <summary>
/// 開いた位置と畳んだ位置を分けた付箋の「畳んだときの置き場所」を示す影
/// （<see cref="FoldedPositionGhost"/>）の出し入れ。
///
/// 影を出すのは、位置を分けていて、今は開いていて、表示されているあいだだけ。
/// 畳むアニメーションの途中は残し、畳み終えた時点で消す（付箋が影の場所へ着く）。
/// 状態の変化はまとめて1回にし（<see cref="QueueFoldedGhostUpdate"/>）、
/// 途中の中途半端な状態で出したり消したりしない。
/// </summary>
public partial class StickyNoteWindow
{
    private FoldedPositionGhost? _foldedGhost;
    private DispatcherOperation? _foldedGhostUpdate;

    /// <summary>影の左上（物理px）。出ていなければ null（テスト用）。</summary>
    public System.Drawing.Point? FoldedGhostPhysicalLocation
    {
        get
        {
            if (_foldedGhost is not { IsVisible: true } ghost) return null;
            var hwnd = new WindowInteropHelper(ghost).Handle;
            return TryGetWindowRect(hwnd, out var rect) ? new System.Drawing.Point((int)rect.X, (int)rect.Y) : null;
        }
    }

    private void QueueFoldedGhostUpdate()
    {
        if (_isClosed || _foldedGhostUpdate?.Status == DispatcherOperationStatus.Pending) return;
        _foldedGhostUpdate = Dispatcher.BeginInvoke(UpdateFoldedGhost, DispatcherPriority.Normal);
    }

    private bool ShouldShowFoldedGhost()
        => !_isClosed && IsVisible && WindowState == WindowState.Normal &&
           Settings.ShowFoldedPositionGhost &&
           ViewModel.IsPositionSeparated &&
           (!ViewModel.IsFolded || _isFoldAnimationRunning) &&
           ViewModel.Model.FoldedX.HasValue && ViewModel.Model.FoldedY.HasValue;

    private void UpdateFoldedGhost()
    {
        if (_foldedGhost?.IsDragging == true) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !ShouldShowFoldedGhost() || !TryGetWindowRect(hwnd, out var noteRect))
        {
            HideFoldedGhost();
            return;
        }

        var note = ViewModel.Model;
        var (dpiX, _) = GetDpi();
        var scale = note.PositionScale > 0 ? note.PositionScale : dpiX;
        var width = MeasureFoldedWidth();
        var height = FoldedHeight;
        var rect = new Rect(Math.Round(note.FoldedX!.Value * scale), Math.Round(note.FoldedY!.Value * scale),
            Math.Round(width * scale), Math.Round(height * scale));
        // 畳んだ位置が今の構成で掴めない場所なら、畳んだときと同じく映せるモニタへ寄せて見せる。
        var monitors = MonitorLayout.Current();
        if (!MonitorLayout.IsReachable(rect, monitors))
            rect = MonitorLayout.Rescue(rect, monitors);
        // 開いた付箋の左上と同じ場所なら、影は付箋の真後ろに隠れるだけなので出さない。
        if (Math.Abs(rect.X - noteRect.X) <= 1 && Math.Abs(rect.Y - noteRect.Y) <= 1)
        {
            HideFoldedGhost();
            return;
        }

        _foldedGhost ??= new FoldedPositionGhost(this);
        _foldedGhost.Apply(ViewModel.DisplayTitle, ViewModel.Icon, ViewModel.TitleIconSize, ViewModel.TitleFontSize,
            TitleText.FontFamily, width, height, Settings.Layout.NoteCornerRadius, IsDarkTheme());
        _foldedGhost.PlaceAt((int)rect.X, (int)rect.Y);
        if (!_foldedGhost.IsVisible) _foldedGhost.Show();
        _foldedGhost.PlaceBelow(hwnd, Topmost);
    }

    private void HideFoldedGhost()
    {
        if (_foldedGhost is { IsVisible: true } ghost) ghost.Hide();
        SetFoldedGhostHighlight(false);
    }

    private void CloseFoldedGhost()
    {
        _foldedGhostUpdate?.Abort();
        _foldedGhost?.Close();
        _foldedGhost = null;
    }

    /// <summary>付箋の重なり順が変わったら、影をそのすぐ後ろへ付いて行かせる。</summary>
    private void KeepFoldedGhostBehind(IntPtr hwnd, IntPtr lParam)
    {
        if (_foldedGhost is not { IsVisible: true } ghost) return;
        var flags = Marshal.PtrToStructure<WINDOWPOS>(lParam).flags;
        if ((flags & (uint)SetWindowPosFlags.NoZOrder) != 0) return;
        ghost.PlaceBelow(hwnd, Topmost);
    }

    /// <summary>影にマウスが乗っているあいだ、どの付箋の影かが分かるよう本体を縁取る。</summary>
    public void SetFoldedGhostHighlight(bool on)
        => FoldedGhostHighlight.Opacity = on ? 1 : 0;

    /// <summary>影をクリックした。その場所へ畳む。</summary>
    public void FoldFromGhost()
    {
        SetFoldedGhostHighlight(false);
        if (!ViewModel.IsFolded) ToggleFold();
    }

    /// <summary>影の右クリックメニューから、位置のそろえ直し。</summary>
    public void ResetPositionSeparationFromGhost()
    {
        SetFoldedGhostHighlight(false);
        ResetPositionSeparation();
    }

    /// <summary>
    /// 影をドラッグして置き直した（物理px）。畳んだときの位置だけを書き換え、
    /// 開いた付箋はそのまま。自分で置いた位置なので、今のモニタ構成の位置として引き受ける。
    /// </summary>
    public void CommitFoldedGhostPosition(int physicalX, int physicalY)
    {
        if (_isClosed) return;
        AdoptCurrentLayoutAsHome();
        // 引き受けた構成の基準で開いた位置もそろえておく（編集中の位置は一時的なので除く）。
        if (!_isEditMode) StoreCurrentPositionInModel();
        _geometry.StoreFoldedPhysicalPosition(physicalX, physicalY);
        RequestSave();
        QueueFoldedGhostUpdate();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }
}
