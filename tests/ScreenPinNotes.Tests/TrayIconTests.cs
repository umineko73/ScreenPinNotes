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

using System.Reflection;

namespace ScreenPinNotes.Tests;

/// <summary>
/// タスクトレイのアイコン。app.ico は csproj で &lt;Resource&gt; として持たせて
/// いるが、ビルドの中間生成物が古いままだと宣言があっても実際には入らず、
/// 起動のたびに例外になったことがある。ここで実物を読んで確かめておく。
/// </summary>
public class TrayIconTests
{
    private static readonly MethodInfo LoadTrayIconMethod = typeof(App).GetMethod(
        "LoadTrayIcon", BindingFlags.Static | BindingFlags.NonPublic)!;

    // アセンブリに app.ico が入っていること。入っていなければ、以前と同じく
    // 起動時に「リソース 'app.ico' を検索できません」になる。
    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppIconIsBuiltIntoTheAssembly(bool dark)
    {
        WpfApplicationFixture.Ensure();

        using var icon = App.TryLoadTrayIconResource(dark);

        Assert.NotNull(icon);
        Assert.True(icon!.Width > 0);
        Assert.True(icon.Height > 0);
    }

    // リソースが欠けていても起動は続ける。アイコンが読めないことと、
    // アプリが立ち上がらないことは釣り合わない。
    [WpfFact]
    public void TrayIconFallsBackInsteadOfThrowing()
    {
        WpfApplicationFixture.Ensure();

        var icon = LoadTrayIconMethod.Invoke(null, null);

        Assert.NotNull(icon);
        Assert.IsType<System.Drawing.Icon>(icon);
    }
}
