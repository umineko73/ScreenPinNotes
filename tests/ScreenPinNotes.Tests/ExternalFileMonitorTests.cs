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

using System.Diagnostics;
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
        new() { PollIntervalMs = 200, PollStopAfterMs = 60_000 };

    [Fact]
    public void WatcherOnly_NoticesWritesAndReplacementWithoutStartingPolling()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "image.png");
            File.WriteAllText(path, "first");
            var poller = new ExternalFilePoller();
            var notifications = 0;
            using var monitor = new ExternalFileMonitor(path, () => FastPolling,
                () => Interlocked.Increment(ref notifications), poller: poller, usePolling: false);
            monitor.Wake();
            Assert.False(poller.IsRunning);
            Assert.Equal(0, poller.Count);
            File.WriteAllText(path, "second");
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref notifications) > 0, 5000));
            var before = Volatile.Read(ref notifications);
            var replacement = Path.Combine(dir, "replacement.tmp");
            File.WriteAllText(replacement, "third");
            File.Move(replacement, path, overwrite: true);
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref notifications) > before, 5000));
            Assert.False(poller.IsRunning);
            Assert.Equal(0, poller.Count);
            Assert.False(monitor.WriterHoldsOpen);
        }
        finally { DeleteTempDirectory(dir); }
    }

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
    public void Poller_StopsPollingAQuietFileAndThenStopsTheTimer()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "app.log");
            File.WriteAllText(path, "first\n");
            var settings = new ExternalFileSettings { PollIntervalMs = 200, PollStopAfterMs = 600 };
            var poller = new ExternalFilePoller();
            using var monitor = new ExternalFileMonitor(path, () => settings, () => { }, useWatcher: false, poller);

            Assert.True(poller.Contains(monitor));
            Assert.True(poller.IsRunning);

            Assert.True(WaitUntil(() => !poller.IsRunning, TimeSpan.FromSeconds(5)));
            Assert.False(poller.Contains(monitor));
            Assert.Equal(0, poller.Count);
        }
        finally { DeleteTempDirectory(dir); }
    }

    [Fact]
    public void Poller_KeepsPollingWhileTheFileKeepsChanging()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "app.log");
            File.WriteAllText(path, "first\n");
            var settings = new ExternalFileSettings { PollIntervalMs = 200, PollStopAfterMs = 800 };
            var poller = new ExternalFilePoller();
            using var monitor = new ExternalFileMonitor(path, () => settings, () => { }, useWatcher: false, poller);

            // 止めるまでの時間を超えて追記を続けても、確認は止まらない。
            var until = Environment.TickCount64 + 2000;
            while (Environment.TickCount64 < until)
            {
                File.AppendAllText(path, "more\n");
                Thread.Sleep(250);
                Assert.True(poller.Contains(monitor));
            }
        }
        finally { DeleteTempDirectory(dir); }
    }

    [Fact]
    public void Wake_RestartsPollingAfterItStopped()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "app.log");
            using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            writer.Write("first\n"u8);
            writer.Flush();
            var settings = new ExternalFileSettings { PollIntervalMs = 200, PollStopAfterMs = 400 };
            var poller = new ExternalFilePoller();
            using var changed = new SemaphoreSlim(0);
            using var monitor = new ExternalFileMonitor(path, () => settings, () => changed.Release(), useWatcher: false, poller);
            Assert.True(WaitUntil(() => !poller.IsRunning, TimeSpan.FromSeconds(5)));

            // 止まっている間の追記には気付かない（監視も切ってある）。
            writer.Write("second\n"u8);
            writer.Flush();
            Assert.False(changed.Wait(TimeSpan.FromMilliseconds(600)));

            // 付箋に触れるなどで再開すると、次の確認で気付く。
            monitor.Wake();
            Assert.True(poller.IsRunning);
            Assert.True(changed.Wait(TimeSpan.FromSeconds(5)));
        }
        finally { DeleteTempDirectory(dir); }
    }

    [Fact]
    public void Watcher_RestartsPollingAfterItStopped()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "app.log");
            File.WriteAllText(path, "first\n");
            var settings = new ExternalFileSettings { PollIntervalMs = 200, PollStopAfterMs = 400 };
            var poller = new ExternalFilePoller();
            using var changed = new SemaphoreSlim(0);
            using var monitor = new ExternalFileMonitor(path, () => settings, () => changed.Release(), useWatcher: true, poller);
            Assert.True(WaitUntil(() => !poller.IsRunning, TimeSpan.FromSeconds(5)));

            File.AppendAllText(path, "second\n");

            Assert.True(changed.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(poller.Contains(monitor));
            Assert.True(poller.IsRunning);
        }
        finally { DeleteTempDirectory(dir); }
    }

    [Fact]
    public void Poller_SharesOneTimerAndRunsUntilEveryFileIsQuiet()
    {
        var dir = CreateTempDirectory();
        try
        {
            var quietPath = Path.Combine(dir, "quiet.log");
            var busyPath = Path.Combine(dir, "busy.log");
            File.WriteAllText(quietPath, "quiet\n");
            File.WriteAllText(busyPath, "busy\n");
            var settings = new ExternalFileSettings { PollIntervalMs = 200, PollStopAfterMs = 600 };
            var poller = new ExternalFilePoller();
            using var quiet = new ExternalFileMonitor(quietPath, () => settings, () => { }, useWatcher: false, poller);
            using var busy = new ExternalFileMonitor(busyPath, () => settings, () => { }, useWatcher: false, poller);
            Assert.Equal(2, poller.Count);

            // 静かなファイルだけが外れ、動いているファイルがある間はタイマーも動き続ける。
            var until = Environment.TickCount64 + 1500;
            while (Environment.TickCount64 < until)
            {
                File.AppendAllText(busyPath, "more\n");
                Thread.Sleep(200);
            }
            Assert.False(poller.Contains(quiet));
            Assert.True(poller.Contains(busy));
            Assert.True(poller.IsRunning);

            Assert.True(WaitUntil(() => !poller.IsRunning, TimeSpan.FromSeconds(5)));
            Assert.Equal(0, poller.Count);
        }
        finally { DeleteTempDirectory(dir); }
    }

    [Fact]
    public void Poller_ReportsEachCheckAndWhenItStops()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "app.log");
            File.WriteAllText(path, "first\n");
            var settings = new ExternalFileSettings { PollIntervalMs = 200, PollStopAfterMs = 700 };
            var poller = new ExternalFilePoller();
            var polled = 0;
            var stopped = 0;
            using var monitor = new ExternalFileMonitor(path, () => settings, () => { }, useWatcher: false, poller);
            monitor.Polled += () => Interlocked.Increment(ref polled);
            monitor.PollingStopped += () => Interlocked.Increment(ref stopped);

            Assert.True(WaitUntil(() => !poller.IsRunning, TimeSpan.FromSeconds(5)));
            Thread.Sleep(300);

            // 止めるまでに何度か確かめ、止めたことは1回だけ知らせる。
            Assert.InRange(polled, 1, 4);
            Assert.Equal(1, stopped);

            // 再開すると、また確認の知らせが届く。
            var before = Volatile.Read(ref polled);
            monitor.Wake();
            Assert.True(WaitUntil(() => Volatile.Read(ref polled) > before, TimeSpan.FromSeconds(5)));
        }
        finally { DeleteTempDirectory(dir); }
    }

    [Fact]
    public void Dispose_StopsTheCheckNotifications()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "app.log");
            File.WriteAllText(path, "first\n");
            var poller = new ExternalFilePoller();
            var events = 0;
            var monitor = new ExternalFileMonitor(path, () => FastPolling, () => { }, useWatcher: false, poller);
            monitor.Polled += () => Interlocked.Increment(ref events);
            monitor.PollingStopped += () => Interlocked.Increment(ref events);

            monitor.Dispose();
            Thread.Sleep(600);

            Assert.Equal(0, events);
        }
        finally { DeleteTempDirectory(dir); }
    }

    [Fact]
    public void Dispose_RemovesTheFileFromThePoller()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "app.log");
            File.WriteAllText(path, "first\n");
            var poller = new ExternalFilePoller();
            var monitor = new ExternalFileMonitor(path, () => FastPolling, () => { }, useWatcher: false, poller);
            Assert.True(poller.Contains(monitor));

            monitor.Dispose();
            monitor.Wake();

            Assert.False(poller.Contains(monitor));
            Assert.True(WaitUntil(() => !poller.IsRunning, TimeSpan.FromSeconds(5)));
        }
        finally { DeleteTempDirectory(dir); }
    }

    /// <summary>
    /// 書き手が開いたままのファイルは、変化が止まっても確認を続ける。
    /// 通知が届かないのはまさにその状態なので、止めると更新に気付けなくなる。
    /// </summary>
    [Fact]
    public void Poller_KeepsPollingWhileAnotherProcessHoldsTheFileOpen()
    {
        var dir = CreateTempDirectory();
        Process? writer = null;
        try
        {
            var path = Path.Combine(dir, "held.log");
            File.WriteAllText(path, "first\n");
            // 同じプロセスのハンドルは「開いたまま」と見なさないので、別のプロセスに握らせる。
            writer = StartHoldingProcess(path, seconds: 30);

            var settings = new ExternalFileSettings { PollIntervalMs = 200, PollStopAfterMs = 1000 };
            var poller = new ExternalFilePoller();
            using var monitor = new ExternalFileMonitor(path, () => settings, () => { }, useWatcher: false, poller);

            // 止めるまでの時間を大きく超えても、確認は続いている。
            Assert.False(WaitUntil(() => !poller.Contains(monitor), TimeSpan.FromSeconds(4)));
            Assert.True(poller.IsRunning);
            Assert.True(monitor.WriterHoldsOpen);
        }
        finally
        {
            StopHoldingProcess(writer);
            DeleteTempDirectory(dir);
        }
    }

    /// <summary>書き手が閉じたら、いつもどおり時間で止まる。</summary>
    [Fact]
    public void Poller_StopsOnceTheHoldingProcessClosesTheFile()
    {
        var dir = CreateTempDirectory();
        Process? writer = null;
        try
        {
            var path = Path.Combine(dir, "released.log");
            File.WriteAllText(path, "first\n");
            writer = StartHoldingProcess(path, seconds: 30);

            var settings = new ExternalFileSettings { PollIntervalMs = 200, PollStopAfterMs = 600 };
            var poller = new ExternalFilePoller();
            using var monitor = new ExternalFileMonitor(path, () => settings, () => { }, useWatcher: false, poller);
            Assert.True(WaitUntil(() => monitor.WriterHoldsOpen, TimeSpan.FromSeconds(5)));

            StopHoldingProcess(writer);
            writer = null;

            Assert.True(WaitUntil(() => !poller.IsRunning, TimeSpan.FromSeconds(10)));
            Assert.False(poller.Contains(monitor));
            Assert.False(monitor.WriterHoldsOpen);
        }
        finally
        {
            StopHoldingProcess(writer);
            DeleteTempDirectory(dir);
        }
    }

    /// <summary>設定で切れば、開いたままでもこれまでどおり時間で止まる。</summary>
    [Fact]
    public void Poller_StopsWhilePollWhileWriterHoldsOpenIsOff()
    {
        var dir = CreateTempDirectory();
        Process? writer = null;
        try
        {
            var path = Path.Combine(dir, "ignored.log");
            File.WriteAllText(path, "first\n");
            writer = StartHoldingProcess(path, seconds: 30);

            var settings = new ExternalFileSettings
            {
                PollIntervalMs = 200,
                PollStopAfterMs = 600,
                PollWhileWriterHoldsOpen = false,
            };
            var poller = new ExternalFilePoller();
            using var monitor = new ExternalFileMonitor(path, () => settings, () => { }, useWatcher: false, poller);

            Assert.True(WaitUntil(() => !poller.IsRunning, TimeSpan.FromSeconds(5)));
            Assert.False(monitor.WriterHoldsOpen);
        }
        finally
        {
            StopHoldingProcess(writer);
            DeleteTempDirectory(dir);
        }
    }

    [Fact]
    public void Holders_ReportsNobodyForAFileNoOneHasOpen()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "closed.log");
            File.WriteAllText(path, "first\n");

            Assert.Equal(FileHolders.HoldState.NotHeld, FileHolders.Query(path));
        }
        finally { DeleteTempDirectory(dir); }
    }

    /// <summary>自分のプロセスが開いているだけなら「開きっぱなし」とは見なさない。</summary>
    [Fact]
    public void Holders_IgnoresHandlesOfThisProcess()
    {
        var dir = CreateTempDirectory();
        try
        {
            var path = Path.Combine(dir, "mine.log");
            File.WriteAllText(path, "first\n");
            using var mine = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            Assert.Equal(FileHolders.HoldState.NotHeld, FileHolders.Query(path));
        }
        finally { DeleteTempDirectory(dir); }
    }

    [Fact]
    public void Holders_ReportsUnknownForAMissingFile()
    {
        var dir = CreateTempDirectory();
        try
        {
            Assert.Equal(FileHolders.HoldState.Unknown, FileHolders.Query(Path.Combine(dir, "nothing.log")));
        }
        finally { DeleteTempDirectory(dir); }
    }

    /// <summary>共有を許さずに開かれていて中身が読めなくても、掴まれているのは分かる。</summary>
    [Fact]
    public void Holders_ReportsHeldWhenTheWriterAllowsNoSharing()
    {
        var dir = CreateTempDirectory();
        Process? writer = null;
        try
        {
            var path = Path.Combine(dir, "exclusive.log");
            File.WriteAllText(path, "first\n");
            writer = StartHoldingProcess(path, seconds: 30, share: "None");

            Assert.Equal(FileHolders.HoldState.HeldByAnotherProcess, FileHolders.Query(path));
        }
        finally
        {
            StopHoldingProcess(writer);
            DeleteTempDirectory(dir);
        }
    }

    /// <summary>
    /// 尋ねている間もファイルを掴まない。属性を読むだけの開き方は共有の
    /// 取り合いに加わらないので、書き手が排他で開き直しても弾かれない。
    /// 中身を読む開き方に戻すと、この試験は必ず落ちる。
    /// </summary>
    [Fact]
    public void Holders_DoesNotGetInTheWayOfAWriterThatOpensExclusively()
    {
        var dir = CreateTempDirectory();
        Process? writer = null;
        try
        {
            var path = Path.Combine(dir, "reopened.log");
            File.WriteAllText(path, "first\n");
            var result = Path.Combine(dir, "failures.txt");
            // 開いては閉じる書き手。数秒のあいだ排他で開き直し、開けなかった回数を残す。
            writer = StartOpeningProcess(path, result, seconds: 3);

            // その間ずっと尋ね続ける。
            Assert.True(WaitUntil(
                () =>
                {
                    FileHolders.Query(path);
                    return File.Exists(result);
                },
                TimeSpan.FromSeconds(30)));

            Assert.Equal("0", File.ReadAllText(result).Trim());
        }
        finally
        {
            StopHoldingProcess(writer);
            DeleteTempDirectory(dir);
        }
    }

    /// <summary>
    /// ファイルを開いては閉じるを繰り返す別プロセス
    /// （ログを1行ごとに開き直すアプリの代役）。排他で開き、
    /// 開けなかった回数を <paramref name="resultPath"/> に残す。
    /// </summary>
    private static Process StartOpeningProcess(string path, string resultPath, int seconds)
    {
        var script =
            $"$fails=0; $until=(Get-Date).AddSeconds({seconds}); " +
            "while((Get-Date) -lt $until) { " +
            $"try {{ $fs=[IO.File]::Open('{path}','Append','Write','None'); $fs.Dispose() }} " +
            "catch { $fails++ } " +
            "Start-Sleep -Milliseconds 20 } " +
            $"$fails | Set-Content -Path '{resultPath}'";
        return Process.Start(new ProcessStartInfo("powershell", $"-NoProfile -Command \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
    }

    /// <summary>
    /// ファイルを開いたまま待つだけの別プロセス（ログを書くアプリの代役）。
    /// 開けたら合図のファイルを作らせ、それを待ってから確かめる。相手がまだ
    /// 開いていないうちに尋ねて空振りしないため。
    /// </summary>
    private static Process StartHoldingProcess(string path, int seconds, string share = "ReadWrite")
    {
        var ready = path + ".ready";
        var script = $"$fs=[IO.File]::Open('{path}','Append','Write','{share}'); " +
            $"New-Item -ItemType File -Path '{ready}' | Out-Null; " +
            $"Start-Sleep -Seconds {seconds}; $fs.Dispose()";
        var process = Process.Start(new ProcessStartInfo("powershell", $"-NoProfile -Command \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        if (WaitUntil(() => File.Exists(ready), TimeSpan.FromSeconds(30)))
            return process;

        StopHoldingProcess(process);
        throw new InvalidOperationException("ファイルを開いたまま待つプロセスを用意できなかった。");
    }

    private static void StopHoldingProcess(Process? process)
    {
        if (process == null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        finally { process.Dispose(); }
    }

    [Fact]
    public void Normalize_KeepsPollingSettingsInRange()
    {
        var settings = new AppSettings { ExternalFile = { PollIntervalMs = 0, PollStopAfterMs = 10 } };

        settings.Normalize();

        Assert.Equal(200, settings.ExternalFile.PollIntervalMs);
        Assert.Equal(1000, settings.ExternalFile.PollStopAfterMs);
    }

    [Fact]
    public void Defaults_PollEverySecondAndStopAfterAMinute()
    {
        var settings = new ExternalFileSettings();

        Assert.Equal(1000, settings.PollIntervalMs);
        Assert.Equal(60_000, settings.PollStopAfterMs);
        Assert.True(settings.PollWhileWriterHoldsOpen);
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var until = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < until)
        {
            if (condition()) return true;
            Thread.Sleep(20);
        }
        return condition();
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
