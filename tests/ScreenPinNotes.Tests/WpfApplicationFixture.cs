using System.Windows;
using ScreenPinNotes;

// UI テストはプロセス全体で共有される WPF の状態（Application、FontCatalog の
// 静的キャッシュなど）に触れるため、テストクラスを並列に走らせない。既定では
// クラスごとに別コレクションとして並列実行され、実行順と実行タイミングによって
// 落ちたり通ったりしていた。
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ScreenPinNotes.Tests;

// WPF は 1 つの AppDomain に Application を 1 つしか作れず、2 つ目を作ると
// InvalidOperationException になる。[WpfFact] はテストごとに別の STA スレッドで
// 動くので、各テストが Application.Current の null チェックだけで new App() して
// いると、null に見えた複数のスレッドが同時に生成へ入って落ちる。生成は
// ここで一度だけ行い、以降は同じインスタンスを共有する。
internal static class WpfApplicationFixture
{
    private static readonly object Gate = new();
    private static Application? _application;

    public static Application Ensure()
    {
        lock (Gate)
        {
            if (_application == null)
            {
                if (Application.Current == null)
                {
                    var app = new App();
                    app.InitializeComponent();
                    _application = app;
                }
                else
                {
                    _application = Application.Current;
                }
            }
        }

        // ShutdownMode は Application を所有するスレッドからしか触れない。
        // 別スレッドのテストから来た場合は、生成時に設定済みのものをそのまま使う。
        if (_application.Dispatcher.CheckAccess())
            _application.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        return _application;
    }
}
