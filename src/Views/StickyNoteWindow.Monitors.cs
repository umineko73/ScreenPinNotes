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
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Views;

/// <summary>
/// モニタ構成が変わったときの置き場所。
///
/// 付箋の位置は「そのモニタの拡大率を基準にした論理ピクセル」で保存してある。
/// そのため復元も判定も物理ピクセルで行う ―― 論理ピクセルのまま Left/Top へ
/// 入れると、拡大率の違うモニタに居るあいだは別の場所を指してしまう。
///
/// 解像度が変わったり、付箋が居たモニタの接続が切れたりして本来の位置に
/// 出せなくなったときは、今映せるモニタへ寄せるだけにして、保存されている
/// 位置（ホーム）は書き換えない。構成が戻れば元の位置へ帰る。
/// </summary>
public partial class StickyNoteWindow
{
    /// <summary>
    /// 位置を書き戻してよいか（＝今のモニタ構成が保存時と同じか）と、
    /// 書き戻すときに添える基準。<see cref="NoteGeometryState"/> から都度呼ばれる。
    /// モニタ構成はキャッシュせず毎回 OS に聞く。取り外した直後に OS がウィンドウを
    /// 動かす瞬間の構成を取り違えると、その一時的な位置をホームとして保存してしまう。
    /// </summary>
    private NotePositionContext CurrentPositionContext()
    {
        var signature = MonitorLayout.Signature(MonitorLayout.Current());
        var home = ViewModel.Model.PositionLayout;
        var (dpiX, _) = GetDpi();
        // 未記録（この機能より前の付箋・作りたて）のときは、今の構成をホームとして引き受ける。
        return new NotePositionContext(home.Length == 0 || home == signature, signature, dpiX);
    }

    /// <summary>
    /// ユーザーが自分で置き直した先（ドラッグ・辺のリサイズ・位置の揃え直し）は、
    /// 今の構成での正しい置き場所として引き受ける。これが無いと、解像度を変えたまま
    /// 使い続ける人の付箋が「一時的に寄せた位置」のままになり、並べ直しても覚えてくれない。
    /// 元の構成でのホームは捨てずに残すので、その構成に戻れば元の位置へ帰る。
    /// </summary>
    private void AdoptCurrentLayoutAsHome()
        => NoteGeometryState.AdoptLayout(ViewModel.Model, MonitorLayout.Signature(MonitorLayout.Current()));

    /// <summary>
    /// <see cref="NoteGeometryState"/> を通さずモデルへ直接位置を書くところ用。
    /// 書いてよければ基準も更新して true を返す。
    /// </summary>
    private bool TryStampPositionContext()
    {
        var context = CurrentPositionContext();
        if (!context.CanStore) return false;
        NoteGeometryState.StampPositionContext(ViewModel.Model, context);
        return true;
    }

    /// <summary>
    /// 保存された位置へ戻し、それが今の構成で掴めないなら映せるモニタへ寄せる。
    /// 起動時・再表示時・モニタ構成変更時に呼ぶ。何度呼んでも同じ結果になる。
    /// </summary>
    internal void ReconcileScreenPlacement()
    {
        if (_isClosed) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return; // まだ一度も表示していない。OnSourceInitialized で通る。

        var monitors = MonitorLayout.Current();
        if (monitors.Count == 0) return;

        // この構成で置いた位置を覚えていれば、それが今のホーム。
        if (NoteGeometryState.RestoreLayoutHome(ViewModel.Model, MonitorLayout.Signature(monitors)))
            RequestSave();

        // 編集中の位置は保存対象ではない一時的なものなので、ホームへは引き戻さない
        // （編集を終えた時点で EnterViewMode がモデルの位置へ戻す）。
        var editing = _isEditMode && !ViewModel.IsFolded;
        SuppressWindowBoundsSave(() =>
        {
            if (!editing)
                MoveToHome(hwnd);
            if (!TryGetWindowRect(hwnd, out var rect)) return;
            if (MonitorLayout.IsReachable(rect, monitors)) return;
            var rescued = MonitorLayout.Rescue(rect, monitors);
            if (rescued != rect)
                MoveToPhysical(hwnd, rescued);
        });
        QueueFoldedGhostUpdate();
    }

    /// <summary>
    /// 保存された位置（ホーム）へ戻す。大きさには触らない。
    ///
    /// 拡大率を記録してある付箋は物理ピクセルで置く。保存値がどのモニタの基準かを
    /// 知っているので、どこに居ても同じ場所へ戻せる。
    ///
    /// 記録が無い付箋（この機能より前に保存されたもの）は Left/Top にそのまま入れて
    /// WPF に解決させる。WPF は初期配置で「いったんシステムの拡大率で置き、着いた
    /// モニタの拡大率で読み直す」ので、多くの構成では正しい場所に落ちる。こちらで
    /// プライマリの拡大率を当てはめると、その解決結果から動かしてしまう。
    /// 次にユーザーが動かした時点で基準が記録され、以降は物理ピクセルで扱う。
    /// </summary>
    private void MoveToHome(IntPtr hwnd)
    {
        var note = ViewModel.Model;
        var x = ViewModel.IsFolded ? note.FoldedX ?? note.X : note.X;
        var y = ViewModel.IsFolded ? note.FoldedY ?? note.Y : note.Y;
        if (note.PositionScale <= 0)
        {
            (Left, Top) = (x, y);
            return;
        }
        if (!TryGetWindowRect(hwnd, out var rect)) return;
        MoveToPhysical(hwnd, new Rect(Math.Round(x * note.PositionScale),
            Math.Round(y * note.PositionScale), rect.Width, rect.Height));
    }

    private static void MoveToPhysical(IntPtr hwnd, Rect target)
        => SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(target.X), (int)Math.Round(target.Y), 0, 0,
            SetWindowPosFlags.NoSize | SetWindowPosFlags.NoZOrder | SetWindowPosFlags.NoActivate);

    private static bool TryGetWindowRect(IntPtr hwnd, out Rect rect)
    {
        rect = default;
        if (!GetWindowRect(hwnd, out var r)) return false;
        rect = new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        return !rect.IsEmpty;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
}
