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
using Timer = System.Threading.Timer;

namespace ScreenPinNotes.Services;

/// <summary>
/// 外部ファイルの更新を知らせる。FileSystemWatcher だけでは足りない。
/// 書き手がファイルを開いたまま追記していると、NTFS はディレクトリ側の
/// 長さ・更新日時を閉じるまで更新せず、監視の通知も届かないことがある。
/// そこで監視の通知や付箋に触れたのをきっかけに、長さと更新日時を
/// <see cref="ExternalFilePoller"/> の共有タイマーで確かめる。
/// <see cref="ExternalFileSettings.PollStopAfterMs"/> のあいだ変化が無ければ確認を止め、
/// 監視だけを残す。
/// </summary>
/// <remarks><paramref name="changed"/> はワーカースレッドから呼ばれる。</remarks>
public sealed class ExternalFileMonitor : IDisposable
{
    private readonly string _path;
    private readonly Func<ExternalFileSettings> _settings;
    private readonly Action _changed;
    private readonly ExternalFilePoller _poller;
    private readonly FileSystemWatcher? _watcher;
    private readonly object _gate = new();
    private FileSignature _last;
    private long _lastActivity;
    private bool _disposed;

    public ExternalFileMonitor(string path, Func<ExternalFileSettings> settings, Action changed,
        bool useWatcher = true, ExternalFilePoller? poller = null)
    {
        _path = Path.GetFullPath(path);
        _settings = settings;
        _changed = changed;
        _poller = poller ?? ExternalFilePoller.Shared;
        _last = FileSignature.Read(_path);

        if (useWatcher)
            _watcher = TryCreateWatcher();

        // 開いた直後は書き手が動いている最中のことが多いので、確認から始める。
        Wake();
    }

    internal ExternalFileSettings Settings => _settings();

    private FileSystemWatcher? TryCreateWatcher()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            var fileName = Path.GetFileName(_path);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
                return null;

            var watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += (_, _) => OnWatcherEvent();
            watcher.Created += (_, _) => OnWatcherEvent();
            watcher.Renamed += (_, _) => OnWatcherEvent();
            watcher.Deleted += (_, _) => OnWatcherEvent();
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex)
        {
            // フォルダが無い・ネットワーク越しで監視できないなどでも、確認だけで追える。
            ErrorReporter.ReportNonFatal("Watch external content", ex);
            return null;
        }
    }

    private void OnWatcherEvent()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _last = FileSignature.Read(_path);
        }
        Wake();
        _changed();
    }

    /// <summary>確認を（止まっていれば）再開し、止めるまでの時間を数え直す。</summary>
    public void Wake()
    {
        // 先に時刻を進めてから登録する。確認側が止める直前に時刻を読み直すので、
        // この順なら再開の要求を取りこぼさない。
        Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);
        lock (_gate)
            if (_disposed) return;
        _poller.Add(this);
    }

    /// <summary>確認を続けるべきか。最後の変化から止めるまでの時間が経っていなければ続ける。</summary>
    internal bool ShouldKeepPolling(long now)
    {
        lock (_gate)
            if (_disposed) return false;
        return now - Interlocked.Read(ref _lastActivity) < _settings().PollStopAfterMs;
    }

    /// <summary>長さと更新日時を確かめ、変わっていれば知らせる。</summary>
    internal void Poll()
    {
        bool changed;
        lock (_gate)
        {
            if (_disposed) return;
            var current = FileSignature.Read(_path);
            changed = current != _last;
            _last = current;
        }
        if (!changed) return;
        Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);
        _changed();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _poller.Remove(this);
        _watcher?.Dispose();
    }
}

/// <summary>
/// 確認中の外部ファイルをまとめて確かめる、全付箋で1つのタイマー。
/// 確認中のファイルが無くなったら止まり、次に登録されたときに動き出す。
/// </summary>
public sealed class ExternalFilePoller
{
    public static ExternalFilePoller Shared { get; } = new();

    private readonly object _gate = new();
    private readonly HashSet<ExternalFileMonitor> _monitors = new();
    private readonly Timer _timer;
    private bool _running;

    public ExternalFilePoller() => _timer = new Timer(_ => Tick());

    /// <summary>タイマーが動いているか。</summary>
    public bool IsRunning { get { lock (_gate) return _running; } }

    /// <summary>確認中のファイルの数。</summary>
    public int Count { get { lock (_gate) return _monitors.Count; } }

    /// <summary>確認中なら true。</summary>
    public bool Contains(ExternalFileMonitor monitor) { lock (_gate) return _monitors.Contains(monitor); }

    internal void Add(ExternalFileMonitor monitor)
    {
        lock (_gate)
        {
            _monitors.Add(monitor);
            if (_running) return;
            _running = true;
            Schedule(monitor.Settings);
        }
    }

    internal void Remove(ExternalFileMonitor monitor)
    {
        lock (_gate) _monitors.Remove(monitor);
    }

    private void Schedule(ExternalFileSettings settings)
        => _timer.Change(TimeSpan.FromMilliseconds(settings.PollIntervalMs), Timeout.InfiniteTimeSpan);

    private void Tick()
    {
        ExternalFileMonitor[] monitors;
        lock (_gate) monitors = _monitors.ToArray();

        // ファイルを開くので、登録・解除を待たせないよう鍵の外で確かめる。
        foreach (var monitor in monitors)
        {
            try { monitor.Poll(); }
            catch (Exception ex) { ErrorReporter.ReportNonFatal("Poll external content", ex); }
        }

        lock (_gate)
        {
            // 止めるかどうかは鍵の中で時刻を読み直して決める。確かめている間に
            // Wake された付箋を、古い時刻のまま外してしまわないため。
            var now = Environment.TickCount64;
            _monitors.RemoveWhere(monitor => !monitor.ShouldKeepPolling(now));
            if (_monitors.Count == 0)
            {
                _running = false;
                return;
            }
            Schedule(_monitors.First().Settings);
        }
    }
}

/// <summary>更新の有無を見分けるための、ファイルの長さと更新日時。</summary>
public readonly record struct FileSignature(bool Exists, long Length, DateTime LastWriteTimeUtc)
{
    public static FileSignature Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return default;
            try
            {
                // 開いたまま追記されているファイルは、ディレクトリ側の長さが古いままのことがある。
                // 開いて尋ねれば今の長さが返る。書き手の邪魔をしないよう共有して開く。
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                return new FileSignature(true, stream.Length, info.LastWriteTimeUtc);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new FileSignature(true, info.Length, info.LastWriteTimeUtc);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return default;
        }
    }
}
