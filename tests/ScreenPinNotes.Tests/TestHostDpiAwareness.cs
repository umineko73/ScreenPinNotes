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

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ScreenPinNotes.Tests;

/// <summary>
/// テストホスト（testhost.exe）はアプリ本体の app.manifest を使わないため、既定では
/// PerMonitorV2 にならない。そのままだと <c>GetDpiForMonitor</c> がどのモニタにも
/// プロセスと同じ拡大率を返し、拡大率が混在した環境でも「すべて同じ」に見えてしまう。
/// 位置の検証が意味を持たなくなるので、ウィンドウを作る前に本体と同じモードへ合わせる。
/// </summary>
internal static class TestHostDpiAwareness
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    [ModuleInitializer]
    internal static void Initialize()
    {
        // 既にホストが決めている場合は失敗するが、それはそのまま尊重してよい。
        try { SetProcessDpiAwarenessContext(PerMonitorAwareV2); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}
