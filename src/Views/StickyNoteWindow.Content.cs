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
    private const int MarkdownImageMinPercent = 20;
    private const int MarkdownImageMaxPercent = 200;
    private const double MarkdownImageVerticalMargin = 6;

    // ─── FlowDocument ↔ プレーンテキスト / Markdown ──────────────

    private void LoadContent(string text)
        => LoadMarkdownContent(text);

    private void LoadPlainContent(string text)
    {
        text = NormalizeLineEndings(text);
        _suppressTextChange = true;
        try
        {
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
        finally { _suppressTextChange = false; }
    }

    private void LoadMarkdownContent(string text)
    {
        text = NormalizeLineEndings(text);
        _suppressTextChange = true;
        try
        {
            _markdownImageContexts.Clear();
            ContentBox.Document.Blocks.Clear();
            _requiredMarkdownPageWidth = 0;
            foreach (var block in MarkdownRenderer.Render(
                text,
                ViewModel.FontSize,
                CreateHyperlink,
                CreateMarkdownImage,
                CreateTaskCheckbox,
                IsDarkTheme()))
            {
                ContentBox.Document.Blocks.Add(block);
            }
            ApplyMarkdownPageWidth();
        }
        finally { _suppressTextChange = false; }
    }

    private void ApplyMarkdownPageWidth()
    {
        ContentBox.Document.PageWidth = _requiredMarkdownPageWidth > 0
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

        ViewModel.Content = string.Join('\n', lines);
        RequestSave();
        LoadContent(ViewModel.Content);
    }

    private Inline CreateMarkdownImage(MarkdownRenderer.MarkdownImage markdownImage)
    {
        var fallback = CreateMarkdownImageFallback(markdownImage);
        if (!LinkDetector.IsRenderableImageTarget(markdownImage.Target))
            return fallback;

        System.Windows.Media.Imaging.BitmapSource bitmap;
        try
        {
            var imagePath = ResolveImagePath(markdownImage.Target);
            if (imagePath == null || !File.Exists(imagePath))
                return fallback;

            var loaded = new WpfBitmapImage();
            loaded.BeginInit();
            loaded.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            loaded.UriSource = new Uri(imagePath, UriKind.Absolute);
            loaded.EndInit();
            loaded.Freeze();
            bitmap = NormalizeZeroAlphaImage(loaded);
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
            ToolTip = string.IsNullOrWhiteSpace(markdownImage.Alt) ? markdownImage.Target : markdownImage.Alt,
            Margin = new Thickness(0, 3, 0, 3),
        };

        var displayWidth = markdownImage.Width ?? originalWidth;
        var displayHeight = markdownImage.Height ?? originalHeight;
        if (markdownImage.Width.HasValue && !markdownImage.Height.HasValue)
            displayHeight = originalHeight * markdownImage.Width.Value / originalWidth;
        else if (!markdownImage.Width.HasValue && markdownImage.Height.HasValue)
            displayWidth = originalWidth * markdownImage.Height.Value / originalHeight;

        if (!hasExplicitWidth)
        {
            var naturalWidth = markdownImage.Height.HasValue
                ? originalWidth * markdownImage.Height.Value / originalHeight
                : originalWidth;
            displayWidth = Math.Min(naturalWidth, GetMarkdownImageAvailableWidth());
            displayHeight = originalHeight * displayWidth / originalWidth;
            image.Width = displayWidth;
            image.Height = originalHeight * displayWidth / originalWidth;
        }

        if (markdownImage.Width.HasValue)
        {
            image.Width = markdownImage.Width.Value;
            _requiredMarkdownPageWidth = Math.Max(_requiredMarkdownPageWidth, markdownImage.Width.Value);
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
            image.ContextMenu = BuildImageContextMenu(context);
            image.PreviewMouseLeftButtonDown += MarkdownImage_PreviewMouseLeftButtonDown;
            image.PreviewMouseLeftButtonUp += MarkdownImage_PreviewMouseLeftButtonUp;
            image.MouseMove += MarkdownImage_MouseMove;
            image.LostMouseCapture += MarkdownImage_LostMouseCapture;
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

    private double GetMarkdownImageAvailableWidth()
    {
        var boxWidth = ContentBox.ActualWidth > 0 ? ContentBox.ActualWidth : Width;
        var padding = ContentBox.Padding.Left + ContentBox.Padding.Right;
        var border = ContentBox.BorderThickness.Left + ContentBox.BorderThickness.Right;
        const double ScrollbarAllowance = 18;
        return Math.Max(1, boxWidth - padding - border - ScrollbarAllowance);
    }

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

    private ContextMenu BuildImageContextMenu(MarkdownImageContext context)
    {
        var cm = new ContextMenu();
        for (var percent = MarkdownImageMinPercent; percent <= MarkdownImageMaxPercent; percent += 20)
        {
            var percentItem = new MenuItem { Header = $"{percent}%", Tag = "ContentChange" };
            var selectedPercent = percent;
            percentItem.Click += (_, _) => ResizeMarkdownImage(context, selectedPercent);
            cm.Items.Add(percentItem);
        }
        cm.Items.Add(new Separator());
        var removeWidthItem = new MenuItem { Header = LocalizationService.T("RemoveImageWidth"), Tag = "ContentChange" };
        removeWidthItem.Click += (_, _) => RemoveMarkdownImageWidth(context);
        cm.Items.Add(removeWidthItem);

        cm.Items.Add(new Separator());
        var fitWindowItem = new MenuItem { Header = LocalizationService.T("FitWindowToImage") };
        fitWindowItem.Click += (_, _) => FitWindowToMarkdownImage(context);
        cm.Items.Add(fitWindowItem);

        cm.Items.Add(new Separator());
        var detachItem = new MenuItem { Header = LocalizationService.T("DetachImageFromNote"), Tag = "ContentChange" };
        detachItem.Click += (_, _) => RemoveMarkdownImage(context, deleteFile: false);
        cm.Items.Add(detachItem);

        var deleteFileItem = new MenuItem
        {
            Header = LocalizationService.T("DeleteImageFile"),
            Tag = "ContentChange",
            IsEnabled = IsImageFileInNoteAssets(context.Target),
        };
        deleteFileItem.Click += (_, _) => RemoveMarkdownImage(context, deleteFile: true);
        cm.Items.Add(deleteFileItem);

        cm.Opened += (_, _) =>
        {
            _suppressViewMode = true;
            _isContentContextMenuOpen = true;
            foreach (var item in cm.Items.OfType<MenuItem>())
            {
                if (item.Tag is string tag && tag == "ContentChange")
                    item.IsEnabled = ViewModel.IsReadOnly
                        ? item != detachItem && item != deleteFileItem
                        : item != deleteFileItem || IsImageFileInNoteAssets(context.Target);
            }
        };
        cm.Closed += ContentContextMenu_Closed;
        return cm;
    }

    private void RemoveMarkdownImageWidth(MarkdownImageContext context)
    {
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
        var width = Math.Clamp(Math.Round(context.OriginalWidth * percent / 100.0), 1, 2000);
        ReplaceMarkdownImage(context, BuildMarkdownImageText(context, width));
    }

    private void FitWindowToMarkdownImage(MarkdownImageContext context)
    {
        var image = _markdownImageContexts.FirstOrDefault(pair => pair.Value.Equals(context)).Key;
        if (image != null)
            FitWindowToMarkdownImages([image]);
    }

    private void FitWindowToMarkdownImages()
        => FitWindowToMarkdownImages(_markdownImageContexts.Keys);

    private void FitWindowToMarkdownImages(IEnumerable<WpfImage> images)
    {
        var imageList = images.ToList();
        if (imageList.Count == 0)
            return;

        UpdateLayout();
        var contentExtent = GetMarkdownImageContentExtent(imageList);
        var (maxWidth, maxHeight) = GetWorkAreaSize();
        var targetWidth = Math.Min(
            maxWidth,
            Math.Max(MinWidth, contentExtent.Width + GetWindowExtraWidthForContent()));
        var targetHeight = Math.Min(
            maxHeight,
            Math.Max(FoldedHeight, contentExtent.Height + GetWindowExtraHeightForContent()));

        SuppressWindowBoundsSave(() =>
        {
            Width = targetWidth;
            Height = targetHeight;
            KeepInsideWorkArea(Width, Height);
        });

        ViewModel.Model.Width = Width;
        ViewModel.Model.Height = Height - _statusBarDelta;
        ViewModel.Model.X = Left;
        ViewModel.Model.Y = Top;
        RequestSave();
        LoadContent(ViewModel.Content);
    }

    private System.Windows.Size GetMarkdownImageContentExtent(IReadOnlyCollection<WpfImage> images)
    {
        var width = 0.0;
        var height = 0.0;
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
                height = Math.Max(height, bounds.Bottom + verticalOffset + ContentBox.Padding.Bottom + MarkdownImageVerticalMargin);
            }
            catch (InvalidOperationException)
            {
                width = Math.Max(width, actualWidth + ContentBox.Padding.Left + ContentBox.Padding.Right);
                height += actualHeight + MarkdownImageVerticalMargin;
            }
        }

        return new System.Windows.Size(Math.Max(1, width), Math.Max(1, height));
    }

    private double GetWindowExtraWidthForContent()
    {
        var padding = ContentBox.Padding.Left + ContentBox.Padding.Right;
        var border = ContentBox.BorderThickness.Left + ContentBox.BorderThickness.Right;
        var rootBorder = RootBorder.BorderThickness.Left + RootBorder.BorderThickness.Right;
        const double ScrollbarAllowance = 18;
        return border + rootBorder + ScrollbarAllowance;
    }

    private double GetWindowExtraHeightForContent()
    {
        var border = ContentBox.BorderThickness.Top + ContentBox.BorderThickness.Bottom;
        var rootBorder = RootBorder.BorderThickness.Top + RootBorder.BorderThickness.Bottom;
        const double ScrollbarAllowance = 18;
        return ViewModel.TitleBarHeight + border + rootBorder + ScrollbarAllowance;
    }

    private (double Width, double Height) GetWorkAreaSize()
    {
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var (dpiX, dpiY) = GetDpi();
        return (screen.WorkingArea.Width / dpiX, screen.WorkingArea.Height / dpiY);
    }

    private void MarkdownImage_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfImage image)
            return;

        var scrollViewer = FindVisualChild<ScrollViewer>(ContentBox);
        if (scrollViewer == null ||
            scrollViewer.ScrollableWidth <= 0 && scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        _isImageScrollDragging = true;
        _imageScrollStartPoint = e.GetPosition(ContentBox);
        _imageScrollStartHorizontalOffset = scrollViewer.HorizontalOffset;
        _imageScrollStartVerticalOffset = scrollViewer.VerticalOffset;
        image.CaptureMouse();
        ContentBox.Cursor = WpfCursors.SizeAll;
        e.Handled = true;
    }

    private void MarkdownImage_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isImageScrollDragging)
            return;

        var scrollViewer = FindVisualChild<ScrollViewer>(ContentBox);
        if (scrollViewer == null)
            return;

        var current = e.GetPosition(ContentBox);
        scrollViewer.ScrollToHorizontalOffset(_imageScrollStartHorizontalOffset - (current.X - _imageScrollStartPoint.X));
        scrollViewer.ScrollToVerticalOffset(_imageScrollStartVerticalOffset - (current.Y - _imageScrollStartPoint.Y));
        e.Handled = true;
    }

    private void MarkdownImage_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndMarkdownImageScrollDrag(sender as WpfImage);
        e.Handled = true;
    }

    private void MarkdownImage_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        => EndMarkdownImageScrollDrag(sender as WpfImage);

    private void EndMarkdownImageScrollDrag(WpfImage? image)
    {
        if (!_isImageScrollDragging)
            return;

        _isImageScrollDragging = false;
        if (image?.IsMouseCaptured == true)
            image.ReleaseMouseCapture();
        ContentBox.Cursor = WpfCursors.Arrow;
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

        ViewModel.Content = string.Join('\n', lines);
        RequestSave();
        LoadContent(ViewModel.Content);
    }

    private void RemoveMarkdownImage(MarkdownImageContext context, bool deleteFile)
    {
        if (ViewModel.IsReadOnly)
            return;

        if (deleteFile)
        {
            var result = System.Windows.MessageBox.Show(
                LocalizationService.T("DeleteImageFileConfirmMessage"),
                LocalizationService.T("DeleteImageFileConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            if (!DeleteImageFileIfOwnedByNote(context.Target))
                return;
        }

        RemoveMarkdownImageReference(context);
    }

    private void RemoveMarkdownImageReference(MarkdownImageContext context)
    {
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

        ViewModel.Content = string.Join('\n', lines);
        RequestSave();
        LoadContent(ViewModel.Content);
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

        var noteDir = _storage.GetNoteDirectoryPath(ViewModel.Model.Id);
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

    // ─── ハイパーリンク ──────────────────────────────────────────

    private Hyperlink CreateHyperlink(string target)
        => CreateHyperlink(target, target);

    private Hyperlink CreateHyperlink(string text, string target)
    {
        var link = new Hyperlink(new Run(text))
        {
            Foreground      = IsDarkTheme() ? WpfBrushes.LightSkyBlue : WpfBrushes.RoyalBlue,
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

}
