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
