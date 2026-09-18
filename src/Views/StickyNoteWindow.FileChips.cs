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
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ScreenPinNotes.Services;
using WpfImage = System.Windows.Controls.Image;
using WpfCursors = System.Windows.Input.Cursors;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace ScreenPinNotes.Views;

/// <summary>
/// 付箋に置いたファイルを、エクスプローラーと同じアイコンと名前の小さな札で見せる。
/// 画像は今までどおり本文に表示するので、ここに来るのは画像以外のファイルとフォルダー。
/// </summary>
/// <remarks>
/// 本文は FlowDocument なので、札は文字と同じ流れに並ぶ（<see cref="InlineUIContainer"/>）。
/// 開くのは本文のクリックを見ている <c>ContentBox_PreviewMouseDown</c>（.EditMode.cs）で、
/// リンクと同じシングルクリック。ダブルクリックにすると、開くより先に本文が
/// 編集モードに入ってしまう（そちらがダブルクリックの役目なので）。
/// </remarks>
public partial class StickyNoteWindow
{
    /// <summary>札のアイコンを本文の文字の何倍にするか。16px 相当に見えるあたり。</summary>
    private const double FileChipIconScale = 1.35;

    /// <summary>札が指しているもの。札の <see cref="FrameworkElement.Tag"/> に付けておく。</summary>
    private sealed record FileChipTarget(string Path, bool IsFolder);

