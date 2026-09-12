using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenPinNotes;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

// Opt-in performance/interactive fixture; never opens the user's data store.
internal sealed class ProbeApp : App
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/unfold-probe"));
    private readonly bool benchmark;
    private static bool StableFont;
    public ProbeApp(bool benchmark) => this.benchmark = benchmark;
    [STAThread]
    public static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable(StorageService.DataDirEnvVar, Root);
        StableFont = args.Contains("--stable-font");
        new ProbeApp(args.Contains("--benchmark")).Run();
    }
    private static object? Call(StickyNoteWindow w, string name, params object?[] args) =>
        typeof(StickyNoteWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, args);
    private static void Invalidate(StickyNoteWindow w) => typeof(StickyNoteWindow)
        .GetField("_expandedContentValid", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(w, false);
    private async Task Settle()
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(25);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            Directory.CreateDirectory(Root);
            var storage = new StorageService(Root);
            var note = new StickyNote { Id = "heavy-markdown", Title = "検証用：長文・表・画像8枚", Icon = "🧪",
                IsTitleBarHidden = true, Width = 680, Height = 680, FoldedWidth = 360, X = 100, Y = 100 };
            var assets = storage.GetNoteAssetsDirectoryPath(note.Id);
            Directory.CreateDirectory(assets);
            for (var n = 0; n < 8; n++)
            {
                var path = Path.Combine(assets, $"test-{n}.png");
                if (File.Exists(path)) continue;
                const int width = 1920, height = 1080;
                var pixels = new byte[width * height * 4];
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var i = (y * width + x) * 4;
                    pixels[i] = (byte)((x / 8 + n * 27) % 256);
                    pixels[i + 1] = (byte)((y / 5 + n * 17) % 256);
                    pixels[i + 2] = (byte)(((x / 120 + y / 90) % 2) * 100 + 80);
                    pixels[i + 3] = 255;
                }
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
                using var output = File.Create(path);
                encoder.Save(output);
            }
            var text = new StringBuilder("# 重量級Markdown検証 — 80節・画像8枚\n\n");
            for (var i = 0; i < 80; i++)
            {
                text.AppendLine($"## {i + 1}. 表示速度と画像の検証");
                if (i % 10 == 0) text.AppendLine($"![検証画像{i / 10}](assets/test-{i / 10}.png){{width=560}}");
                text.AppendLine("**重要な項目**と*補足説明*、~~修正前の記述~~、`inline code`、[リンク](https://example.com)を含みます。");
                for (var j = 0; j < 3; j++) text.AppendLine("長い文章を含む付箋の展開速度を調べます。画像と表が混在した場合にも、折りたたみから戻した際の表示とスクロール位置が維持されることを確認します。");
                text.AppendLine("\n| 項目 | 条件 | 結果 |\n| --- | --- | --- |\n| 画像 | 1920 × 1080 | 確認用 |\n| 開閉 | 同一サイズ | 比較対象 |\n");
                text.AppendLine("- [x] 文章の表示\n- [ ] 展開速度の確認\n\n```csharp\nvar note = OpenMarkdown();\nnote.ToggleFold();\n```\n");
            }
            note.Content = text.ToString();
            storage.Save([note]);
            Settings.EnableFoldAnimation = true;
            Settings.Timings.FoldAnimationMs = 150;
            if (!benchmark)
            {
                foreach (var spine in new[] { true, false })
                {
                    var copy = JsonSerializer.Deserialize<StickyNote>(JsonSerializer.Serialize(note))!;
                    copy.Content = note.Content;
                    copy.X = spine ? 100 : 810;
                    copy.Title = spine ? "検証用：縦バーあり" : "検証用：縦バーなし";
                    var settings = new AppSettings { ShowTitleBarHiddenSpine = spine };
                    var w = new StickyNoteWindow(new StickyNoteViewModel(copy, settings), storage);
                    w.ShowInTaskbar = true;
                    w.Show();
                }
                return;
            }
            var results = new List<object>();
            foreach (var spine in new[] { false, true })
            {
                Settings.ShowTitleBarHiddenSpine = spine;
                var model = JsonSerializer.Deserialize<StickyNote>(JsonSerializer.Serialize(note))!;
                model.Content = note.Content;
                var w = new StickyNoteWindow(new StickyNoteViewModel(model, Settings), storage);
                var cold = Stopwatch.StartNew();
                w.Show(); w.UpdateLayout(); await Settle(); cold.Stop();
                var box = (RichTextBox)w.FindName("ContentBox");
                if (StableFont)
                    box.SetBinding(Control.FontSizeProperty, new System.Windows.Data.Binding(nameof(StickyNoteViewModel.FontSize)));
                var blockCount = box.Document.Blocks.Count;
                foreach (var animated in new[] { false, true })
                foreach (var forceRebuild in StableFont ? new[] { false } : new[] { false, true })
                {
                    Settings.EnableFoldAnimation = animated;
                    var times = new List<double>();
                    var syncTimes = new List<double>();
                    var reused = 0;
                    for (var i = 0; i < 12; i++)
                    {
                        Call(w, "ToggleFold", (object?)null);
                        if (animated) await Task.Delay(200);
                        await Settle();
                        var block = box.Document.Blocks.FirstBlock;
                        if (forceRebuild) Invalidate(w);
                        var sw = Stopwatch.StartNew();
                        Call(w, "ToggleFold", (object?)null);
                        syncTimes.Add(sw.Elapsed.TotalMilliseconds);
                        while ((bool)typeof(StickyNoteWindow).GetField("_isFoldAnimationRunning", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!) await Task.Delay(5);
                        w.UpdateLayout();
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                        times.Add(sw.Elapsed.TotalMilliseconds);
                        if (ReferenceEquals(block, box.Document.Blocks.FirstBlock)) reused++;
                        await Settle();
                    }
                    results.Add(new { spine, animated, forceRebuild, coldMs = cold.Elapsed.TotalMilliseconds, blockCount, reused,
                        medianMs = times.Order().ElementAt(times.Count / 2), maxMs = times.Max(), times, syncTimes });
                    File.WriteAllText(Path.Combine(Root, StableFont ? "results-stable-font.json" : "results.json"), JsonSerializer.Serialize(new { characters = note.Content.Length, lines = note.Content.Count(c => c == '\n') + 1, images = 8, results }, new JsonSerializerOptions { WriteIndented = true }));
                }
                w.Close();
            }
            Shutdown();
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(Root, "error.txt"), ex.ToString()); Shutdown(1); }
    }
}
