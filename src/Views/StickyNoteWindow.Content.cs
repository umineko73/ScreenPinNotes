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
    private const int MarkdownImageMinPercent = 20;
    private const int MarkdownImageMaxPercent = 200;
    private const double MarkdownImageMinDisplayWidth = 80;
    private bool _expandedContentValid;
    private string? _expandedContentText;
    private double _expandedScrollX;
    private double _expandedScrollY;
    private readonly Dictionary<string, DateTime> _renderedImageFiles = new(StringComparer.OrdinalIgnoreCase);

    private void EnsureExpandedContent()
    {
        // Cached pixels still need to reflect images edited outside the app.
        var imagesChanged = _renderedImageFiles.Any(pair => File.GetLastWriteTimeUtc(pair.Key) != pair.Value);
        if (!_expandedContentValid || _expandedContentText != ViewModel.Content || imagesChanged)
            LoadContent(ViewModel.Content);
        ContentBox.UpdateLayout();
        ContentBox.ScrollToHorizontalOffset(_expandedScrollX);
        ContentBox.ScrollToVerticalOffset(_expandedScrollY);
    }

    // ─── FlowDocument ↔ プレーンテキスト / Markdown ──────────────

    private void LoadContent(string text)
    {
        _expandedContentValid = false;
        if (ViewModel.IsFolded)
        {
            UpdateFoldedPreview(text);
            return;
        }
        // Rendering replaces many blocks. Batch them into one layout/change notification
        // and do not retain generated documents in the editor's undo history.
        var undoEnabled = ContentBox.IsUndoEnabled;
        _renderedImageFiles.Clear();
        ContentBox.IsUndoEnabled = false;
        ContentBox.BeginChange();
        try { LoadMarkdownContent(text); UpdateImagePathPreview(); }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Render Markdown; showing source text", ex);
            _markdownImageContexts.Clear();
            LoadPlainContent(text);
        }
        finally
        {
            ContentBox.EndChange();
            ContentBox.IsUndoEnabled = undoEnabled;
        }
        _expandedContentText = text;
        _expandedContentValid = true;
    }

    public void ReloadExternalContent()
    {
        try
        {
            if (_uiDispatcher.HasShutdownStarted || _uiDispatcher.HasShutdownFinished)
                return;

            // FileSystemWatcher はワーカースレッドで発火する。ここでは WPF の
            // Window/コントロール/ViewModel には触れず、UI スレッドだけで処理する。
            _uiDispatcher.BeginInvoke(ReloadExternalContentOnUiThread);
        }
        // FileSystemWatcher のイベントがウィンドウ終了後に届く場合がある。
        // 終了済み Dispatcher へキューできなくても、アプリ全体を終了させない。
        catch (InvalidOperationException)
        {
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Queue external content reload", ex);
        }
    }

    private void ReloadExternalContentOnUiThread()
    {
        try
        {
            // ウォッチャーのイベント発火後にウィンドウが閉じられている場合は何もしない。
            if (_isClosed || !ViewModel.Model.IsExternalContent)
                return;

            // 一時的にファイルが読めない場合は表示中の内容を維持する
            // （エラー文言で上書きしてキャッシュを壊さない）。
            if (StorageService.TryReadExternalContent(ViewModel.Model, out var content))
            {
                ViewModel.Content = content;
                if (!_isEditMode)
                    LoadContent(ViewModel.Content);
            }
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Reload external content", ex);
        }
    }

    private void LoadPlainContent(string text, bool resetUndoHistory = false)
    {
        text = NormalizeLineEndings(text);
        _suppressTextChange = true;
        var restoreUndo = resetUndoHistory && ContentBox.IsUndoEnabled;
        try
        {
            if (restoreUndo)
                ContentBox.IsUndoEnabled = false;

            ContentBox.Document.Blocks.Clear();
            ContentBox.Document.PageWidth = double.NaN;
            var lines = string.IsNullOrEmpty(text) ? [""] : text.Split('\n');
            var para = new Paragraph { Margin = new Thickness(0) };
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                    para.Inlines.Add(new LineBreak());

                para.Inlines.Add(new Run(lines[i]));
            }
            ContentBox.Document.Blocks.Add(para);
        }
        finally
        {
            if (restoreUndo)
                ContentBox.IsUndoEnabled = true;
            _suppressTextChange = false;
        }
    }

    private void LoadMarkdownContent(string text)
    {
        text = NormalizeLineEndings(text);
        _suppressTextChange = true;
        try
        {
            _markdownImageContexts.Clear();
            ContentBox.Document.Blocks.Clear();
            ApplyDocumentPagePadding();
            _requiredMarkdownPageWidth = 0;
            foreach (var block in MarkdownRenderer.Render(
                text,
                ViewModel.ContentFontSize,
                CreateHyperlink,
                CreateMarkdownImage,
                CreateTaskCheckbox,
                ViewModel.UsesDarkNoteColors,
                ignoreFirstLineHeadingSize: ViewModel.IsFolded && ViewModel.IsTitleBarHidden))
            {
                ContentBox.Document.Blocks.Add(block);
                if (block is Table table)
                    SizeMarkdownTableColumns(table);
            }
            ApplyMarkdownPageWidth();
        }
        finally { _suppressTextChange = false; }
    }

    private void UpdateFoldedPreview(string text)
    {
        var preview = string.Concat(MarkdownRenderer.Render(
                GetFoldedPreviewSource(text), ViewModel.TitleFontSize,
                (label, target) => new Hyperlink(new Run(label)),
                image => new Run(image.Alt))
            .Select(block => new TextRange(block.ContentStart, block.ContentEnd).Text));
        FoldedPreviewText.Text = preview.TrimEnd('\r', '\n');
        UpdateImagePathPreview();
    }

    private void SizeMarkdownTableColumns(Table table)
    {
        for (var column = 0; column < table.Columns.Count; column++)
        {
            var width = 0.0;
            foreach (var row in table.RowGroups.SelectMany(group => group.Rows))
            {
                var cell = row.Cells[column];
                var paragraph = (Paragraph)cell.Blocks.FirstBlock;
                var text = new TextRange(cell.ContentStart, cell.ContentEnd).Text.TrimEnd('\r', '\n');
                var measured = new FormattedText(text,
                    System.Globalization.CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
                    new Typeface(new WpfFontFamily(ViewModel.FontFamily), paragraph.FontStyle, paragraph.FontWeight, paragraph.FontStretch),
                    ViewModel.ContentFontSize, ViewModel.TextForeground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                width = Math.Max(width, measured.WidthIncludingTrailingWhitespace + 12);
            }
            table.Columns[column].Width = new GridLength(Math.Max(12, width));
        }
        _requiredMarkdownPageWidth = Math.Max(_requiredMarkdownPageWidth, table.Columns.Sum(column => column.Width.Value));
    }

    private static string GetFoldedPreviewSource(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                return line;
        }

        return string.Empty;
    }

    private void UpdateImagePathPreview()
    {
        if (_isEditMode || !ViewModel.IsFolded || _isFoldAnimationRunning || DataContext is not StickyNoteViewModel) return;
        var iconWidth = string.IsNullOrEmpty(ViewModel.Icon) ? 24 : ViewModel.TitleIconSize + 18;
        FoldedPreviewText.Margin = new Thickness(5, 0, iconWidth + 5, 0);
        var path = MarkdownRenderer.GetImageOnlyTarget(ViewModel.Content);
        if (path == null) return;
        double Measure(string value, double size) => new FormattedText(value,
            System.Globalization.CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface(ViewModel.FontFamily), size, ViewModel.TextForeground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
        if (ViewModel.IsFolded && ViewModel.IsTitleBarHidden)
        {
            // Reserve room for the always-visible icon and its overlay padding.
            var contentWidth = FoldedPreviewHost.ActualWidth > 0
                ? FoldedPreviewHost.ActualWidth
                : Math.Max(0, Width - RootBorder.BorderThickness.Left - RootBorder.BorderThickness.Right);
            var width = contentWidth - FoldedPreviewHost.Padding.Left - FoldedPreviewHost.Padding.Right - iconWidth - 10;
            var display = PathDisplay.Fit(path, width, s => Measure(s, ViewModel.TitleFontSize));
            FoldedPreviewText.Text = display;
        }
        if (string.IsNullOrWhiteSpace(ViewModel.Title))
            TitleText.SetCurrentValue(System.Windows.Controls.TextBlock.TextProperty,
                ViewModel.IsFolded && !ViewModel.IsTitleBarHidden
                    ? PathDisplay.Fit(path, TitleText.ActualWidth, s => Measure(s, ViewModel.TitleFontSize))
                    : ViewModel.DisplayTitle);
    }

    private void ApplyMarkdownPageWidth()
    {
        var availableWidth = GetMarkdownImageAvailableWidth();
        ContentBox.Document.PageWidth = _requiredMarkdownPageWidth > availableWidth
            ? _requiredMarkdownPageWidth
            : double.NaN;
    }

    private string GetPlainText()
    {
        var sb    = new StringBuilder();
        bool first = true;
        foreach (Block block in ContentBox.Document.Blocks)
        {
            if (!first) sb.Append('\n');
            first = false;
            if (block is Paragraph para)
            {
                foreach (Inline inline in para.Inlines)
                {
                    sb.Append(inline switch
                    {
                        Run r                              => r.Text,
                        Hyperlink h when h.Tag is string t => t,
                        LineBreak                          => "\n",
                        _ => new TextRange(inline.ContentStart, inline.ContentEnd).Text,
                    });
                }
            }
        }
        return sb.ToString();
    }

    private WpfCheckBox CreateTaskCheckbox(int lineIndex, bool isChecked)
    {
        var checkbox = new WpfCheckBox
        {
            IsChecked = isChecked,
            Tag = lineIndex,
            Focusable = false,
            IsHitTestVisible = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0),
            Foreground = ViewModel.TextForeground,
        };
        checkbox.PreviewMouseLeftButtonDown += TaskCheckbox_PreviewMouseLeftButtonDown;
        return checkbox;
    }

    private void TaskCheckbox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfCheckBox { Tag: int lineIndex } checkbox)
            return;

        _isTaskCheckboxUpdatePending = true;
        var isChecked = checkbox.IsChecked != true;
        checkbox.IsChecked = isChecked;
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                ToggleMarkdownTask(lineIndex, isChecked);
            }
            finally
            {
                _isTaskCheckboxUpdatePending = false;
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
        e.Handled = true;
    }

    private void ToggleMarkdownTask(int lineIndex, bool isChecked)
    {
        var lines = ViewModel.Content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        if (lineIndex < 0 || lineIndex >= lines.Length)
            return;

        var line = lines[lineIndex];
        var uncheckedIndex = line.IndexOf("[ ]", StringComparison.Ordinal);
        var checkedIndex = line.IndexOf("[x]", StringComparison.OrdinalIgnoreCase);
        var markerIndex = uncheckedIndex >= 0 ? uncheckedIndex : checkedIndex;
        if (markerIndex < 0)
            return;

        lines[lineIndex] =
            line[..markerIndex] +
            (isChecked ? "[x]" : "[ ]") +
            line[(markerIndex + 3)..];

        if (!TrySetNoteContent(string.Join('\n', lines)))
            return;
        LoadContent(ViewModel.Content);
    }

    // 同じ画像は再描画（リサイズ・編集/閲覧モード切替）のたびに
    // ゼロアルファ正規化（全ピクセル走査）をやり直さないよう、
    // ファイルパス＋更新日時をキーにキャッシュする。
    private System.Windows.Media.Imaging.BitmapSource GetOrLoadNormalizedImage(string imagePath)
    {
        var writeTimeUtc = File.GetLastWriteTimeUtc(imagePath);
        if (_normalizedImageCache.TryGetValue(imagePath, out var cached) && cached.WriteTimeUtc == writeTimeUtc)
            return cached.Bitmap;

        var loaded = new WpfBitmapImage();
        loaded.BeginInit();
        loaded.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        loaded.UriSource = new Uri(imagePath, UriKind.Absolute);
        loaded.EndInit();
        loaded.Freeze();
        var normalized = NormalizeZeroAlphaImage(loaded);
        _normalizedImageCache[imagePath] = (writeTimeUtc, normalized);
        return normalized;
    }

    private Inline CreateMarkdownImage(MarkdownRenderer.MarkdownImage markdownImage)
    {
        var fallback = CreateMarkdownImageFallback(markdownImage);
        if (!LinkDetector.IsRenderableImageTarget(markdownImage.Target))
            return fallback;

        System.Windows.Media.Imaging.BitmapSource bitmap;
        string imagePath;
        try
        {
            var resolvedImagePath = ResolveImagePath(markdownImage.Target);
            if (resolvedImagePath == null)
                return fallback;

            imagePath = Path.GetFullPath(resolvedImagePath);
            _renderedImageFiles[imagePath] = File.GetLastWriteTimeUtc(imagePath);
            if (!File.Exists(imagePath))
                return fallback;
            bitmap = GetOrLoadNormalizedImage(imagePath);
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Load markdown image", ex);
            return fallback;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var originalWidth = Math.Max(1, bitmap.PixelWidth / dpi.DpiScaleX);
        var originalHeight = Math.Max(1, bitmap.PixelHeight / dpi.DpiScaleY);
        var hasExplicitWidth = markdownImage.Width.HasValue;
        var image = new WpfImage
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            ToolTip = imagePath,
            // 前後の行と詰まって見えないよう上下を空ける。画像1枚だけの
            // 付箋には空ける相手がいないので、そのぶんも詰める。
            Margin = ViewModel.UsesTightImageLayout ? default : new Thickness(0, 3, 0, 3),
        };

        var widthOverride = GetMarkdownImageWidthOverride(markdownImage);
        var displayWidth = widthOverride ?? markdownImage.Width ?? originalWidth;
        var displayHeight = markdownImage.Height ?? originalHeight;
        if ((widthOverride.HasValue || markdownImage.Width.HasValue) && !markdownImage.Height.HasValue)
            displayHeight = originalHeight * displayWidth / originalWidth;
        else if (!markdownImage.Width.HasValue && markdownImage.Height.HasValue)
            displayWidth = originalWidth * markdownImage.Height.Value / originalHeight;

        if (!hasExplicitWidth && !widthOverride.HasValue)
        {
            var naturalWidth = markdownImage.Height.HasValue
                ? originalWidth * markdownImage.Height.Value / originalHeight
                : originalWidth;
            // サイズ未指定の画像は、付箋に収まる範囲でだけ縮小する。
            // 元のピクセル寸法より拡大すると、低解像度画像がぼやけてしまう。
            displayWidth = Math.Min(naturalWidth, GetMarkdownImageAvailableWidth(
                reserveScrollBar: NeedsScrollBarAllowance(naturalWidth, originalWidth, originalHeight)));
            displayHeight = originalHeight * displayWidth / originalWidth;
            if (ViewModel.UsesTightImageLayout && !markdownImage.Height.HasValue && !_isFittingWindowToImages)
            {
                // Fit a standalone, unspecified image to both dimensions without distortion.
                var scale = Math.Min(1, Math.Min(
                    GetMarkdownImageAvailableWidth(reserveScrollBar: false) / originalWidth,
                    Math.Max(1, GetMarkdownImageAvailableHeight()) / originalHeight));
                displayWidth = originalWidth * scale;
                displayHeight = originalHeight * scale;
            }
            image.Width = displayWidth;
            image.Height = displayHeight;
        }

        if (markdownImage.Width.HasValue)
        {
            image.Width = markdownImage.Width.Value;
            _requiredMarkdownPageWidth = Math.Max(_requiredMarkdownPageWidth, markdownImage.Width.Value);
        }
        if (widthOverride.HasValue)
        {
            image.Width = widthOverride.Value;
            _requiredMarkdownPageWidth = Math.Max(_requiredMarkdownPageWidth, widthOverride.Value);
        }
        if (markdownImage.Height.HasValue)
            image.Height = markdownImage.Height.Value;

        if (markdownImage.LineIndex >= 0)
        {
            var context = new MarkdownImageContext(
                markdownImage.LineIndex,
                markdownImage.Start,
                markdownImage.Length,
                markdownImage.Alt,
                markdownImage.Target,
                originalWidth,
                originalHeight,
                displayWidth,
                displayHeight);
            _markdownImageContexts[image] = context;
            image.PreviewMouseWheel += (_, e) =>
            {
                try
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
                        return;

                    e.Handled = true;
                    QueueNextMarkdownImageResize(context, image, e.Delta);
                }
                catch (Exception ex)
                {
                    e.Handled = true;
                    ErrorReporter.ReportNonFatal("Image mouse wheel", ex);
                    ShowSizeOverlay("画像サイズ変更に失敗しました");
                }
            };
        }

        return new InlineUIContainer(image)
        {
            BaselineAlignment = BaselineAlignment.Center,
        };
    }

    private static Run CreateMarkdownImageFallback(MarkdownRenderer.MarkdownImage markdownImage)
        => new(string.IsNullOrWhiteSpace(markdownImage.Alt)
            ? $"![image]({markdownImage.Target})"
            : $"![{markdownImage.Alt}]({markdownImage.Target})");

    private const double ScrollbarAllowance = 18;

    private double GetMarkdownImageAvailableWidth(bool reserveScrollBar = true)
    {
        var boxWidth = ContentBox.ActualWidth > 0 ? ContentBox.ActualWidth : Width;
        var padding = ContentBox.Padding.Left + ContentBox.Padding.Right;
        var border = ContentBox.BorderThickness.Left + ContentBox.BorderThickness.Right;
        var allowance = reserveScrollBar ? ScrollbarAllowance : 0;
        return Math.Max(MarkdownImageMinDisplayWidth, boxWidth - padding - border - allowance);
    }

    private double GetMarkdownImageAvailableHeight()
    {
        var boxHeight = ContentBox.ActualHeight > 0 ? ContentBox.ActualHeight : Height;
        return boxHeight
            - ContentBox.Padding.Top - ContentBox.Padding.Bottom
            - ContentBox.BorderThickness.Top - ContentBox.BorderThickness.Bottom;
    }

    /// <summary>
    /// 縦スクロールバーの場所を空けておくかどうか。文字が続く付箋では、
    /// 空けずに広げると後からバーが出たときに画像がはみ出して横スクロールまで
    /// 増えるので、常に空けておく。画像1枚だけの付箋は高さが読み切れるので、
    /// 縦に収まりきるときだけ空けずに済ませ、右端の隙間をなくす。
    /// </summary>
    private bool NeedsScrollBarAllowance(double naturalWidth, double originalWidth, double originalHeight)
    {
        // 付箋を画像に合わせている最中は、収まる大きさをこれから決めるところ。
        // ここで場所を空けると、空けたぶん画像が縮み、その縮んだ姿に高さを
        // 合わせ、また空ける、と堂々巡りになって下に隙間が残り続ける。
        if (_isFittingWindowToImages) return false;
        if (!ViewModel.UsesTightImageLayout) return true;

        var width = Math.Min(naturalWidth, GetMarkdownImageAvailableWidth(reserveScrollBar: false));
        return originalHeight * width / originalWidth > GetMarkdownImageAvailableHeight();
    }

    /// <summary>
    /// FlowDocument は既定で左右に 5px の余白を持つ。画像1枚だけの付箋では
    /// これも詰める。文字のときは既定のままにして、行頭が縁に寄らないようにする。
    /// </summary>
    private void ApplyDocumentPagePadding()
        => ContentBox.Document.PagePadding = ViewModel.UsesTightImageLayout
            ? default
            : new Thickness(DefaultDocumentPagePadding, 0, DefaultDocumentPagePadding, 0);

    /// <summary>RichTextBox が FlowDocument に与える左右余白の既定値。</summary>
    private const double DefaultDocumentPagePadding = 5;

    private sealed record MarkdownImageContext(
        int LineIndex,
        int Start,
        int Length,
        string Alt,
        string Target,
        double OriginalWidth,
        double OriginalHeight,
        double DisplayWidth,
        double DisplayHeight);

    private sealed record PendingMarkdownImageResize(MarkdownImageContext Context, int Percent);

    /// <summary>
    /// 本文メニューの先頭へ差し込む画像用の項目を作る。中身は開くたびに
    /// <see cref="UpdateImageMenuItems"/> が右クリック先の画像へ向け直すので、
    /// ここでは画像を特定せず、器だけを1回作る。
    /// 倍率だけ小メニューに畳んであるのは、通常の項目まで並ぶと画面に
    /// 収まらなくなるため。よく使う4つは開いてすぐ押せる位置に残す。
    /// </summary>
    private IReadOnlyList<FrameworkElement> BuildImageMenuItems()
    {
        _imageSizeItem = new MenuItem { Header = LocalizationService.T("ImageSizeMenu") };
        for (var percent = MarkdownImageMinPercent; percent <= MarkdownImageMaxPercent; percent += 20)
        {
            var percentItem = new MenuItem { Header = $"{percent}%" };
            var selectedPercent = percent;
            percentItem.Click += (_, _) => WithContextMenuImage(c => ResizeMarkdownImage(c, selectedPercent));
            _imageSizeItem.Items.Add(percentItem);
        }
        _imageSizeItem.Items.Add(new Separator());
        _removeImageWidthItem = new MenuItem { Header = LocalizationService.T("RemoveImageWidth") };
        _removeImageWidthItem.Click += (_, _) => WithContextMenuImage(RemoveMarkdownImageWidth);
        _imageSizeItem.Items.Add(_removeImageWidthItem);

        _fitWindowToImageItem = new MenuItem { Header = LocalizationService.T("FitWindowToImage") };
        _fitWindowToImageItem.Click += (_, _) => WithContextMenuImage(FitWindowToMarkdownImage);

        _detachImageItem = new MenuItem { Header = LocalizationService.T("DetachImageFromNote") };
        _detachImageItem.Click += (_, _) => WithContextMenuImage(c => RemoveMarkdownImage(c, deleteFile: false));

        _deleteImageFileItem = new MenuItem { Header = LocalizationService.T("DeleteImageFile") };
        _deleteImageFileItem.Click += (_, _) => WithContextMenuImage(c => RemoveMarkdownImage(c, deleteFile: true));

        _imageMenuSeparator = new Separator();
        return new FrameworkElement[]
        {
            _imageSizeItem, _fitWindowToImageItem, _detachImageItem, _deleteImageFileItem, _imageMenuSeparator,
        };
    }

    private void WithContextMenuImage(Action<MarkdownImageContext> action)
    {
        if (_contextMenuImage is { } context) action(context);
    }

    /// <summary>
    /// 右クリックがどの画像に当たったかを覚える。ContextMenuOpening の
    /// OriginalSource はメニューの持ち主（ContentBox）になってしまい、
    /// 実際に押された要素が分からないので、押した時点で拾っておく。
    /// </summary>
    private void CaptureContextMenuImage(object? originalSource)
        => _contextMenuImage = FindMarkdownImageContext(originalSource);

    /// <summary>
    /// 覚えておいた画像に合わせて、画像用の項目の表示と可否を整える。
    /// 画像の上でなければ丸ごと隠し、通常の本文メニューだけにする。
    /// </summary>
    private void UpdateImageMenuItems(bool fromKeyboard)
    {
        // キーボードから開いたときは直前の右クリックの記憶が残っているだけなので捨てる。
        if (fromKeyboard) _contextMenuImage = null;
        var visibility = _contextMenuImage == null ? Visibility.Collapsed : Visibility.Visible;
        _imageSizeItem.Visibility = visibility;
        _fitWindowToImageItem.Visibility = visibility;
        _fitWindowToImageItem.IsEnabled = !_isEditMode;
        _detachImageItem.Visibility = visibility;
        _deleteImageFileItem.Visibility = visibility;
        _imageMenuSeparator.Visibility = visibility;
        // 付箋の全画像版は、右クリックした画像を対象にする版と入れ替える。
        // 画像1枚の付箋では同じ動きの項目が2つ並んでしまうため。
        var wholeNoteVisibility = _contextMenuImage == null ? Visibility.Visible : Visibility.Collapsed;
        _fitWindowToImagesItem.Visibility = wholeNoteVisibility;
        _fitWindowToImagesSeparator.Visibility = wholeNoteVisibility;
        if (_contextMenuImage is not { } context) return;

        var canEdit = !IsContentReadOnly();
        _imageSizeItem.IsEnabled = CanResizeMarkdownImage();
        _detachImageItem.IsEnabled = canEdit;
        _deleteImageFileItem.IsEnabled = canEdit && IsImageFileInNoteAssets(context.Target);
    }

    private MarkdownImageContext? FindMarkdownImageContext(object? originalSource)
    {
        var node = originalSource as DependencyObject;
        // 本文は Run や Paragraph の入れ子なので、いくら深くても十数段。
        // 打ち切りを置くのは、木を遡る2つの経路が行き来して戻らなくなる
        // ことがあり得るため。右クリックのたびに通る場所なので止めない。
        for (var depth = 0; node != null && depth < 64; depth++)
        {
            if (node is WpfImage image && _markdownImageContexts.TryGetValue(image, out var context))
                return context;
            node = GetParentNode(node);
        }

        return null;
    }

    /// <summary>
    /// 木を1段だけ遡る。本文の文字を右クリックすると Run から始まって
    /// FlowDocument まで来るが、これは Visual ではないので
    /// <see cref="VisualTreeHelper.GetParent"/> は null ではなく例外を返す。
    /// Visual かどうかで経路を選び分ける。
    /// </summary>
    private static DependencyObject? GetParentNode(DependencyObject node)
        => node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);

    private bool CanResizeMarkdownImage()
        => true;

    private void RemoveMarkdownImageWidth(MarkdownImageContext context)
    {
        if (ShouldUseImageWidthOverrides())
        {
            ClearMarkdownImageWidthOverride(context);
            return;
        }

        ReplaceMarkdownImage(context, BuildMarkdownImageText(context, null));
    }

    private bool ResizeMarkdownImageAtPoint(System.Windows.Point point, int wheelDelta)
    {
        var hit = VisualTreeHelper.HitTest(ContentBox, point)?.VisualHit as DependencyObject;
        while (hit != null)
        {
            if (hit is WpfImage image && _markdownImageContexts.TryGetValue(image, out var context))
            {
                QueueNextMarkdownImageResize(context, image, wheelDelta);
                return true;
            }

            hit = VisualTreeHelper.GetParent(hit);
        }

        return false;
    }

    private void QueueNextMarkdownImageResize(MarkdownImageContext context, WpfImage image, int wheelDelta)
    {
        if (!CanResizeMarkdownImage())
        {
            ShowSizeOverlay(LocalizationService.T("EditLockNotice"));
            return;
        }

        var currentPercent = GetCurrentMarkdownImagePercent(context, image);
        var nextPercent = Math.Clamp(
            currentPercent + (wheelDelta > 0 ? 20 : -20),
            MarkdownImageMinPercent,
            MarkdownImageMaxPercent);
        QueueMarkdownImageResize(context, nextPercent);
        ShowSizeOverlay($"画像 {nextPercent}%");
    }

    private int GetCurrentMarkdownImagePercent(MarkdownImageContext context, WpfImage image)
    {
        if (_pendingMarkdownImageResize is { } pending &&
            pending.Context.LineIndex == context.LineIndex &&
            pending.Context.Start == context.Start &&
            string.Equals(pending.Context.Target, context.Target, StringComparison.Ordinal))
        {
            return pending.Percent;
        }

        // image.Width == 0 は「サイズ0%を明示的に指定した」有効な状態なので、
        // NaN（未指定）とは区別する。0 を "未指定" 扱いすると、0%まで縮めた画像の
        // 現在値が誤って ActualWidth/OriginalWidth 側にフォールバックしてしまう。
        var currentWidth = !double.IsNaN(image.Width)
            ? image.Width
            : image.ActualWidth > 0
                ? image.ActualWidth
                : context.OriginalWidth;

        return (int)Math.Round(currentWidth / context.OriginalWidth * 100.0 / 20.0) * 20;
    }

    private void QueueMarkdownImageResize(MarkdownImageContext context, int percent)
    {
        _pendingMarkdownImageResize = new PendingMarkdownImageResize(context, percent);
        if (_isMarkdownImageResizeQueued)
            return;

        _isMarkdownImageResizeQueued = true;
        Dispatcher.BeginInvoke(ProcessPendingMarkdownImageResize, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ProcessPendingMarkdownImageResize()
    {
        var pending = _pendingMarkdownImageResize;
        _pendingMarkdownImageResize = null;
        _isMarkdownImageResizeQueued = false;
        if (pending == null)
            return;

        try
        {
            ResizeMarkdownImage(pending.Context, pending.Percent);
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Resize markdown image", ex);
            ShowSizeOverlay("画像サイズ変更に失敗しました");
        }
    }

    private void ResizeMarkdownImage(MarkdownImageContext context, int percent)
    {
        percent = Math.Clamp(percent, MarkdownImageMinPercent, MarkdownImageMaxPercent);
        var width = Math.Max(1, Math.Round(context.OriginalWidth * percent / 100.0));
        if (ShouldUseImageWidthOverrides())
        {
            SetMarkdownImageWidthOverride(context, width);
            return;
        }

        ReplaceMarkdownImage(context, BuildMarkdownImageText(context, width));
    }

    private bool ShouldUseImageWidthOverrides()
        => ViewModel.IsReadOnly || ViewModel.Model.IsExternalContent;

    private double? GetMarkdownImageWidthOverride(MarkdownRenderer.MarkdownImage markdownImage)
    {
        if (!ShouldUseImageWidthOverrides())
            return null;

        return ViewModel.Model.ExternalImageWidthOverrides.TryGetValue(GetMarkdownImageOverrideKey(markdownImage), out var width)
            ? width
            : null;
    }

    private void SetMarkdownImageWidthOverride(MarkdownImageContext context, double width)
    {
        ViewModel.Model.ExternalImageWidthOverrides[GetMarkdownImageOverrideKey(context)] = width;
        ViewModel.Model.UpdatedAt = DateTime.Now;
        RequestSave();
        LoadContent(ViewModel.Content);
    }

    private void ClearMarkdownImageWidthOverride(MarkdownImageContext context)
    {
        ViewModel.Model.ExternalImageWidthOverrides.Remove(GetMarkdownImageOverrideKey(context));
        ViewModel.Model.UpdatedAt = DateTime.Now;
        RequestSave();
        LoadContent(ViewModel.Content);
    }

    private static string GetMarkdownImageOverrideKey(MarkdownRenderer.MarkdownImage image)
        => $"{image.LineIndex}:{image.Start}:{image.Target}";

    private static string GetMarkdownImageOverrideKey(MarkdownImageContext context)
        => $"{context.LineIndex}:{context.Start}:{context.Target}";

    private void FitWindowToMarkdownImage(MarkdownImageContext context)
        => FitWindowToMarkdownImages([context]);

    private void FitWindowToMarkdownImages()
        => FitWindowToMarkdownImages(_markdownImageContexts.Values.ToList());

    /// <summary>
    /// 付箋を画像にぴったり合わせる。<see cref="MarkdownImageContext"/> で受けるのは、
    /// 途中で LoadContent が走ると Image の実体が作り直されるため。
    /// </summary>
    private void FitWindowToMarkdownImages(IReadOnlyCollection<MarkdownImageContext> contexts)
    {
        // Fitting changes the view bounds, never the temporary editor bounds.
        if (_isEditMode) return;
        CompleteFoldAnimation();
        // Completing an unfold can run a callback that enters edit mode.
        if (_isEditMode) return;
        if (contexts.Count == 0)
            return;

        _isFittingWindowToImages = true;
        try
        {

        // 大きさを変えると、幅に合わせて縮めている画像はその場で伸び縮みし、
        // 必要な高さも変わる。1回測って当てるだけでは下に隙間が残るので、
        // 動かなくなるまで測り直す。数回で収まらない組み合わせもあり得るため
        // 上限を置く（振動したままだと操作が返ってこない）。
        for (var pass = 0; pass < 4; pass++)
        {
            UpdateLayout();
            var images = ResolveMarkdownImages(contexts);
            if (images.Count == 0)
                return;

            var (targetWidth, targetHeight) = GetFitToImagesSize(images);
            var settled = Math.Abs(targetWidth - Width) < 1 && Math.Abs(targetHeight - Height) < 1;
            SuppressWindowBoundsSave(() =>
            {
                Width = targetWidth;
                Height = targetHeight;
                KeepInsideWorkArea(Width, Height);
            });
            LoadContent(ViewModel.Content);
            if (settled) break;
        }
        }
        finally
        {
            _isFittingWindowToImages = false;
        }

        ViewModel.Model.Width = Width;
        ViewModel.Model.Height = Height;
        ViewModel.Model.X = Left;
        ViewModel.Model.Y = Top;
        MarkPositionSeparatedIfOpenViewMovedAwayFromClosedView();
        RequestSave();
    }

    private List<WpfImage> ResolveMarkdownImages(IReadOnlyCollection<MarkdownImageContext> contexts)
        => _markdownImageContexts
            .Where(pair => contexts.Any(context => IsSameMarkdownImage(context, pair.Value)))
            .Select(pair => pair.Key)
            .ToList();

    /// <summary>
    /// 同じ画像を指しているか。record の等値では駄目で、表示中の大きさまで
    /// 一致を求めてしまう ―― measure して当てるたびに変わる値なので、
    /// 測り直しの2周目で見失う。本文のどこを指しているかだけで見る。
    /// </summary>
    private static bool IsSameMarkdownImage(MarkdownImageContext a, MarkdownImageContext b)
        => a.LineIndex == b.LineIndex && a.Start == b.Start && a.Target == b.Target;

    private (double Width, double Height) GetFitToImagesSize(IReadOnlyCollection<WpfImage> images)
    {
        var contentExtent = GetMarkdownImageContentExtent(images);
        var (maxWidth, maxHeight) = GetWorkAreaSize();
        var desiredWidth = Math.Max(MinWidth, contentExtent.Width + GetWindowExtraWidthForContent());
        var desiredHeight = Math.Max(FoldedHeight, contentExtent.Height + GetWindowExtraHeightForContent());

        // ここまでは中身がぴったり収まる大きさ。画面に入りきらず切り詰める側には
        // スクロールバーが出るので、そのときだけ直交する向きにバーの幅を足す。
        // 常に足していたころは、収まっている付箋にも下と右に隙間が残っていた。
        if (desiredHeight > maxHeight) desiredWidth += ScrollbarAllowance;
        if (desiredWidth > maxWidth) desiredHeight += ScrollbarAllowance;

        return (Math.Min(maxWidth, desiredWidth), Math.Min(maxHeight, desiredHeight));
    }

    private System.Windows.Size GetMarkdownImageContentExtent(IReadOnlyCollection<WpfImage> images)
    {
        var width = 0.0;
        var height = 0.0;
        // TransformToAncestor が失敗した画像は実際の描画位置が分からないため、
        // 縦積みされる前提で高さを別途積算し、最後に height と Math.Max で合成する
        // （height 自体に += してしまうと、他の画像の Math.Max 結果と混ざって
        // 意味のない値になってしまう）。
        var fallbackStackedHeight = 0.0;
        var scrollViewer = FindVisualChild<ScrollViewer>(ContentBox);
        var horizontalOffset = scrollViewer?.HorizontalOffset ?? 0;
        var verticalOffset = scrollViewer?.VerticalOffset ?? 0;
        foreach (var image in images)
        {
            var actualWidth = image.ActualWidth > 0 ? image.ActualWidth : image.Width;
            var actualHeight = image.ActualHeight > 0 ? image.ActualHeight : image.Height;
            if (double.IsNaN(actualWidth) || actualWidth <= 0)
                actualWidth = _markdownImageContexts.TryGetValue(image, out var widthContext)
                    ? widthContext.DisplayWidth
                    : 0;
            if (double.IsNaN(actualHeight) || actualHeight <= 0)
                actualHeight = _markdownImageContexts.TryGetValue(image, out var heightContext)
                    ? heightContext.DisplayHeight
                    : 0;

            try
            {
                var bounds = image.TransformToAncestor(ContentBox)
                    .TransformBounds(new Rect(0, 0, actualWidth, actualHeight));
                width = Math.Max(width, bounds.Right + horizontalOffset + ContentBox.Padding.Right);
                // bounds は画像そのものの矩形。上の余白は位置に織り込み済みなので、
                // 足すのは下の余白だけ。決め打ちの値ではなく実物から取るのは、
                // 画像1枚だけの付箋では余白を 0 にしてあるため。
                height = Math.Max(height, bounds.Bottom + verticalOffset + ContentBox.Padding.Bottom + image.Margin.Bottom);
            }
            catch (InvalidOperationException)
            {
                width = Math.Max(width, actualWidth + ContentBox.Padding.Left + ContentBox.Padding.Right);
                fallbackStackedHeight += actualHeight + image.Margin.Top + image.Margin.Bottom;
            }
        }

        height = Math.Max(height, fallbackStackedHeight);
        return new System.Windows.Size(Math.Max(1, width), Math.Max(1, height));
    }

    // 本文の余白は contentExtent 側で見ているので、ここでは数えない。
    private double GetWindowExtraWidthForContent()
        => ContentBox.BorderThickness.Left + ContentBox.BorderThickness.Right
           + RootBorder.BorderThickness.Left + RootBorder.BorderThickness.Right;

    private double GetWindowExtraHeightForContent()
        => TitleBarExtraHeight
           + ContentBox.BorderThickness.Top + ContentBox.BorderThickness.Bottom
           + RootBorder.BorderThickness.Top + RootBorder.BorderThickness.Bottom;

    /// <summary>
    /// タイトルバーが占める高さ。隠しているときは行ごと畳んであるので 0。
    /// 数えたままだと、本文の下にタイトルバー1本分の空きが残る。
    /// </summary>
    private double TitleBarExtraHeight
        => ViewModel.TitleBarVisibility == Visibility.Visible ? ViewModel.TitleBarHeight : 0;

    private (double Width, double Height) GetWorkAreaSize()
    {
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var (dpiX, dpiY) = GetDpi();
        return (screen.WorkingArea.Width / dpiX, screen.WorkingArea.Height / dpiY);
    }

    private void BeginPaneScrollDrag(System.Windows.Point point)
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(ContentBox);
        if (scrollViewer == null ||
            scrollViewer.ScrollableWidth <= 0 && scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        _isPaneScrollDragging = true;
        _suppressNextContentContextMenu = true;
        _paneScrollStartPoint = point;
        _paneScrollStartHorizontalOffset = scrollViewer.HorizontalOffset;
        _paneScrollStartVerticalOffset = scrollViewer.VerticalOffset;
        ContentBox.CaptureMouse();
        ContentBox.Cursor = WpfCursors.SizeAll;
    }

    private void UpdatePaneScrollDrag(System.Windows.Point current)
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(ContentBox);
        if (scrollViewer == null)
            return;

        scrollViewer.ScrollToHorizontalOffset(_paneScrollStartHorizontalOffset - (current.X - _paneScrollStartPoint.X));
        scrollViewer.ScrollToVerticalOffset(_paneScrollStartVerticalOffset - (current.Y - _paneScrollStartPoint.Y));
    }

    private void EndPaneScrollDrag()
    {
        if (!_isPaneScrollDragPending && !_isPaneScrollDragging)
            return;

        _isPaneScrollDragPending = false;
        _isPaneScrollDragging = false;
        if (ContentBox.IsMouseCaptured)
            ContentBox.ReleaseMouseCapture();
        ContentBox.Cursor = IsBodyEditing()
            ? WpfCursors.IBeam
            : WpfCursors.Arrow;
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                return typed;

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
                return descendant;
        }

        return null;
    }

    private void ReplaceMarkdownImage(MarkdownImageContext context, string replacement)
    {
        if (ViewModel.Model.IsExternalContent)
            return;

        var lines = ViewModel.Content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        if (context.LineIndex < 0 || context.LineIndex >= lines.Length)
            return;

        var line = lines[context.LineIndex];
        if (context.Start < 0 ||
            context.Start + context.Length > line.Length ||
            line[context.Start..(context.Start + context.Length)].IndexOf(context.Target, StringComparison.Ordinal) < 0)
        {
            return;
        }

        lines[context.LineIndex] =
            line[..context.Start] +
            replacement +
            line[(context.Start + context.Length)..];

        if (!TrySetNoteContent(string.Join('\n', lines)))
            return;
        LoadContent(ViewModel.Content);
    }

    private void RemoveMarkdownImage(MarkdownImageContext context, bool deleteFile)
    {
        if (IsContentReadOnly())
            return;

        if (deleteFile)
        {
            var confirmed = ConfirmationDialog.ShowFor(
                this,
                LocalizationService.T("DeleteImageFileConfirmMessage"),
                LocalizationService.T("DeleteImageFileConfirmTitle"));
            if (!confirmed)
                return;

            if (!DeleteImageFileIfOwnedByNote(context.Target))
                return;
        }

        RemoveMarkdownImageReference(context, recordUndo: !deleteFile);
    }

    private void RemoveMarkdownImageReference(MarkdownImageContext context, bool recordUndo)
    {
        var previousContent = ViewModel.Content;
        var lines = ViewModel.Content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').ToList();
        if (context.LineIndex < 0 || context.LineIndex >= lines.Count)
            return;

        var line = lines[context.LineIndex];
        if (context.Start < 0 ||
            context.Start + context.Length > line.Length ||
            line[context.Start..(context.Start + context.Length)].IndexOf(context.Target, StringComparison.Ordinal) < 0)
        {
            return;
        }

        var before = line[..context.Start];
        var after = line[(context.Start + context.Length)..];
        if (string.IsNullOrWhiteSpace(before) && string.IsNullOrWhiteSpace(after))
            lines.RemoveAt(context.LineIndex);
        else
            lines[context.LineIndex] = before + after;

        var nextContent = string.Join('\n', lines);
        if (!TrySetNoteContent(nextContent))
            return;
        if (recordUndo)
            _contentUndoStack.Push(new ContentUndoEntry(previousContent, nextContent));
        LoadContent(ViewModel.Content);
        ContentBox.Focus();
    }

    private bool DeleteImageFileIfOwnedByNote(string target)
    {
        var imagePath = ResolveImagePath(target);
        if (imagePath == null)
            return false;
        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
            return true;

        if (!IsImageFileInNoteAssets(target))
        {
            System.Windows.MessageBox.Show(
                LocalizationService.T("DeleteExternalImageFileBlocked"),
                LocalizationService.T("DeleteImageFileConfirmTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        try
        {
            File.Delete(fullPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsImageFileInNoteAssets(string target)
    {
        var imagePath = ResolveImagePath(target);
        if (imagePath == null)
            return false;

        var fullPath = Path.GetFullPath(imagePath);
        var assetsRoot = Path.GetFullPath(_storage.GetNoteAssetsDirectoryPath(ViewModel.Model.Id))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        return fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildMarkdownImageText(MarkdownImageContext context, double? width)
        => width.HasValue
            ? FormattableString.Invariant($"![{context.Alt}]({context.Target}){{width={width.Value:0}}}")
            : $"![{context.Alt}]({context.Target})";

    private string? ResolveImagePath(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return null;

        if (Path.IsPathRooted(target))
            return Path.GetFullPath(target);

        var noteDir = GetMarkdownBaseDirectory();
        if ((target.Contains("://", StringComparison.Ordinal) ||
             target.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) &&
            Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return uri.IsFile ? uri.LocalPath : null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(noteDir, target.Replace('/', Path.DirectorySeparatorChar)));
        var noteRoot = Path.GetFullPath(noteDir) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(noteRoot, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }

    private string GetMarkdownBaseDirectory()
    {
        if (ViewModel.Model.IsExternalContent &&
            !string.IsNullOrWhiteSpace(ViewModel.Model.ExternalContentPath))
        {
            var fullPath = Path.GetFullPath(ViewModel.Model.ExternalContentPath);
            return Path.GetDirectoryName(fullPath) ?? _storage.GetNoteDirectoryPath(ViewModel.Model.Id);
        }

        return _storage.GetNoteDirectoryPath(ViewModel.Model.Id);
    }

    // ─── ハイパーリンク ──────────────────────────────────────────

    private Hyperlink CreateHyperlink(string target)
        => CreateHyperlink(target, target);

    private Hyperlink CreateHyperlink(string text, string target)
    {
        var link = new Hyperlink(new Run(text))
        {
            Foreground      = ViewModel.UsesDarkNoteColors ? WpfBrushes.LightSkyBlue : WpfBrushes.RoyalBlue,
            Cursor          = WpfCursors.Hand,
            Tag             = target,
            ToolTip         = target,
            TextDecorations = TextDecorations.Underline,
        };
        link.Click += (_, _) => OpenTarget(target);
        return link;
    }

    private static void OpenTarget(string target)
    {
        try
        {
            if (LinkDetector.IsFolder(target))
                Process.Start("explorer.exe", $"\"{target}\"");
            else
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Open link target", ex);
        }
    }

    private void ConfigureExternalContentWatcher()
    {
        DisposeExternalContentWatcher();
        if (!ViewModel.Model.IsExternalContent ||
            string.IsNullOrWhiteSpace(ViewModel.Model.ExternalContentPath))
            return;

        try
        {
            var fullPath = Path.GetFullPath(ViewModel.Model.ExternalContentPath);
            var directory = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
                return;

            _externalContentWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _externalContentWatcher.Changed += (_, _) => ReloadExternalContent();
            _externalContentWatcher.Created += (_, _) => ReloadExternalContent();
            _externalContentWatcher.Renamed += (_, _) => ReloadExternalContent();
            _externalContentWatcher.Deleted += (_, _) => ReloadExternalContent();
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Watch external content", ex);
        }
    }

    private void DisposeExternalContentWatcher()
    {
        if (_externalContentWatcher == null)
            return;

        _externalContentWatcher.Dispose();
        _externalContentWatcher = null;
    }

    public void OpenExternalFile()
    {
        if (!ViewModel.Model.IsExternalContent ||
            string.IsNullOrWhiteSpace(ViewModel.Model.ExternalContentPath))
            return;

        OpenTarget(Path.GetFullPath(ViewModel.Model.ExternalContentPath));
    }

    public void OpenExternalFolder()
    {
        if (!ViewModel.Model.IsExternalContent ||
            string.IsNullOrWhiteSpace(ViewModel.Model.ExternalContentPath))
            return;

        var folder = Path.GetDirectoryName(Path.GetFullPath(ViewModel.Model.ExternalContentPath));
        if (!string.IsNullOrWhiteSpace(folder))
            OpenTarget(folder);
    }

    public void ConvertExternalToNormalNote()
    {
        if (!ViewModel.Model.IsExternalContent)
            return;

        // 変換前に最新の内容を取り直す。読めない場合は表示中の内容
        // （直前に読めていた内容）をそのまま引き継ぐ。
        if (StorageService.TryReadExternalContent(ViewModel.Model, out var freshContent))
        {
            if (!CanAcceptNoteContent(freshContent))
            {
                ShowSizeOverlay(string.Format(
                    LocalizationService.T("NoteContentTooLarge"),
                    FormatByteSize(Settings.MaxNoteContentBytes)));
                return;
            }

            ViewModel.Content = freshContent;
        }

        DisposeExternalContentWatcher();
        ViewModel.ClearExternalContentPath();
        ViewModel.IsReadOnly = false;
        ViewModel.Icon = "📝";
        RequestSave();
        // ApplyReadOnlyState は「読み取り専用でなくなった」場合そのまま return し
        // LoadContent を呼ばない。ここでは Content 自体を上の freshContent で
        // 差し替えているので、タイトルのパス表示なども含めて明示的に描き直す。
        ApplyReadOnlyState();
        if (!_isEditMode)
            LoadContent(ViewModel.Content);
        ConfigureContextMenus();
    }

    private sealed class RelayCommand(Action<object?> execute) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute(parameter);
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

}