    /// <summary>実行されると困る拡張子。開く前にひと声かける。</summary>
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".msi", ".msp", ".scr", ".cpl", ".hta", ".reg", ".lnk", ".url", ".jar",
    };

    private Inline CreateFileChip(MarkdownRenderer.MarkdownImage markdownImage, Inline fallback)
    {
        var resolved = ResolveImagePath(markdownImage.Target);
        if (resolved == null)
            return fallback;

        var isFolder = Directory.Exists(resolved);
        var exists = isFolder || File.Exists(resolved);
        var name = string.IsNullOrWhiteSpace(markdownImage.Alt)
            ? Path.GetFileName(resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : markdownImage.Alt;
        if (string.IsNullOrWhiteSpace(name))
            name = resolved;

        var fontSize = ContentBox.FontSize;
        var iconSize = Math.Round(fontSize * FileChipIconScale);
        var chip = new Border
        {
            Background = ViewModel.FileChipBackground,
            CornerRadius = new CornerRadius(Math.Round(fontSize * 0.35)),
            Padding = new Thickness(iconSize * 0.2, 0, iconSize * 0.35, 0),
            Cursor = WpfCursors.Hand,
            SnapsToDevicePixels = true,
            Tag = new FileChipTarget(resolved, isFolder),
        };

        var label = new TextBlock
        {
            Text = name,
            FontSize = fontSize,
            Foreground = ContentBox.Foreground,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (!exists)
        {
            // 無いものを黙って出さない。名前は残して、消えていることだけ見せる。
            label.TextDecorations = TextDecorations.Strikethrough;
            label.Opacity = 0.7;
        }

        var row = new StackPanel { Orientation = WpfOrientation.Horizontal };
        var icon = FileIcons.Get(resolved, isFolder);
        if (icon != null)
        {
            row.Children.Add(new WpfImage
            {
                Source = icon,
                Width = iconSize,
                Height = iconSize,
                Margin = new Thickness(0, 0, iconSize * 0.25, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = exists ? 1 : 0.55,
            });
        }
        row.Children.Add(label);
        chip.Child = row;

        chip.ToolTip = BuildFileChipToolTip(resolved, exists, IsInsideNoteAssets(resolved));

        // 札の高さのぶん行間が空くのを抑える（文字の中に収まって見えるように）。
        return new InlineUIContainer(chip) { BaselineAlignment = BaselineAlignment.Center };
    }

    /// <summary>押された場所が札の中なら、その札が指しているものを返す。</summary>
    private static bool TryGetFileChipAt(object? source, out FileChipTarget chip)
    {
        for (var element = source as DependencyObject; element != null; element = GetParentObject(element))
        {
            if (element is FrameworkElement { Tag: FileChipTarget found })
            {
                chip = found;
                return true;
            }
        }

        chip = null!;
        return false;
    }

    // ─── 右クリックメニュー ────────────────────────────────────────

    private FileChipTarget? _contextMenuFileChip;
    private MenuItem _openFileChipItem = null!;
    private MenuItem _openFileChipWithItem = null!;
    private MenuItem _editChipInDrawioItem = null!;
    private Separator _fileChipMenuSeparator = null!;

    /// <summary>
    /// 本文メニューの先頭へ差し込む札用の項目を作る。画像用の項目と同じく、
    /// 開くたびに <see cref="UpdateFileChipMenuItems"/> が右クリック先の札へ
    /// 向け直すので、ここでは器だけを1回作る。
    /// </summary>
    private IReadOnlyList<FrameworkElement> BuildFileChipMenuItems()
    {
        _openFileChipItem = new MenuItem { Header = LocalizationService.T("FileChipOpen") };
        _openFileChipItem.Click += (_, _) => WithContextMenuFileChip(c => OpenDroppedFile(c.Path, c.IsFolder));

        _openFileChipWithItem = new MenuItem { Header = LocalizationService.T("FileChipOpenWith") };
        _openFileChipWithItem.Click += (_, _) => WithContextMenuFileChip(c => OpenDroppedFileWith(c.Path));

        // .drawio などの図面を置いたときだけ出す。
        _editChipInDrawioItem = new MenuItem { Header = LocalizationService.T("EditInDrawio") };
        _editChipInDrawioItem.Click += (_, _) => WithContextMenuFileChip(c => EditInDrawio(c.Path));

        _fileChipMenuSeparator = new Separator();
        return [_openFileChipItem, _openFileChipWithItem, _editChipInDrawioItem, _fileChipMenuSeparator];
    }

    private void WithContextMenuFileChip(Action<FileChipTarget> action)
    {
        if (_contextMenuFileChip is { } chip) action(chip);
    }

    /// <summary>
    /// 右クリックがどの札に当たったかを覚える。画像と同じ理由で、
    /// ContextMenuOpening では押された要素が分からないので押した時点で拾う。
    /// </summary>
    private void CaptureContextMenuFileChip(object? originalSource)
        => _contextMenuFileChip = TryGetFileChipAt(originalSource, out var chip) ? chip : null;

    /// <summary>札の上で開いたときだけ、札用の項目を出す。</summary>
    private void UpdateFileChipMenuItems(bool fromKeyboard)
    {
        // キーボードから開いたときは直前の右クリックの記憶が残っているだけなので捨てる。
        if (fromKeyboard) _contextMenuFileChip = null;
        var visibility = _contextMenuFileChip == null ? Visibility.Collapsed : Visibility.Visible;
        _openFileChipItem.Visibility = visibility;
        _openFileChipWithItem.Visibility = visibility;
        _fileChipMenuSeparator.Visibility = visibility;
        _editChipInDrawioItem.Visibility =
            _contextMenuFileChip is { IsFolder: false } drawioCandidate && CanEditInDrawio(drawioCandidate.Path)
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (_contextMenuFileChip is not { } chip) return;

        // フォルダーと、もう無いファイルには選ぶアプリが無い。
        _openFileChipWithItem.IsEnabled = !chip.IsFolder && File.Exists(chip.Path);
    }

    /// <summary>Windows の「プログラムから開く」を出す。開くアプリを選びたいとき用。</summary>
    private void OpenDroppedFileWith(string path)
    {
        if (!File.Exists(path))
        {
            WpfMessageBox.Show(this,
                string.Format(LocalizationService.T("FileChipMissingMessage"), path),
                LocalizationService.T("FileChipOpenTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            // エクスプローラーの「プログラムから開く」と同じ入口。
            // ShellExecute の "openas" 動詞は、その拡張子に openas が登録されて
            // いないと「関連付けがありません」(1155) で失敗するので使わない
            // （.txt でも実際に失敗した）。引数は引用符で囲まない。
            // OpenAs_RunDLL はコマンドラインの残り全部をパスとして読むので、
            // 空白を含む場所もそのまま渡せる。
            // 開くアプリは本人が選ぶので、実行できる種類でも確認は挟まない。
            Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {path}")
            {
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Show the open-with dialog", ex);
        }
    }

    private string BuildFileChipToolTip(string path, bool exists, bool inAssets)
    {
        var state = !exists
            ? LocalizationService.T("FileChipMissing")
            : inAssets
                ? LocalizationService.T("FileChipCopyHint")
                : LocalizationService.T("FileChipLinkHint");
        return $"{path}\n{state}";
    }

    /// <summary>付箋の assets の中にあるファイルか（＝この付箋が持っているコピー）。</summary>
    private bool IsInsideNoteAssets(string fullPath)
    {
        var assetsRoot = Path.GetFullPath(_storage.GetNoteAssetsDirectoryPath(ViewModel.Model.Id))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return Path.GetFullPath(fullPath).StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase);
    }

    private void OpenDroppedFile(string path, bool isFolder)
    {
        if (!isFolder && !File.Exists(path))
        {
            WpfMessageBox.Show(this,
                string.Format(LocalizationService.T("FileChipMissingMessage"), path),
                LocalizationService.T("FileChipOpenTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 付箋の中身は同期フォルダーや外部ファイルから来ることもある。
        // 実行されうるものだけは、開く前に本人の意思を確かめる。
        if (!isFolder && ExecutableExtensions.Contains(Path.GetExtension(path)) &&
            WpfMessageBox.Show(this,
                string.Format(LocalizationService.T("FileChipExecutableConfirm"), path),
                LocalizationService.T("FileChipOpenTitle"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        OpenTarget(path);
    }
}
