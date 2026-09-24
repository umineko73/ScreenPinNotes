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

using System.IO;
using System.Windows.Threading;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

/// <summary>
/// ログは動かしっぱなしにすると必ずローテーションされる。同じ名前で作り直される
/// 方式（logrotate の既定や RollingFileAppender、copytruncate）では、付箋が
/// 新しい中身へ勝手に追いつけることを確かめる。
/// </summary>
public class ExternalLogRotationTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
    private readonly App _app;
    private readonly int _previousInterval;

    public ExternalLogRotationTests()
    {
        Directory.CreateDirectory(_temp);
        _app = (App)WpfApplicationFixture.Ensure();
        _previousInterval = _app.Settings.ExternalFile.MinRefreshIntervalMs;
    }

    private (StickyNoteWindow Window, StickyNoteViewModel Vm, string Path) OpenTailNote(bool show = true)
    {
        // 間引きを待たずに済ませる。ここで見たいのは追従できるかどうか。
        _app.Settings.ExternalFile.MinRefreshIntervalMs = 0;
        var logPath = Path.Combine(_temp, "app.log");
        File.WriteAllText(logPath, "line 1\n");
        var vm = new StickyNoteViewModel(
            new StickyNote
            {
                Content = "line 1\n", ExternalContentPath = logPath,
                ExternalTailMode = true, IsReadOnly = true,
            },
            _app.Settings);
        var window = new StickyNoteWindow(vm, new StorageService(_temp));
        if (show)
        {
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
        return (window, vm, logPath);
    }

    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstShow_ReadsChangesMadeWhileInitiallyHidden(bool tail)
    {
        var (window, vm, path) = OpenTailNote(show: false);
        try
        {
            vm.Model.ExternalTailMode = tail;
            File.WriteAllText(path, "changed before first show\n");
            window.Show();
            WaitFor(window, () => vm.Content.Contains("changed before first show"));
            Assert.Equal("changed before first show\n", vm.Content);
        }
        finally { window.Close(); }
    }

    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedRead_RetriesWithoutAnotherFileNotification(bool tail)
    {
        var (window, vm, path) = OpenTailNote();
        try
        {
            vm.Model.ExternalTailMode = tail;
            // Isolate retries from watcher events and signature changes on unlock.
            typeof(StickyNoteWindow).GetMethod("DisposeExternalContentWatcher",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(window, null);
            File.WriteAllText(path, "updated\n");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                window.ReloadExternalContent();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal("line 1\n", vm.Content);
            }
            WaitFor(window, () => vm.Content == "updated\n");
            Assert.Equal("updated\n", vm.Content);
        }
        finally { window.Close(); }
    }

    private static void WaitFor(StickyNoteWindow window, Func<bool> condition)
    {
        var until = Environment.TickCount64 + 4000;
        while (!condition() && Environment.TickCount64 < until)
        {
            Thread.Sleep(25);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    // app.log を退避して、同じ名前で作り直す方式。
    [WpfFact]
    public void RenameRotation_FollowsTheFileRecreatedUnderTheSameName()
    {
        var (window, vm, logPath) = OpenTailNote();
        try
        {
            File.Move(logPath, logPath + ".1");
            File.WriteAllText(logPath, "after rotation\n");

            WaitFor(window, () => vm.Content.Contains("after rotation"));

            Assert.Contains("after rotation", vm.Content);
        }
        finally { window.Close(); }
    }

    // copytruncate: 中身を退避してから同じファイルを空にして書き続ける方式。
    [WpfFact]
    public void TruncateRotation_FollowsTheEmptiedFile()
    {
        var (window, vm, logPath) = OpenTailNote();
        try
        {
            File.Copy(logPath, logPath + ".1");
            using (new FileStream(logPath, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite)) { }
            File.AppendAllText(logPath, "after truncate\n");

            WaitFor(window, () => vm.Content.Contains("after truncate"));

            Assert.Contains("after truncate", vm.Content);
        }
        finally { window.Close(); }
    }

    // 退避されて次のファイルが作られるまでの一瞬は読めない。そこで消えたと
    // 表示してしまうと、ローテーションのたびに中身が飛ぶ。
    [WpfFact]
    public void WhileTheFileIsMissing_TheLastContentStays()
    {
        var (window, vm, logPath) = OpenTailNote();
        try
        {
            File.Move(logPath, logPath + ".1");

            WaitFor(window, () => !vm.Content.Contains("line 1"));

            Assert.Contains("line 1", vm.Content);
        }
        finally { window.Close(); }
    }

    public void Dispose()
    {
        _app.Settings.ExternalFile.MinRefreshIntervalMs = _previousInterval;
        if (Directory.Exists(_temp)) Directory.Delete(_temp, true);
    }
}
