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
public partial class StickyNoteWindow
{
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

    }
}
