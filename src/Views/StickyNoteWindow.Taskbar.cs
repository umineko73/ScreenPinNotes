using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using Brushes = System.Windows.Media.Brushes;

namespace ScreenPinNotes.Views;

public partial class StickyNoteWindow
{
    private bool _taskbarUnfoldQueued;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetIconicThumbnail(IntPtr hwnd, IntPtr bitmap, int flags);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetIconicLivePreviewBitmap(IntPtr hwnd, IntPtr bitmap, IntPtr offset, int flags);
    [DllImport("dwmapi.dll")]
    private static extern int DwmInvalidateIconicBitmaps(IntPtr hwnd);

    private void ConfigureTaskbarPreview()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || _isClosed) return;
        var enabled = ShowInTaskbar ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 7 /* FORCE_ICONIC_REPRESENTATION */, ref enabled, sizeof(int));
        DwmSetWindowAttribute(hwnd, 10 /* HAS_ICONIC_BITMAP */, ref enabled, sizeof(int));
        InvalidateTaskbarPreview();
    }

    private void InvalidateTaskbarPreview()
    {
        if (!ShowInTaskbar || _isClosed) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) DwmInvalidateIconicBitmaps(hwnd);
    }

    private void HandleTaskbarMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!ShowInTaskbar || _isClosed || _isInitializing) return;
        if (msg is 0x0323 or 0x0326) // DWM thumbnail / live preview request
        {
            try
            {
                var live = msg == 0x0326;
                var width = live ? 2048 : (int)((lParam.ToInt64() >> 16) & 0xffff);
                var height = live ? 2048 : (int)(lParam.ToInt64() & 0xffff);
                if (width <= 0 || height <= 0) return;
                // Peek bitmaps cannot exceed the actual client area. Keep Peek
                // faithful to the desktop; only the switcher thumbnail is expanded.
                var bitmap = live
                    ? RenderTaskbarVisual(RootBorder, RootBorder.ActualWidth, RootBorder.ActualHeight, width, height)
                    : RenderExpandedTaskbarPreview(width, height);
                // GetHbitmap owns a GDI object independently of the managed bitmap.
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                using var native = new System.Drawing.Bitmap(bitmap.PixelWidth, bitmap.PixelHeight,
                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                var data = native.LockBits(new System.Drawing.Rectangle(0, 0, native.Width, native.Height),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly, native.PixelFormat);
                try
                {
                    for (var row = 0; row < native.Height; row++)
                        Marshal.Copy(pixels, row * native.Width * 4, data.Scan0 + row * data.Stride, native.Width * 4);
                }
                finally { native.UnlockBits(data); }
                var handle = native.GetHbitmap(System.Drawing.Color.FromArgb(0));
                try
                {
                    var result = live ? DwmSetIconicLivePreviewBitmap(hwnd, handle, IntPtr.Zero, 0)
                        : DwmSetIconicThumbnail(hwnd, handle, 0);
                    Marshal.ThrowExceptionForHR(result);
                }
                finally { DeleteObject(handle); }
                handled = true;
            }
            catch (Exception ex)
            {
                ErrorReporter.ReportNonFatal("Render taskbar note preview", ex);
            }
        }
        // A click on the note itself is WA_CLICKACTIVE, not WA_ACTIVE. Ignore
        // activation returning from our popups/dialogs and initialization as well.
        // Shell/keyboard switching and restoration both open the chosen note.
        if ((msg == 0x0006 && (wParam.ToInt64() & 0xffff) == 1 &&
             (lParam == IntPtr.Zero || HwndSource.FromHwnd(lParam) is not { RootVisual: not StickyNoteWindow })) ||
            (msg == 0x0112 && (wParam.ToInt64() & 0xfff0) == 0xf120))
            QueueTaskbarUnfold();
    }

    private void QueueTaskbarUnfold()
    {
        if (_taskbarUnfoldQueued || !ViewModel.IsFolded || _isFoldAnimationRunning) return;
        _taskbarUnfoldQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _taskbarUnfoldQueued = false;
            if (_isClosed || !ShowInTaskbar || !IsActive || !ViewModel.IsFolded) return;
            TransitionTo(DisplayMode.View);
        }, DispatcherPriority.Background);
    }

    // A separate visual tree renders the expanded note even when it has never
    // been opened. No live controls, geometry, scrolling or model state change.
    private BitmapSource RenderExpandedTaskbarPreview(int maxWidth, int maxHeight)
    {
        var note = JsonSerializer.Deserialize<StickyNote>(JsonSerializer.Serialize(ViewModel.Model))!;
        note.Content = ViewModel.Content; // Content is deliberately excluded from persisted JSON.
        note.IsFolded = false;
        var vm = new StickyNoteViewModel(note, Settings);
        var width = Math.Clamp(note.Width, 140, 8192);
        var height = Math.Clamp(note.Height, 28, 8192);
        var padding = vm.NoteContentPadding;
        var bodyWidth = Math.Max(1, width - 2 - padding.Left - padding.Right);
        var bodyHeight = Math.Max(1, height - 2 - padding.Top - padding.Bottom - (note.IsTitleBarHidden ? 0 : vm.TitleBarHeight));
        var attachments = note.IsExternalContent && !string.IsNullOrWhiteSpace(note.ExternalContentPath)
            ? new ObsidianAttachments(note.ExternalContentPath) : null;
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        if (!note.IsTitleBarHidden)
        {
            var title = new TextBlock { Text = vm.DisplayTitle, Foreground = vm.TitleBarForeground,
                FontFamily = new System.Windows.Media.FontFamily(note.FontFamily), FontSize = note.TitleFontSize,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0),
                TextTrimming = TextTrimming.CharacterEllipsis };
            var heading = new DockPanel();
            if (IconImage.Source != null)
                heading.Children.Add(new System.Windows.Controls.Image { Source = IconImage.Source,
                    Width = vm.TitleIconSize, Height = vm.TitleIconSize, Margin = new Thickness(6, 0, 0, 0) });
            heading.Children.Add(title);
            grid.Children.Add(new Border { Height = vm.TitleBarHeight, Background = vm.TitleBarBrush, Child = heading });
        }
        Inline ImageInline(MarkdownRenderer.MarkdownImage image)
        {
            var path = attachments?.Resolve(image.Target) ?? ResolveImagePath(image.Target);
            if (path == null || !File.Exists(path)) return new Run(image.Alt);
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                var dpi = VisualTreeHelper.GetDpi(this);
                var naturalWidth = bitmap.PixelWidth / dpi.DpiScaleX;
                var naturalHeight = bitmap.PixelHeight / dpi.DpiScaleY;
                var explicitWidth = GetMarkdownImageWidthOverride(image) ?? image.Width;
                var imageWidth = explicitWidth ?? (image.Height.HasValue ? naturalWidth * image.Height.Value / naturalHeight : naturalWidth);
                var imageHeight = image.Height ?? naturalHeight * imageWidth / naturalWidth;
                if (!explicitWidth.HasValue)
                {
                    var scale = Math.Min(1, bodyWidth / imageWidth);
                    if (vm.UsesTightImageLayout && !image.Height.HasValue) scale = Math.Min(scale, bodyHeight / imageHeight);
                    imageWidth *= scale;
                    imageHeight *= scale;
                }
                return new InlineUIContainer(new System.Windows.Controls.Image { Source = bitmap,
                    Width = imageWidth, Height = imageHeight, Stretch = Stretch.Uniform,
                    Margin = vm.UsesTightImageLayout ? default : new Thickness(0, 3, 0, 3) });
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
            { return new Run(image.Alt); }
        }
        var document = new FlowDocument { FontFamily = new System.Windows.Media.FontFamily(note.FontFamily),
            FontSize = note.FontSize, Foreground = vm.TextForeground,
            PagePadding = vm.UsesTightImageLayout ? default : new Thickness(DefaultDocumentPagePadding, 0, DefaultDocumentPagePadding, 0) };
        foreach (var block in MarkdownRenderer.Render(note.Content, note.FontSize,
            (label, target) => new Hyperlink(new Run(label)) { Foreground = vm.UsesDarkNoteColors ? Brushes.LightSkyBlue : Brushes.RoyalBlue },
            ImageInline, (_, check) => new System.Windows.Controls.CheckBox { IsChecked = check }, vm.UsesDarkNoteColors,
            language: Settings.Language, propertiesCollapsed: note.ArePropertiesCollapsed))
            document.Blocks.Add(block);
        var body = new System.Windows.Controls.RichTextBox { Document = document, IsReadOnly = true,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = padding,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden };
        Grid.SetRow(body, 1);
        grid.Children.Add(body);
        if (vm.TitleSpineVisibility == Visibility.Visible)
        {
            var spine = new Border { Width = vm.TitleSpineWidth, Background = vm.HeaderBrush,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = vm.TitleSpineMargin,
                CornerRadius = new CornerRadius(vm.TitleSpineCornerRadius) };
            Grid.SetRowSpan(spine, 2);
            grid.Children.Add(spine);
        }
        if (note.IsTitleBarHidden && IconImage.Source != null)
        {
            var icon = new System.Windows.Controls.Image { Source = IconImage.Source,
                Width = vm.TitleIconSize, Height = vm.TitleIconSize,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 7, 0) };
            Grid.SetRowSpan(icon, 2);
            grid.Children.Add(icon);
        }
        var root = new Border { Width = width, Height = height, Background = vm.BackgroundBrush,
            BorderBrush = vm.NoteBorderBrush, BorderThickness = new Thickness(1), CornerRadius = vm.NoteCornerRadius,
            Child = grid, ClipToBounds = true };
        root.Measure(new System.Windows.Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        return RenderTaskbarVisual(root, width, height, maxWidth, maxHeight);
    }

    private static BitmapSource RenderTaskbarVisual(Visual root, double width, double height, int maxWidth, int maxHeight)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var scaleFactor = Math.Min(1, Math.Min(Math.Min(maxWidth, 2048) / width, Math.Min(maxHeight, 2048) / height));
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, width * scaleFactor, height * scaleFactor));
        var result = new RenderTargetBitmap(Math.Max(1, (int)(width * scaleFactor)), Math.Max(1, (int)(height * scaleFactor)),
            96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }
}
