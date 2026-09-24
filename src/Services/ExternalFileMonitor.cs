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
using ScreenPinNotes.Models;
using Timer = System.Threading.Timer;

namespace ScreenPinNotes.Services;

/// <summary>
/// 外部ファイルの更新を知らせる。通常は変更通知のみ、ログではポーリングも併用できる。
/// 書き手がファイルを開いたまま追記していると、NTFS はディレクトリ側の
/// 長さ・更新日時を閉じるまで更新せず、監視の通知も届かないことがある。
/// そこで監視の通知や付箋に触れたのをきっかけに、長さと更新日時を
/// <see cref="ExternalFilePoller"/> の共有タイマーで確かめる。
/// <see cref="ExternalFileSettings.PollStopAfterMs"/> のあいだ変化が無ければ確認を止め、
/// 監視だけを残す――ただし、止める前に <see cref="FileHolders"/> で
/// 「まだ誰かが開いたままか」を確かめ、開いたままなら確認を続ける。
/// 通知が届かないのはまさにその状態なので、止めてしまうと更新に気付けなくなる。
/// </summary>
/// <remarks><paramref name="changed"/> はワーカースレッドから呼ばれる。</remarks>
public sealed class ExternalFileMonitor : IDisposable
{
    private readonly string _path;
    private readonly Func<ExternalFileSettings> _settings;
    private readonly Action _changed;
    private readonly ExternalFilePoller _poller;
    private readonly bool _usePolling;
    private readonly FileSystemWatcher? _watcher;
    private readonly object _gate = new();
    private FileSignature _last;
    private long _lastActivity;
    private bool _writerHoldsOpen;
    private long _holdCheckedAt;
    private bool _disposed;

    public ExternalFileMonitor(string path, Func<ExternalFileSettings> settings, Action changed,
        bool useWatcher = true, ExternalFilePoller? poller = null, bool usePolling = true)
    {
        _path = Path.GetFullPath(path);
        _settings = settings;
        _changed = changed;
        _poller = poller ?? ExternalFilePoller.Shared;
        _usePolling = usePolling;
        if (_usePolling) _last = FileSignature.Read(_path);

        if (useWatcher)
            _watcher = TryCreateWatcher();

        // 開いた直後は書き手が動いている最中のことが多いので、確認から始める。
        Wake();
    }

    internal ExternalFileSettings Settings => _settings();

    /// <summary>
    /// 最後に確かめた時点で、ほかのプロセスがこのファイルを開いたままだったか。
    /// 確認を止めようとするときにだけ確かめるので、動きのあるファイルでは false のまま。
    /// </summary>
    public bool WriterHoldsOpen { get { lock (_gate) return _writerHoldsOpen; } }

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
            // ログのポーリングが有効なら、監視できない場所でも定期確認は続ける。
            ErrorReporter.ReportNonFatal("Watch external content", ex);
            return null;
        }
    }

    private void OnWatcherEvent()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_usePolling) _last = FileSignature.Read(_path);
        }
        Wake();
        _changed();
    }

    /// <summary>確認を（止まっていれば）再開し、止めるまでの時間を数え直す。</summary>
    public void Wake()
    {
        if (!_usePolling) return;
        // 先に時刻を進めてから登録する。確認側が止める直前に時刻を読み直すので、
        // この順なら再開の要求を取りこぼさない。
        Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);
        lock (_gate)
        {
            if (_disposed) return;
            // 次に止めようとするときは、開いたままかどうかを確かめ直す。
            _holdCheckedAt = 0;
        }
        _poller.Add(this);
    }

    /// <summary>
    /// 確認を続けるべきか。最後の変化から止めるまでの時間が経っていなければ続ける。
    /// 経っていても、書き手がファイルを開いたままなら続ける
    /// （<see cref="RefreshWriterHoldsOpen"/> が先に確かめておく）。
    /// </summary>
    internal bool ShouldKeepPolling(long now)
    {
        lock (_gate)
        {
            if (_disposed) return false;
            if (_writerHoldsOpen) return true;
        }
        return now - Interlocked.Read(ref _lastActivity) < _settings().PollStopAfterMs;
    }

    /// <summary>
    /// 確認を止めようとしているときだけ、ほかのプロセスがこのファイルを
    /// 開いたままかを確かめ直す。問い合わせは数十ミリ秒かかるので、
    /// <see cref="ExternalFileSettings.PollStopAfterMs"/> に1回までに抑え、
    /// 共有タイマーの鍵を持たないところから呼ぶ。
    /// </summary>
    internal void RefreshWriterHoldsOpen(long now)
    {
        var settings = _settings();
        lock (_gate)
        {
            if (_disposed) return;
            if (!settings.PollWhileWriterHoldsOpen)
            {
                _writerHoldsOpen = false;
                return;
            }
            // まだ止める時間ではない（＝確認は続く）なら、尋ねる必要がない。
            if (now - Interlocked.Read(ref _lastActivity) < settings.PollStopAfterMs) return;
            if (_holdCheckedAt != 0 && now - _holdCheckedAt < settings.PollStopAfterMs) return;
            // 尋ねる前に印を付ける。鍵を離している間に同じ問い合わせを重ねないため。
            _holdCheckedAt = now;
        }

        var state = FileHolders.Query(_path);
        lock (_gate)
        {
            if (_disposed) return;
            // 確かめられなかったときは、これまでどおり時間で止める。
            _writerHoldsOpen = state == FileHolders.HoldState.HeldByAnotherProcess;
        }
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

    /// <summary>共有タイマーで確かめるたびに呼ばれる（ワーカースレッドから）。</summary>
    public event Action? Polled;

    /// <summary>変化が無いまま時間が経ち、確かめるのを止めたときに呼ばれる（ワーカースレッドから）。</summary>
    public event Action? PollingStopped;

    internal void RaisePolled()
    {
        if (!IsDisposed) Polled?.Invoke();
    }

    internal void RaisePollingStopped()
    {
        if (!IsDisposed) PollingStopped?.Invoke();
    }

    private bool IsDisposed
    {
        get { lock (_gate) return _disposed; }
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

        // 「開いたままか」の問い合わせも鍵の外で。止めようとしている付箋だけが
        // 実際に尋ねるので、動きのあるファイルばかりのときは何も起きない。
        var beforeDecision = Environment.TickCount64;
        foreach (var monitor in monitors)
        {
            try { monitor.RefreshWriterHoldsOpen(beforeDecision); }
            catch (Exception ex) { ErrorReporter.ReportNonFatal("Check external file holders", ex); }
        }

        var stopped = new List<ExternalFileMonitor>();
        var kept = new List<ExternalFileMonitor>();
        lock (_gate)
        {
            // 止めるかどうかは鍵の中で時刻を読み直して決める。確かめている間に
            // Wake された付箋を、古い時刻のまま外してしまわないため。
            var now = Environment.TickCount64;
            foreach (var monitor in monitors)
            {
                if (!_monitors.Contains(monitor))
                    continue;
                if (monitor.ShouldKeepPolling(now))
                {
                    kept.Add(monitor);
                    continue;
                }
                _monitors.Remove(monitor);
                stopped.Add(monitor);
            }
            if (_monitors.Count == 0)
                _running = false;
            else
                Schedule(_monitors.First().Settings);
        }

        // 知らせは鍵の外で出す（受け手が UI スレッドへ渡すのを待たせないため）。
        foreach (var monitor in stopped)
            monitor.RaisePollingStopped();
        foreach (var monitor in kept)
            monitor.RaisePolled();
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
