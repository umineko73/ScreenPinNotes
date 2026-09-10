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
    [WpfFact]
    public void AppIconIsBuiltIntoTheAssembly()
    {
        WpfApplicationFixture.Ensure();

        using var icon = App.TryLoadTrayIconResource();

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
