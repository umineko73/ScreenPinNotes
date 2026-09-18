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
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using SkiaSharp;
using WpfBrushes     = System.Windows.Media.Brushes;
using WpfButton      = System.Windows.Controls.Button;
using WpfBitmapImage = System.Windows.Media.Imaging.BitmapImage;
using WpfCheckBox    = System.Windows.Controls.CheckBox;
using WpfColor       = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfCursors     = System.Windows.Input.Cursors;
using WpfDataFormats = System.Windows.DataFormats;
using WpfFontFamily  = System.Windows.Media.FontFamily;
using WpfImage       = System.Windows.Controls.Image;
using WpfListBox     = System.Windows.Controls.ListBox;
using WpfSolidBrush  = System.Windows.Media.SolidColorBrush;


namespace ScreenPinNotes.Views;

public partial class StickyNoteWindow
{
    // ─── 貼り付け（リンク検出付き） ──────────────────────────────

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (IsContentReadOnly())
        {
            e.CancelCommand();
            ShowSizeOverlay(LocalizationService.T("EditLockNotice"));
            return;
        }

        if (sender == BodyEditBox)
        {
            e.CancelCommand();
            PasteFromDataObject(e.DataObject);
            return;
        }

        PasteFromDataObject(e.DataObject);
        e.CancelCommand();
    }

    private void PasteFromClipboard()
    {
        if (IsContentReadOnly())
        {
            ShowSizeOverlay(LocalizationService.T("EditLockNotice"));
            return;
        }

        PasteFromDataObject(System.Windows.Clipboard.GetDataObject());
    }

    private void PasteFromDataObject(System.Windows.IDataObject dataObject)
    {
        // エクスプローラーでコピーしたファイルは、画像データより先に見て元の形式のまま取り込む。
        if (TryGetDroppedPaths(dataObject, out var pastedPaths))
        {
            InsertDroppedPaths(pastedPaths);
            return;
        }

        if (TryGetPastedImage(dataObject, out var image))
        {
            // すでに編集中だった場合はそのまま編集モードを維持する
            // （そうしないと編集途中の内容が閲覧モードへ切り替わって失われる）。
            var wasEditing = IsBodyEditing();
            if (!wasEditing)
                EnterEditMode();

            var relativePath = SavePastedImage(image);
            var markdown = BuildImageMarkdown(relativePath);
            InsertTextAtSelection(markdown);
            if (!wasEditing)
                EnterViewMode();
            return;
        }

        if (!dataObject.GetDataPresent(WpfDataFormats.UnicodeText)) return;

        if (!IsBodyEditing())
            EnterEditMode();

        if (!TryGetClipboardText(dataObject, out var clipboardText)) return;
        InsertTextAtSelection(clipboardText.TrimEnd('\n'));
    }

    // ─── ファイルの貼り付け・ドロップ ──────────────────────────────
    // エクスプローラーでコピー・ドラッグしたファイルは、元のファイルに触れずに付箋の assets へ
    // 元の形式のままコピーして参照する（PNG に変換して保存する画像データの貼り付けとは別の経路）。
    // 画像はそのまま付箋に表示し、それ以外はアイコンとして置く。Shift を押しながら落とすと
    // コピーせず元の場所を指す（フォルダーは必ずそちら）。

    private static bool TryGetDroppedPaths(
        System.Windows.IDataObject? dataObject,
        out IReadOnlyList<string> paths)
    {
        paths = [];
        try
        {
            if (dataObject == null || !dataObject.GetDataPresent(WpfDataFormats.FileDrop))
                return false;

            paths = ImageAssetImport.GetDroppedPaths(dataObject.GetData(WpfDataFormats.FileDrop) as string[]);
            return paths.Count > 0;
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// 落とされた・貼り付けられたファイルを本文に入れる。画像はそのまま表示され、
    /// それ以外はアイコンとして置かれる（どちらも Markdown の画像記法で書く）。
    /// <paramref name="asLink"/> のときはコピーせず元の場所を指す。
    /// </summary>
    private void InsertDroppedPaths(IReadOnlyList<string> paths, int? insertionIndex = null, bool asLink = false)
    {
        if (IsContentReadOnly())
        {
            ShowSizeOverlay(LocalizationService.T("EditLockNotice"));
            return;
        }

        var assetsDir = _storage.GetNoteAssetsDirectoryPath(ViewModel.Model.Id);
        var images = new List<string>();
        foreach (var path in paths)
        {
            // フォルダーは中身ごと持ってくると事故になりやすいので、必ず元の場所を指す。
            if (asLink || Directory.Exists(path))
            {
                images.Add(BuildFileMarkdown(Path.GetFileName(path.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), path));
                continue;
            }

            try
            {
                var isImage = LinkDetector.IsRenderableImageTarget(path);
                var name = ImageAssetImport.CopyIntoAssets(path, assetsDir, isImage ? "image" : "file");
                images.Add(isImage
                    ? $"![{Path.GetFileNameWithoutExtension(name)}](assets/{name})"
                    : BuildFileMarkdown(Path.GetFileName(path), $"assets/{name}"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ErrorReporter.ReportNonFatal("Copy a dropped file into note assets", ex);
                ShowSizeOverlay(LocalizationService.T("ImageImportFailed"));
            }
        }

        if (images.Count == 0)
            return;

        // 画像データの貼り付けと同じく、編集中でなければ一時的に編集モードにして末尾へ入れる。
        var wasEditing = IsBodyEditing();
        if (!wasEditing)
            EnterEditMode();
        else if (insertionIndex is { } index)
            BodyEditBox.Select(Math.Clamp(index, 0, BodyEditBox.Text.Length), 0);

        InsertTextAtSelection(BuildBlockMarkdown(string.Join("\n", images)));
        if (!wasEditing)
            EnterViewMode();
    }

    private static string BuildFileMarkdown(string displayName, string target)
        => ImageAssetImport.BuildFileMarkdown(displayName, target);

    private void OnImageFileDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        OnImageFileDragOver(sender, e);
        if (!e.Handled || ViewModel.IsFolded)
            return;
        if (IsContentReadOnly())
            ShowSizeOverlay(LocalizationService.T("EditLockNotice"));
        else
            ShowSizeOverlay(LocalizationService.T(
                IsLinkDrop(e) ? "FileDropAsLinkNotice" : "FileDropAsCopyNotice"));
    }

    private void OnImageFileDragOver(object sender, System.Windows.DragEventArgs e)
    {
        // ファイル以外のドラッグ（本文内の文字の移動など）は、これまでどおり各コントロールに任せる。
        if (!TryGetDroppedPaths(e.Data, out _))
            return;

        e.Effects = ViewModel.IsFolded || IsContentReadOnly() ||
                    !e.AllowedEffects.HasFlag(System.Windows.DragDropEffects.Copy)
            ? System.Windows.DragDropEffects.None
            : System.Windows.DragDropEffects.Copy;
        e.Handled = true;
    }

    /// <summary>Shift を押しながら落としたか。コピーせず元の場所を指す合図。</summary>
    private static bool IsLinkDrop(System.Windows.DragEventArgs e)
        => e.KeyStates.HasFlag(System.Windows.DragDropKeyStates.ShiftKey);

    private void OnImageFileDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!TryGetDroppedPaths(e.Data, out var files))
            return;

        e.Handled = true;
        // Report Copy at completion too: the source may delete its original if
        // a Move effect survives from the incoming OLE drop event.
        e.Effects = System.Windows.DragDropEffects.None;
        if (ViewModel.IsFolded || IsContentReadOnly() ||
            !e.AllowedEffects.HasFlag(System.Windows.DragDropEffects.Copy))
            return;

        e.Effects = System.Windows.DragDropEffects.Copy;

        // 編集中はドロップした行の直後に入れる（行の途中で分けない）。表示中は末尾に入れる。
        int? insertionIndex = null;
        if (IsBodyEditing())
        {
            var index = BodyEditBox.GetCharacterIndexFromPoint(e.GetPosition(BodyEditBox), snapToText: true);
            insertionIndex = TextInsertion.GetLineEnd(BodyEditBox.Text, index < 0 ? BodyEditBox.Text.Length : index);
        }

        InsertDroppedPaths(files, insertionIndex, IsLinkDrop(e));
    }

    private static bool TryGetClipboardText(
        System.Windows.IDataObject dataObject,
        out string text)
    {
        text = "";
        if (!dataObject.GetDataPresent(WpfDataFormats.UnicodeText))
            return false;

        if (dataObject.GetData(WpfDataFormats.UnicodeText) is not string rawText)
            return false;

        text = NormalizeLineEndings(rawText).TrimEnd('\n');
        return text.Length > 0;
    }

    private void InsertTextAtSelection(string text)
    {
        if (IsContentReadOnly())
            return;

        text = NormalizeLineEndings(text);
        if (IsBodyEditing())
        {
            var start = BodyEditBox.SelectionStart;
            var length = BodyEditBox.SelectionLength;
            var inserted = TextInsertion.InsertAtSelection(BodyEditBox.Text, start, length, text);
            if (!CanAcceptNoteContent(inserted.Text))
            {
                ShowSizeOverlay(string.Format(
                    LocalizationService.T("NoteContentTooLarge"),
                    FormatByteSize(Settings.MaxNoteContentBytes)));
                return;
            }

            BodyEditBox.Text = inserted.Text;
            BodyEditBox.Select(inserted.CaretIndex, 0);
            return;
        }

        var plainText = GetPlainText();
        var startOff  = GetOffsetOfPointer(ContentBox.Selection.Start);
        var endOff    = GetOffsetOfPointer(ContentBox.Selection.End);
        var insertedText = TextInsertion.InsertAtSelection(plainText, startOff, endOff - startOff, text);
        if (!TrySetNoteContent(insertedText.Text))
            return;

        LoadPlainContent(insertedText.Text);
        RestoreCaretAt(insertedText.CaretIndex);
    }

    private void PasteExcelTable_Click(object sender, RoutedEventArgs e)
        => PasteExcelTable(useFirstRowAsHeader: true);

    private void PasteExcelTableWithoutHeader_Click(object sender, RoutedEventArgs e)
        => PasteExcelTable(useFirstRowAsHeader: false);

    private void PasteExcelTable(bool useFirstRowAsHeader)
    {
        if (IsContentReadOnly())
            return;

        if (!TryGetClipboardText(out var clipboard)) return;
        if (!MarkdownTableClipboard.TryTabularTextToMarkdownTable(clipboard, useFirstRowAsHeader, out var markdownTable))
            return;

        if (!_isEditMode)
            EnterEditMode();
        InsertTextAtSelection(BuildBlockMarkdown(markdownTable));
    }

    private void CopyExcelTable_Click(object sender, RoutedEventArgs e)
    {
        var selectedText = IsBodyEditing()
            ? BodyEditBox.SelectedText.Replace("\r\n", "\n").Replace("\r", "\n").Trim()
            : ContentBox.Selection.IsEmpty
                ? ""
                : ContentBox.Selection.Text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        if (!MarkdownTableClipboard.TryCopyableTableTextToTabularText(selectedText, out var tabularText))
            return;

        TrySetClipboardText(tabularText);
    }

    private static bool TryGetClipboardText(out string text)
    {
        text = "";
        try
        {
            if (!System.Windows.Clipboard.ContainsText())
                return false;

            text = System.Windows.Clipboard.GetText();
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool ClipboardHasImage()
    {
        try
        {
            return System.Windows.Clipboard.ContainsImage() ||
                System.Windows.Clipboard.ContainsData("PNG") ||
                System.Windows.Clipboard.ContainsData("image/png");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// 画像を「絵」としてクリップボードへ置く。貼り付け先に合わせて2つの形で渡す
    /// ――多くのアプリが読む昔からのビットマップと、透明を保てる PNG。
    /// 付箋を閉じても貼れるよう、クリップボードへ預けきる（copy: true）。
    /// </summary>
    private bool TrySetClipboardImage(string imagePath)
    {
        try
        {
            System.Windows.Clipboard.SetDataObject(
                BuildImageDataObject(GetOrLoadNormalizedImage(imagePath)), copy: true);
            return true;
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException
            or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// 画像ファイルそのものをクリップボードへ置く。エクスプローラーやメールに
    /// そのまま貼れる。切り取りと間違われないよう、貼り付け方は「コピー」を指定する。
    /// </summary>
    private static bool TrySetClipboardFile(string path)
    {
        try
        {
            System.Windows.Clipboard.SetDataObject(BuildFileDataObject(path), copy: true);
            return true;
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException
            or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// 絵として渡すときの中身。貼り付け先に合わせて2つの形を載せる
    /// ――多くのアプリが読む昔からのビットマップと、透明を保てる PNG。
    /// </summary>
    private static System.Windows.DataObject BuildImageDataObject(
        System.Windows.Media.Imaging.BitmapSource bitmap)
    {
        var data = new System.Windows.DataObject();
        data.SetImage(bitmap);

        var png = new MemoryStream();
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        encoder.Save(png);
        png.Position = 0;
        data.SetData("PNG", png);
        return data;
    }

    /// <summary>ファイルとして渡すときの中身。</summary>
    private static System.Windows.DataObject BuildFileDataObject(string path)
    {
        var data = new System.Windows.DataObject();
        data.SetFileDropList([path]);
        // DROPEFFECT_COPY。これが無いと、貼り付け先によっては移動と受け取られる。
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1u)));
        return data;
    }

    private static bool TrySetClipboardText(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // 汎用の改行正規化。編集内容の読み込みや貼り付け処理から幅広く使われるため、
    // テーブル変換専用の MarkdownTableClipboard には含めていない。
    private static string NormalizeLineEndings(string text)
        => text.Replace("\r\n", "\n").Replace("\r", "\n");

    private string BuildBlockMarkdown(string markdown)
    {
        var plainText = IsBodyEditing()
            ? NormalizeLineEndings(BodyEditBox.Text)
            : GetPlainText();
        var startOff = IsBodyEditing()
            ? BodyEditBox.SelectionStart
            : GetOffsetOfPointer(ContentBox.Selection.Start);
        var endOff = IsBodyEditing()
            ? BodyEditBox.SelectionStart + BodyEditBox.SelectionLength
            : GetOffsetOfPointer(ContentBox.Selection.End);
        return TextInsertion.BuildBlockInsertion(plainText, startOff, endOff - startOff, markdown);
    }

    private static bool TryGetPastedImage(
        System.Windows.IDataObject dataObject,
        out System.Windows.Media.Imaging.BitmapSource image)
    {
        // Bitmap の相互変換では透過が失われることがある。コピー時に添えた
        // PNG があれば、そのアルファを持つピクセルを優先して読む。
        if (TryGetPastedPng(dataObject, out image))
            return true;

        image = null!;
        if (!dataObject.GetDataPresent(WpfDataFormats.Bitmap, autoConvert: true))
            return false;

        var data = dataObject.GetData(WpfDataFormats.Bitmap, autoConvert: true);
        if (data is System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            image = NormalizeZeroAlphaImage(bitmap);
            return true;
        }

        if (data is System.Drawing.Bitmap drawingBitmap)
        {
            using (drawingBitmap)
            {
                image = NormalizeZeroAlphaImage(ConvertDrawingBitmapToBitmapSource(drawingBitmap));
            }
            return true;
        }

        try
        {
            var clipboardImage = System.Windows.Clipboard.GetImage();
            if (clipboardImage != null)
            {
                image = NormalizeZeroAlphaImage(clipboardImage);
                return true;
            }
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return false;
    }

    private static bool TryGetPastedPng(System.Windows.IDataObject dataObject,
        out System.Windows.Media.Imaging.BitmapSource image)
    {
        image = null!;
        foreach (var format in new[] { "PNG", "image/png" })
        {
            try
            {
                if (!dataObject.GetDataPresent(format, autoConvert: false)) continue;
                var data = dataObject.GetData(format, autoConvert: false);
                using var bytesStream = data is byte[] bytes ? new MemoryStream(bytes, writable: false) : null;
                var stream = data as Stream ?? bytesStream;
                if (stream == null || !stream.CanRead) continue;
                var position = stream.CanSeek ? stream.Position : (long?)null;
                try
                {
                    if (position.HasValue) stream.Position = 0;
                    var decoder = new System.Windows.Media.Imaging.PngBitmapDecoder(stream,
                        System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                        System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    image = decoder.Frames[0];
                    image.Freeze();
                    return true;
                }
                finally
                {
                    // IDataObject が所有するストリームは閉じず、次の貼り付けにも使えるようにする。
                    if (position.HasValue) stream.Position = position.Value;
                }
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException
                or InvalidOperationException or ExternalException)
            {
                // PNG が壊れていても、別形式や従来の Bitmap で貼り付けられる。
            }
        }
        return false;
    }

    private static System.Windows.Media.Imaging.BitmapSource ConvertDrawingBitmapToBitmapSource(
        System.Drawing.Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// クリップボードから新しく作る付箋の本文。まだウィンドウの無い付箋のために、
    /// エディタを通さず Markdown を組み立て、画像はその付箋の assets へ保存する。
    /// 貼り付けと違って文字を画像より先に見る。Excel や Word は文字と一緒に
    /// 選択範囲の絵も載せるので、画像を先に見るとそちらが付箋になってしまう。
    /// </summary>
    public static bool TryBuildClipboardNoteContent(
        System.Windows.IDataObject dataObject, StorageService storage, string noteId, out string content)
    {
        content = "";
        try
        {
            var assetsDir = storage.GetNoteAssetsDirectoryPath(noteId);
            if (TryGetDroppedPaths(dataObject, out var copiedPaths))
            {
                var images = new List<string>();
                foreach (var path in copiedPaths)
                {
                    if (Directory.Exists(path))
                    {
                        images.Add(BuildFileMarkdown(Path.GetFileName(path.TrimEnd(
                            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), path));
                        continue;
                    }

                    try
                    {
                        var isImage = LinkDetector.IsRenderableImageTarget(path);
                        var name = ImageAssetImport.CopyIntoAssets(path, assetsDir, isImage ? "image" : "file");
                        images.Add(isImage
                            ? $"![{Path.GetFileNameWithoutExtension(name)}](assets/{name})"
                            : BuildFileMarkdown(Path.GetFileName(path), $"assets/{name}"));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        ErrorReporter.ReportNonFatal("Copy a file into note assets", ex);
                    }
                }
                content = string.Join("\n", images);
                return images.Count > 0;
            }

            if (TryGetClipboardText(dataObject, out var text) && !string.IsNullOrWhiteSpace(text))
            {
                content = text;
                return true;
            }

            if (TryGetPastedImage(dataObject, out var image))
            {
                content = $"![image]({SavePastedImage(image, assetsDir)})";
                return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or IOException)
        {
            ErrorReporter.ReportNonFatal("Build a note from the clipboard", ex);
            content = "";
            return false;
        }
    }

    private string SavePastedImage(System.Windows.Media.Imaging.BitmapSource image)
        => SavePastedImage(image, _storage.GetNoteAssetsDirectoryPath(ViewModel.Model.Id));

    private static string SavePastedImage(System.Windows.Media.Imaging.BitmapSource image, string assetsDir)
    {
        Directory.CreateDirectory(assetsDir);

        var fileName = $"image-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.png";
        var path = Path.Combine(assetsDir, fileName);

        // PNG のアルファはそのまま保存する。全画素が透明でも不透明化しない。
        // 古い Bitmap の未設定アルファの補正は、読み取り時だけ行う。
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using (var stream = File.Create(path))
            encoder.Save(stream);

        return $"assets/{fileName}";
    }

    private static System.Windows.Media.Imaging.BitmapSource NormalizeZeroAlphaImage(
        System.Windows.Media.Imaging.BitmapSource image)
    {
        var source = image.Format == System.Windows.Media.PixelFormats.Bgra32
            ? image
            : new System.Windows.Media.Imaging.FormatConvertedBitmap(
                image,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                0);

        var stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);

        var hasNonZeroAlpha = false;
        var hasRgbContent = false;
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            hasNonZeroAlpha |= pixels[i + 3] != 0;
            hasRgbContent |= pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0;
            if (hasNonZeroAlpha && hasRgbContent)
                break;
        }

        if (hasNonZeroAlpha || !hasRgbContent)
            return image;

        for (var i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;

        var normalized = System.Windows.Media.Imaging.BitmapSource.Create(
            source.PixelWidth,
            source.PixelHeight,
            source.DpiX,
            source.DpiY,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        normalized.Freeze();
        return normalized;
    }

    private string BuildImageMarkdown(string relativePath)
    {
        var plainText = IsBodyEditing()
            ? NormalizeLineEndings(BodyEditBox.Text)
            : GetPlainText();
        var startOff = IsBodyEditing()
            ? BodyEditBox.SelectionStart
            : GetOffsetOfPointer(ContentBox.Selection.Start);
        var endOff = IsBodyEditing()
            ? BodyEditBox.SelectionStart + BodyEditBox.SelectionLength
            : GetOffsetOfPointer(ContentBox.Selection.End);
        return TextInsertion.BuildBlockInsertion(plainText, startOff, endOff - startOff, $"![image]({relativePath})");
    }

    // TextPointer が指す位置の、GetPlainText() が返す文字列上での文字オフセットを求める。
    //
    // 以前は TextRange(from, to).Text を直接使っていたが、WPF の TextRange.Text は
    // 範囲の終端が段落境界と一致するかどうかで末尾の改行の有無が不安定になる
    // （終端が文書末尾かどうか等で余分な改行が付いたり付かなかったりする）。
    // GetPlainText() と同じ辿り方（Run/Hyperlink/LineBreak の順に長さを積み上げる）を
    // することで、その揺れを避けて GetPlainText() の結果と常に一致するオフセットを得る。
    private int GetOffsetOfPointer(TextPointer target)
    {
        int pos = 0;
        bool firstPara = true;
        foreach (Block block in ContentBox.Document.Blocks)
        {
            if (!firstPara) pos++;
            firstPara = false;

            if (block is not Paragraph para) continue;

            bool targetInThisPara =
                target.CompareTo(para.ContentStart) >= 0 &&
                target.CompareTo(para.ContentEnd) <= 0;

            foreach (Inline inline in para.Inlines)
            {
                int len = inline switch
                {
                    Run r                              => r.Text.Length,
                    Hyperlink h when h.Tag is string t => t.Length,
                    LineBreak                          => 1,
                    _ => new TextRange(inline.ContentStart, inline.ContentEnd).Text.Length,
                };

                if (targetInThisPara && target.CompareTo(inline.ContentEnd) <= 0)
                {
                    if (target.CompareTo(inline.ContentStart) <= 0)
                        return pos;

                    var within = new TextRange(inline.ContentStart, target).Text
                        .Replace("\r\n", "\n").Replace("\r", "\n").Length;
                    return pos + Math.Min(within, len);
                }

                pos += len;
            }

            if (targetInThisPara) return pos;
        }
        return pos;
    }

    private void RestoreCaretAt(int target)
    {
        int pos = 0;
        bool firstPara = true;
        foreach (Block block in ContentBox.Document.Blocks)
        {
            if (!firstPara)
            {
                if (pos == target)
                {
                    ContentBox.CaretPosition =
                        block.ContentStart.GetInsertionPosition(LogicalDirection.Forward)
                        ?? ContentBox.Document.ContentEnd;
                    return;
                }
                pos++;
            }
            firstPara = false;

            if (block is Paragraph para)
            {
                foreach (Inline inline in para.Inlines)
                {
                    int len = inline switch
                    {
                        Run r                              => r.Text.Length,
                        Hyperlink h when h.Tag is string t => t.Length,
                        LineBreak                          => 1,
                        _                                  => 0,
                    };
                    if (pos + len >= target)
                    {
                        var tp = inline.ContentStart;
                        for (int i = 0; i < target - pos; i++)
                            tp = tp.GetNextInsertionPosition(LogicalDirection.Forward) ?? tp;
                        ContentBox.CaretPosition = tp;
                        return;
                    }
                    pos += len;
                }
            }
        }
        ContentBox.CaretPosition = ContentBox.Document.ContentEnd;
    }

}
