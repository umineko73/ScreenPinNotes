using System.IO;
using System.Reflection;
using System.Windows;
using ScreenPinNotes;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

internal sealed class ThemeProbe : App
{
    [STAThread]
    public new static void Main()
    {
        Environment.SetEnvironmentVariable(StorageService.DataDirEnvVar,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/theme-probe")));
        new ThemeProbe().Run();
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Settings.Theme = "Dark";
        var storage = new StorageService();
        var windows = (List<StickyNoteWindow>)typeof(App).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!;
        for (var i = 0; i < 35; i++)
        {
            var note = new StickyNote { Title = $"Theme test {i + 1}", Content = "# ダーク表示の確認\n- [x] チェックボックス\n" + string.Join("\n", Enumerable.Repeat("本文とスクロールバーを確認します。", 50)), Width = 350, Height = 350 };
            windows.Add(new StickyNoteWindow(new StickyNoteViewModel(note, Settings), storage));
        }
        windows[0].ShowInTaskbar = true;
        windows[0].Show();
        new SettingsWindow(Settings, this) { ShowInTaskbar = true }.Show();
        new NoteManagerWindow { ShowInTaskbar = true }.Show();
    }
}
