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
using System.Text;

namespace ScreenPinNotes.Services;

/// <summary>
/// 付箋が活性化されたのが、タスクバーや Alt+Tab・タスクビューで選ばれたからなのかを見分ける。
/// 別プロセスからの WM_ACTIVATE は直前のウインドウを教えてくれず（lParam が 0）、
/// Alt+Tab は Alt を離した時点で切り替わるのでキーの状態も当てにならない。
/// そこで前面のウインドウが変わるたびに記録しておき、直前に前面だったのが
/// シェルの切り替え用ウインドウかどうかで判断する。他アプリの窓を閉じた・最小化したことで
/// Windows が付箋へ入力先を移したときは、直前の前面はその窓（もう見えない）になる。
/// </summary>
/// <remarks>UI スレッドから使う。フックの通知は、設定したスレッドのメッセージループで届く。</remarks>
public static class ShellSwitchTracker
{
    /// <summary>
    /// 見えなくなった切り替え用ウインドウを、直前の切り替えとみなす時間。Alt+Tab の
    /// 切り替え画面は選んだ直後に消えるうえ、切り替えの後にもう一度前面の通知を出すことがある。
    /// 時間で区切らないと、その後で他の窓を閉じたときまで切り替えと取り違える。
    /// </summary>
    public const long RecentSwitchMs = 1500;

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private static readonly string[] SwitcherClasses =
    [
        "Shell_TrayWnd",                 // タスクバー（ウインドウが1つのボタン）
        "Shell_SecondaryTrayWnd",        // サブモニターのタスクバー
        "XamlExplorerHostIslandWindow",  // タスクバーのサムネイル一覧・Alt+Tab・タスクビュー
        "ForegroundStaging",             // Alt+Tab で選んだ窓へ移る途中
        "MultitaskingViewFrame",         // 旧来のタスクビュー / Alt+Tab
        "TaskSwitcherWnd",               // 旧来の Alt+Tab
        "TaskListThumbnailWnd",          // 旧来のタスクバーのサムネイル
    ];

    private static WinEventDelegate? _callback;
    private static IntPtr _hook;
    private static IntPtr _lastForeground;
    private static long _lastForegroundAt;

    /// <summary>前面の変化の記録を始める。何度呼んでもよい。</summary>
    public static void EnsureInstalled()
    {
        if (_hook != IntPtr.Zero) return;
        _callback = OnForegroundChanged;
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public static void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        _callback = null;
    }

    /// <summary>
    /// 活性化したばかりの <paramref name="activated"/> が、ユーザーが切り替えて選んだものか。
    /// </summary>
    public static bool WasActivatedByUserSwitch(IntPtr activated)
    {
        if (IsCursorOverTaskbar()) return true;

        var previous = _lastForeground;
        if (previous == IntPtr.Zero || previous == activated) return false;
        var className = GetClassName(previous);
        var visible = IsWindow(previous) && IsWindowVisible(previous);
        return IsUserSwitch(className, visible, Environment.TickCount64 - _lastForegroundAt);
    }

    /// <summary>
    /// 直前に前面だったウインドウから、切り替え操作だったかを決める。
    /// タスクビューは選ぶまで出ているので、見えている間は時間を問わない。
    /// タスクバーは常に見えているので、直前に触れたときだけ数える。
    /// </summary>
    public static bool IsUserSwitch(string previousClassName, bool previousVisible, long elapsedMs)
        => Array.IndexOf(SwitcherClasses, previousClassName) >= 0 &&
           (elapsedMs <= RecentSwitchMs ||
            (previousVisible && previousClassName is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd")));

    private static void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (idObject != 0) return;
        RecordForeground(hwnd);
    }

    /// <summary>前面になったウインドウを記録する。</summary>
    public static void RecordForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        // 活性化した付箋自身の通知は WM_ACTIVATE より後に届く。自分を直前として
        // 覚えても判断には使わない（呼び出し側で除く）ので、そのまま上書きしてよい。
        _lastForeground = hwnd;
        _lastForegroundAt = Environment.TickCount64;
    }

    private static bool IsCursorOverTaskbar()
    {
        if (!GetCursorPos(out var point)) return false;
        var root = GetAncestor(WindowFromPoint(point), GA_ROOT);
        return root != IntPtr.Zero && GetClassName(root) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    private const uint GA_ROOT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventDelegate callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
}
