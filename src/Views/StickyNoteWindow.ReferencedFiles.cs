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
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Views;

public partial class StickyNoteWindow
{
    private readonly Dictionary<string, ExternalFileMonitor> _referencedFileWatches =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _referencedFilesInRender = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _referencedFileReloadTimer;

    // 描画で参照したファイルは、起動した編集アプリに関係なく通知だけで監視する。
    // 同じファイルが本文に何度現れても、この付箋では1つの監視にまとめる。
    private void WatchReferencedFile(string path)
    {
        if (_isClosed) return;
        var fullPath = Path.GetFullPath(path);
        _referencedFilesInRender.Add(fullPath);
        if (_referencedFileWatches.ContainsKey(fullPath)) return;
        try
        {
            _referencedFileWatches[fullPath] = new ExternalFileMonitor(
                fullPath, () => Settings.ExternalFile, QueueReferencedFileReload, usePolling: false);
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Watch a referenced file", ex);
        }
    }

    private void PruneReferencedFileWatches()
    {
        foreach (var path in _referencedFileWatches.Keys.Where(p => !_referencedFilesInRender.Contains(p)).ToArray())
        {
            _referencedFileWatches[path].Dispose();
            _referencedFileWatches.Remove(path);
        }
    }

    private void QueueReferencedFileReload()
    {
        try
        {
            if (_uiDispatcher.HasShutdownStarted || _uiDispatcher.HasShutdownFinished) return;
            _uiDispatcher.BeginInvoke(new Action(() =>
            {
                if (_isClosed) return;
                _expandedContentValid = false;
                // 保存時の削除・作成・変更の連続通知を、最後の通知から200ms後にまとめる。
                _referencedFileReloadTimer ??= new DispatcherTimer(
                    TimeSpan.FromMilliseconds(200), DispatcherPriority.Background,
                    (_, _) => ReloadReferencedFiles(), _uiDispatcher);
                _referencedFileReloadTimer.Stop();
                _referencedFileReloadTimer.Start();
            }));
        }
        catch (InvalidOperationException) { }
    }

    private void ReloadReferencedFiles()
    {
        _referencedFileReloadTimer?.Stop();
        if (_isClosed) return;
        // 同じ更新日時で置換された画像も、変更通知が来たら読み直す。
        _normalizedImageCache.Clear();
        if (_isEditMode || ViewModel.IsFolded) return;
        var x = ContentBox.HorizontalOffset;
        var y = ContentBox.VerticalOffset;
        LoadContent(ViewModel.Content);
        ContentBox.ScrollToHorizontalOffset(x);
        ContentBox.ScrollToVerticalOffset(y);
    }

    private void DisposeReferencedFileWatches()
    {
        _referencedFileReloadTimer?.Stop();
        foreach (var watch in _referencedFileWatches.Values) watch.Dispose();
        _referencedFileWatches.Clear();
    }
}
