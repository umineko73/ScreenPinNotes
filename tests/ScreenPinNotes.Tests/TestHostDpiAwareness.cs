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
