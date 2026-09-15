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
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

/// <summary>
/// ログを書くアプリはファイルを開いたまま追記するので、閉じるまで
/// FileSystemWatcher に通知が来ないことがある。確認（ポーリング）だけでも
/// 追記に気付けることを、監視を切った状態で確かめる。
/// </summary>
public class ExternalFileMonitorTests
{
    private static readonly ExternalFileSettings FastPolling =
        new() { PollIntervalMs = 200, PollBurstMs = 60_000, IdlePollIntervalMs = 200 };

    [Fact]
    public void Polling_NoticesAppendsToAFileTheWriterKeepsOpen()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "app.log");
            using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            writer.Write("first\n"u8);
            writer.Flush();
            using var changed = new SemaphoreSlim(0);
            using var monitor = new ExternalFileMonitor(path, () => FastPolling, () => changed.Release(), useWatcher: false);

            writer.Write("second\n"u8);
            writer.Flush();

            Assert.True(changed.Wait(TimeSpan.FromSeconds(5)));
        }
        finally { DeleteTempDirectory(dir); }
    }

    [Fact]
    public void Polling_StaysQuietWhileTheFileIsUnchanged()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "notes.md");
            File.WriteAllText(path, "# unchanged");
            using var changed = new SemaphoreSlim(0);
            using var monitor = new ExternalFileMonitor(path, () => FastPolling, () => changed.Release(), useWatcher: false);

            Assert.False(changed.Wait(TimeSpan.FromMilliseconds(900)));
        }
        finally { DeleteTempDirectory(dir); }
    }

    [Fact]
    public void Polling_NoticesAFileThatAppearsLater()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "later.log");
            using var changed = new SemaphoreSlim(0);
            using var monitor = new ExternalFileMonitor(path, () => FastPolling, () => changed.Release(), useWatcher: false);

            File.WriteAllText(path, "created");

            Assert.True(changed.Wait(TimeSpan.FromSeconds(5)));
        }
        finally { DeleteTempDirectory(dir); }
    }

    [Fact]
    public void Dispose_StopsNotifications()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "app.log");
            File.WriteAllText(path, "first\n");
            using var changed = new SemaphoreSlim(0);
            var monitor = new ExternalFileMonitor(path, () => FastPolling, () => changed.Release());
            monitor.Dispose();

            File.AppendAllText(path, "second\n");

            Assert.False(changed.Wait(TimeSpan.FromMilliseconds(900)));
        }
        finally { DeleteTempDirectory(dir); }
    }

    [Fact]
    public void NextPollInterval_IsShortDuringTheBurstAndLongAfterwards()
    {
        var settings = new ExternalFileSettings { PollIntervalMs = 1000, PollBurstMs = 30_000, IdlePollIntervalMs = 5000 };

        Assert.Equal(TimeSpan.FromSeconds(1), ExternalFileMonitor.NextPollInterval(now: 100, burstUntil: 200, settings));
        Assert.Equal(TimeSpan.FromSeconds(5), ExternalFileMonitor.NextPollInterval(now: 200, burstUntil: 200, settings));
    }

    [Fact]
    public void Normalize_KeepsPollingIntervalsInRange()
    {
        var settings = new AppSettings { ExternalFile = { PollIntervalMs = 0, PollBurstMs = -5, IdlePollIntervalMs = 10 } };

        settings.Normalize();

        Assert.Equal(200, settings.ExternalFile.PollIntervalMs);
        Assert.Equal(0, settings.ExternalFile.PollBurstMs);
        // The idle interval is never shorter than the burst interval.
        Assert.Equal(200, settings.ExternalFile.IdlePollIntervalMs);
    }

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteTempDirectory(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (IOException) { }
    }
}
