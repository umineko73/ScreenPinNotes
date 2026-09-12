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

namespace ScreenPinNotes.Services;

/// <summary>モニタ1台ぶん。矩形は物理ピクセル（仮想スクリーン座標）。</summary>
public readonly record struct MonitorInfo(Rect WorkArea, double Scale, bool IsPrimary);

/// <summary>
/// 付箋が今のモニタ構成で掴める位置にあるかを判定し、外れているときの
/// 移動先を決める。座標はすべて物理ピクセルで扱う ―― 論理ピクセルは
/// 「そのウィンドウが今いるモニタの拡大率」が基準なので、拡大率の違う
/// モニタをまたぐ判定には使えない。
///
/// モニタ構成の取得に WinForms の Screen を使わないのは、あちらが
/// 構成変更の通知でキャッシュを捨てる作りで、モニタを取り外した直後に
/// OS がウィンドウを動かす瞬間には古い一覧を返しうるため。
/// その瞬間の構成を取り違えると「一時的な位置」を本来の位置として
/// 保存してしまうので、ここでは常に OS へ問い合わせる。
/// </summary>
public static class MonitorLayout
{
    /// <summary>掴める判定に必要な、見えている幅（物理px）。付箋がこれより細ければ全幅。</summary>
    public const double MinVisibleWidth = 96;
    /// <summary>タイトルバーとして掴める上端の帯の高さ（物理px）。</summary>
    public const double GrabHeight = 28;
    /// <summary>その帯のうち見えていなければならない高さ（物理px）。</summary>
    public const double MinVisibleGrabHeight = 4;

    /// <summary>今のモニタ構成。取得できなければ空（＝判定せず現状維持）。</summary>
    public static IReadOnlyList<MonitorInfo> Current()
    {
        var monitors = new List<MonitorInfo>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr hdc, ref RECT clip, IntPtr data) =>
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfoW(handle, ref info))
                    monitors.Add(new MonitorInfo(ToRect(info.rcWork), GetScale(handle),
                        (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return [];
        }
        return monitors;
    }

    /// <summary>
    /// 構成を表す文字列。保存した位置がどの構成のものかを覚えておき、
    /// 違う構成では位置を書き換えないための鍵にする。
    /// </summary>
    public static string Signature(IReadOnlyList<MonitorInfo> monitors)
        => string.Join(";", monitors
            .Select(m => string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{m.WorkArea.X},{m.WorkArea.Y},{m.WorkArea.Width}x{m.WorkArea.Height}@{m.Scale:0.###}"))
            .OrderBy(text => text, StringComparer.Ordinal));

    /// <summary>プライマリの拡大率。拡大率を記録していない付箋の位置を物理pxへ戻すのに使う。</summary>
    public static double PrimaryScale(IReadOnlyList<MonitorInfo> monitors)
    {
        foreach (var monitor in monitors)
            if (monitor.IsPrimary) return monitor.Scale > 0 ? monitor.Scale : 1;
        return 1;
    }

    /// <summary>
    /// タイトルバーを掴んで動かせるだけ見えているか。下や右にはみ出しているだけの
    /// 付箋は掴めるので動かさない ―― ユーザーが自分で置いた位置を勝手に直さない。
    /// </summary>
    public static bool IsReachable(Rect note, IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) return true;
        var grab = new Rect(note.X, note.Y, note.Width, Math.Min(note.Height, GrabHeight));
        foreach (var monitor in monitors)
        {
            var visible = Rect.Intersect(grab, monitor.WorkArea);
            if (visible.IsEmpty) continue;
            if (visible.Width >= Math.Min(note.Width, MinVisibleWidth) &&
                visible.Height >= Math.Min(grab.Height, MinVisibleGrabHeight))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 今映せるモニタの作業領域へ寄せた矩形。大きさは変えない（縮めてしまうと
    /// 構成が戻ったときに元の大きさが分からなくなる）。
    /// </summary>
    public static Rect Rescue(Rect note, IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) return note;
        var area = ChooseTarget(note, monitors).WorkArea;
        var x = note.Width >= area.Width ? area.X : Math.Clamp(note.X, area.X, area.Right - note.Width);
        var y = note.Height >= area.Height ? area.Y : Math.Clamp(note.Y, area.Y, area.Bottom - note.Height);
        return new Rect(x, y, note.Width, note.Height);
    }

    // 少しでも重なっているモニタを優先し、どれとも重なっていなければ
    // 中心が最も近いモニタ。同点ならプライマリを選ぶ。
    private static MonitorInfo ChooseTarget(Rect note, IReadOnlyList<MonitorInfo> monitors)
    {
        var best = monitors[0];
        var bestOverlap = -1.0;
        var bestDistance = double.MaxValue;
        foreach (var monitor in monitors)
        {
            var visible = Rect.Intersect(note, monitor.WorkArea);
            var overlap = visible.IsEmpty ? 0 : visible.Width * visible.Height;
            var dx = monitor.WorkArea.X + monitor.WorkArea.Width / 2 - (note.X + note.Width / 2);
            var dy = monitor.WorkArea.Y + monitor.WorkArea.Height / 2 - (note.Y + note.Height / 2);
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var better = overlap > bestOverlap
                || (overlap == bestOverlap && distance < bestDistance)
                || (overlap == bestOverlap && distance == bestDistance && monitor.IsPrimary);
            if (!better) continue;
            (best, bestOverlap, bestDistance) = (monitor, overlap, distance);
        }
        return best;
    }

    // ─── Win32 ──────────────────────────────────────────────────

    private const int MONITORINFOF_PRIMARY = 1;
    private const int MDT_EFFECTIVE_DPI = 0;

    private static Rect ToRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    private static double GetScale(IntPtr monitor)
    {
        try
        {
            if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
                return dpiX / 96.0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // shcore が無い環境では拡大率を見ない。
        }
        return 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT clip, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
