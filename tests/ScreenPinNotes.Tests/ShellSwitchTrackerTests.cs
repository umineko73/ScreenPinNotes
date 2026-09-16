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

using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

/// <summary>
/// 畳んだ付箋は、タスクバーや Alt+Tab で選ばれたときだけ開く。他アプリの窓を
/// 閉じて入力先が回ってきただけのときは開かない。
/// </summary>
public class ShellSwitchTrackerTests
{
    [Theory]
    [InlineData("Shell_TrayWnd", true, 50)]                    // ウインドウ1つのタスクバーボタン
    [InlineData("Shell_SecondaryTrayWnd", true, 50)]           // サブモニターのタスクバー
    [InlineData("XamlExplorerHostIslandWindow", true, 5000)]   // サムネイル一覧・タスクビューで迷っている間
    [InlineData("ForegroundStaging", false, 10)]               // Alt+Tab で選んだ直後
    [InlineData("XamlExplorerHostIslandWindow", false, 100)]   // Alt+Tab の画面が閉じた直後
    public void SwitchingThroughTheShell_OpensTheNote(string className, bool visible, long elapsedMs)
        => Assert.True(ShellSwitchTracker.IsUserSwitch(className, visible, elapsedMs));

    [Theory]
    [InlineData("WindowsForms10.Window.8.app.0", false, 10)]   // 他アプリの窓を閉じた
    [InlineData("Notepad", true, 10)]                           // 他アプリの窓を最小化した
    [InlineData("HwndWrapper[ScreenPinNotes]", false, 10)]      // 自分のダイアログや付箋を閉じた
    [InlineData("XamlExplorerHostIslandWindow", false, 3000)]   // 前の Alt+Tab の名残
    [InlineData("Shell_TrayWnd", true, 3000)]                   // 前にタスクバーの空き領域を触っただけ
    [InlineData("", false, 0)]
    public void FocusHandedOverByWindows_KeepsTheNoteFolded(string className, bool visible, long elapsedMs)
        => Assert.False(ShellSwitchTracker.IsUserSwitch(className, visible, elapsedMs));
}
