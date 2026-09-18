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
using System.Windows;
using ScreenPinNotes.Services;
using WpfMessageBox = System.Windows.MessageBox;

namespace ScreenPinNotes.Views;

/// <summary>
/// 付箋に貼った図を draw.io で編集する。draw.io は図の XML を PNG の中に
/// 忍ばせて保存するので、貼ってある画像がそのまま編集できる図面でもある。
/// </summary>
/// <remarks>
/// 編集して保存されると同じ PNG が描き直される。付箋は開いたままのことが多いので、
/// 保存に気付いて貼り直せるよう、開いたファイルを <see cref="ExternalFileMonitor"/> で
/// 見張る。外部ファイル付箋と同じ仕組みで、draw.io がファイルを開いたままでも
/// 更新を取りこぼさない。
/// </remarks>
public partial class StickyNoteWindow
{
    private readonly Dictionary<string, ExternalFileMonitor> _drawioWatches =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>draw.io で開ける図か（メニューに項目を出すかの判断）。</summary>
    private bool CanEditInDrawio(string? fullPath)
        => fullPath != null && File.Exists(fullPath) && DrawioFiles.IsDiagram(fullPath);

    private void EditInDrawio(string fullPath)
    {
        var executable = DrawioFiles.FindExecutable(Settings.DrawioPath);
        if (executable == null)
        {
            WpfMessageBox.Show(this,
                LocalizationService.T("DrawioNotFound"),
                LocalizationService.T("EditInDrawio"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(executable, $"\"{fullPath}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Open a diagram in draw.io", ex);
            return;
        }

        WatchEditedDiagram(fullPath);
    }

    /// <summary>
    /// draw.io へ渡したファイルを見張り、保存されたら貼り直す。付箋を閉じるまで
    /// 見張り続けるのは、draw.io を開いたまま何度も保存されるため。
    /// </summary>
    private void WatchEditedDiagram(string fullPath)
    {
        if (_isClosed || _drawioWatches.ContainsKey(fullPath))
            return;

        try
        {
            var settings = Settings;
            _drawioWatches[fullPath] = new ExternalFileMonitor(
                fullPath,
                () => settings.ExternalFile,
                // 知らせはワーカースレッドから届く。
                () => Dispatcher.BeginInvoke(new Action(ReloadEditedDiagram)));
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Watch a diagram edited in draw.io", ex);
        }
    }

    /// <summary>
    /// 描き直された図を貼り直す。画像はパスと更新日時で覚えているので、
    /// 本文を読み込み直せば新しい絵に入れ替わる。
    /// </summary>
    private void ReloadEditedDiagram()
    {
        // 編集モードは生の Markdown を見せている最中で、閲覧へ戻るときに
        // どのみち読み込み直す。折りたたみ中も、開くときに読み直される。
        if (_isClosed || _isEditMode || ViewModel.IsFolded)
            return;

        LoadContent(ViewModel.Content);
    }

    private void DisposeDrawioWatches()
    {
        foreach (var watch in _drawioWatches.Values)
            watch.Dispose();
        _drawioWatches.Clear();
    }
}
