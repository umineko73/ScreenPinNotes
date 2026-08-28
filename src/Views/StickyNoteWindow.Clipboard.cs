// ScreenStickyNotes - a desktop sticky notes app for Windows 11
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
using ScreenStickyNotes.Models;
using ScreenStickyNotes.Services;
using ScreenStickyNotes.ViewModels;
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


namespace ScreenStickyNotes.Views;

public partial class StickyNoteWindow
{
    // ─── 貼り付け（リンク検出付き） ──────────────────────────────

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (TryGetPastedImage(e.DataObject, out var image))
        {
            e.CancelCommand();
            if (!_isEditMode || ContentBox.IsReadOnly)
                EnterEditMode();

            var relativePath = SavePastedImage(image);
            var markdown = BuildImageMarkdown(relativePath);
            InsertTextAtSelection(markdown);
            EnterViewMode();
            return;
        }

        if (!e.DataObject.GetDataPresent(WpfDataFormats.UnicodeText)) return;
        e.CancelCommand();

        if (!_isEditMode || ContentBox.IsReadOnly)
            EnterEditMode();

        if (!TryGetClipboardText(e.DataObject, out var clipboardText)) return;
        InsertTextAtSelection(clipboardText.TrimEnd('\n'));
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
        text = NormalizeLineEndings(text);
        var plainText = GetPlainText();
        var startOff  = GetOffsetOfPointer(ContentBox.Selection.Start);
        var endOff    = GetOffsetOfPointer(ContentBox.Selection.End);
        var beforeText = plainText[..startOff];
        var afterText  = plainText[endOff..];
        var newText    = beforeText + text + afterText;
        var caretOff   = beforeText.Length + text.Length;

        LoadPlainContent(newText);
        RestoreCaretAt(caretOff);

        ViewModel.Content = newText;
        RequestSave();
    }

    private void PasteExcelTable_Click(object sender, RoutedEventArgs e)
        => PasteExcelTable(useFirstRowAsHeader: true);

    private void PasteExcelTableWithoutHeader_Click(object sender, RoutedEventArgs e)
        => PasteExcelTable(useFirstRowAsHeader: false);

    private void PasteExcelTable(bool useFirstRowAsHeader)
    {
        if (!TryGetClipboardText(out var clipboard)) return;
        if (!MarkdownTableClipboard.TryTabularTextToMarkdownTable(clipboard, useFirstRowAsHeader, out var markdownTable))
            return;

        if (!_isEditMode)
            EnterEditMode();
        InsertTextAtSelection(BuildBlockMarkdown(markdownTable));
    }

    private void CopyExcelTable_Click(object sender, RoutedEventArgs e)
    {
        var selectedText = ContentBox.Selection.IsEmpty
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
        var plainText = GetPlainText();
        var startOff = GetOffsetOfPointer(ContentBox.Selection.Start);
        var endOff = GetOffsetOfPointer(ContentBox.Selection.End);
        var prefix = startOff > 0 && plainText[startOff - 1] != '\n' ? "\n" : "";
        var suffix = endOff < plainText.Length && plainText[endOff] != '\n' ? "\n" : "";
        return $"{prefix}{markdown}{suffix}";
    }

    private static bool TryGetPastedImage(
        System.Windows.IDataObject dataObject,
        out System.Windows.Media.Imaging.BitmapSource image)
    {
        image = null!;
        if (!dataObject.GetDataPresent(WpfDataFormats.Bitmap, autoConvert: true))
            return false;

        var bitmap = dataObject.GetData(WpfDataFormats.Bitmap, autoConvert: true)
            as System.Windows.Media.Imaging.BitmapSource;
        if (bitmap != null)
        {
            image = bitmap;
            return true;
        }

        if (dataObject.GetData(WpfDataFormats.Bitmap, autoConvert: true) is System.Drawing.Bitmap drawingBitmap)
        {
            image = ConvertDrawingBitmapToBitmapSource(drawingBitmap);
            return true;
        }

        try
        {
            var clipboardImage = System.Windows.Clipboard.GetImage();
            if (clipboardImage != null)
            {
                image = clipboardImage;
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

    private string SavePastedImage(System.Windows.Media.Imaging.BitmapSource image)
    {
        var assetsDir = _storage.GetNoteAssetsDirectoryPath(ViewModel.Model.Id);
        Directory.CreateDirectory(assetsDir);

        var fileName = $"image-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.png";
        var path = Path.Combine(assetsDir, fileName);

        image = NormalizeZeroAlphaImage(image);
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
        var plainText = GetPlainText();
        var startOff = GetOffsetOfPointer(ContentBox.Selection.Start);
        var endOff = GetOffsetOfPointer(ContentBox.Selection.End);
        var prefix = startOff > 0 && plainText[startOff - 1] != '\n' ? "\n" : "";
        var suffix = endOff < plainText.Length && plainText[endOff] != '\n' ? "\n" : "";
        return $"{prefix}![image]({relativePath}){suffix}";
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
