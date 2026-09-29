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
using System.Security.Principal;

namespace ScreenPinNotes.Services;

/// <summary>
/// 付箋から開くアプリへ、今の環境変数を渡す。プロセスの環境変数は起動した時点の写しなので、
/// 常駐している間にシステムのプロパティなどで変えても、付箋から開いたアプリには古い値が渡っていた。
/// エクスプローラーは変更の通知を受けて自分の環境を作り直しているので、同じように
/// 開く直前にレジストリの現在の値（ユーザー・システム・揮発性の環境変数）を読み直す。
///
/// 読み直した値でまるごと置き換えるのではなく、起動時に読んだ値からの差分だけを当てる。
/// 起動した側が渡した変数（SCREENPINNOTES_DATA など）は、ユーザーが同じ変数を
/// 変えない限りそのまま残る。
/// </summary>
public static class UserEnvironment
{
    private static readonly object Gate = new();
    private static Dictionary<string, string>? _baseline;

    /// <summary>起動時の値を覚えておく。これより前の変更は、すでにこのプロセスに入っている。</summary>
    public static void CaptureBaseline()
    {
        var current = TryReadCurrent();
        lock (Gate) _baseline = current;
    }

    /// <summary>起動後に変わった環境変数を、このプロセスへ反映する。アプリを開く直前に呼ぶ。</summary>
    public static void Refresh()
    {
        try
        {
            var current = TryReadCurrent();
            if (current == null) return;
            lock (Gate)
            {
                if (_baseline != null)
                    ApplyChanges(_baseline, current, Environment.SetEnvironmentVariable);
                _baseline = current;
            }
        }
        catch (Exception ex)
        {
            // 古い環境のままでも開けるので、開くこと自体は止めない。
            ErrorReporter.ReportNonFatal("Refresh environment variables", ex);
        }
    }

    /// <summary>
    /// <paramref name="before"/> から <paramref name="after"/> への変更（追加・変更・削除）だけを
    /// <paramref name="set"/> で当てる。値 null は削除。変わっていない変数には触れない。
    /// </summary>
    public static void ApplyChanges(IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after, Action<string, string?> set)
    {
        foreach (var (name, value) in after)
        {
            if (!before.TryGetValue(name, out var old) || !string.Equals(old, value, StringComparison.Ordinal))
                set(name, value);
        }
        foreach (var name in before.Keys)
        {
            if (!after.ContainsKey(name))
                set(name, null);
        }
    }

    /// <summary>
    /// 今ログオンし直したら得られる環境変数。CreateEnvironmentBlock に親の環境を
    /// 継がせない（bInherit=false）ので、レジストリの現在の値から組み立てられる。
    /// </summary>
    public static Dictionary<string, string>? TryReadCurrent()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!CreateEnvironmentBlock(out var block, identity.Token, false))
            return null;
        try
        {
            return Parse(block);
        }
        finally
        {
            DestroyEnvironmentBlock(block);
        }
    }

    // "名前=値\0" が並び、空の文字列（\0\0）で終わる UTF-16 の並び。
    private static Dictionary<string, string> Parse(IntPtr block)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ptr = block;
        while (true)
        {
            var entry = Marshal.PtrToStringUni(ptr);
            if (string.IsNullOrEmpty(entry)) break;
            ptr += (entry.Length + 1) * sizeof(char);
            // "=C:=C:\..." のようなドライブごとのカレントディレクトリは環境変数ではない。
            var separator = entry.IndexOf('=', 1);
            if (entry[0] == '=' || separator <= 0) continue;
            result[entry[..separator]] = entry[(separator + 1)..];
        }
        return result;
    }

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);
}
