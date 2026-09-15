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
/// そこで長さと更新日時を定期的に確かめる。変化を見つけてからしばらくは
/// 短い間隔で、落ち着いたら長い間隔で確かめる。
/// </summary>
/// <remarks><paramref name="changed"/> はワーカースレッドから呼ばれる。</remarks>
public sealed class ExternalFileMonitor : IDisposable
{
    private readonly string _path;
    private readonly Func<ExternalFileSettings> _settings;
    private readonly Action _changed;
    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private FileSignature _last;
    private long _burstUntil;
    private bool _disposed;

    public ExternalFileMonitor(string path, Func<ExternalFileSettings> settings, Action changed, bool useWatcher = true)
    {
        _path = Path.GetFullPath(path);
        _settings = settings;
        _changed = changed;
        _last = FileSignature.Read(_path);
        // 開いた直後は書き手が動いている最中のことが多いので、短い間隔から始める。
        _burstUntil = Environment.TickCount64 + settings().PollBurstMs;
        _timer = new Timer(_ => Poll());

        if (useWatcher)
            _watcher = TryCreateWatcher();

        lock (_gate) ScheduleNextPoll();
    }

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
            BeginBurst();
        }
        _changed();
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
            if (changed) BeginBurst();
            else ScheduleNextPoll();
        }
        if (changed) _changed();
    }

    // 呼び出し側で _gate を取っていること。
    private void BeginBurst()
    {
        _burstUntil = Environment.TickCount64 + _settings().PollBurstMs;
        ScheduleNextPoll();
    }

    private void ScheduleNextPoll()
        => _timer.Change(NextPollInterval(Environment.TickCount64, _burstUntil, _settings()), Timeout.InfiniteTimeSpan);

    public static TimeSpan NextPollInterval(long now, long burstUntil, ExternalFileSettings settings)
        => TimeSpan.FromMilliseconds(now < burstUntil ? settings.PollIntervalMs : settings.IdlePollIntervalMs);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _watcher?.Dispose();
        _timer.Dispose();
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
