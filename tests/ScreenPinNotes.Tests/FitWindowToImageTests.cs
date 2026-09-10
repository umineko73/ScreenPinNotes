using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

/// <summary>
/// 「画像に合わせて付箋のサイズを調整」。合わせたあとに余りが出ないこと。
/// 大きさを変えると画像も伸び縮みするので、1回測って当てるだけでは足りない。
/// </summary>
[Collection("WPF")]
public class FitWindowToImageTests
{
    [WpfTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void FitToImage_LeavesNoGapAroundTheImage(bool hiddenTitleBar)
    {
        WpfApplicationFixture.Ensure();
        var root = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString());
        var storage = new StorageService(root);
        // 付箋より大きい画像。幅に合わせて縮むので、縮んだ姿で測り直す必要がある。
        var note = NoteWithImage(storage, 600, 600, hiddenTitleBar);
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), storage);
        try
        {
            Open(window, note);
            FitToImages(window);

            var box = (RichTextBox)window.FindName("ContentBox");
            var image = SingleImage(box);

            // 画像の下に残る空き。以前は「常に空けていたスクロールバーの場所
            // + 隠しているタイトルバーの高さ + 決め打ちの画像余白」で
            // 25px ほど残っていた。
            var below = box.ActualHeight - box.Padding.Top - box.Padding.Bottom - image.ActualHeight;
            Assert.True(below < 2, $"gap below the image: {below:F1}px");

            var beside = box.ActualWidth - box.Padding.Left - box.Padding.Right - image.ActualWidth;
            Assert.True(beside < 2, $"gap beside the image: {beside:F1}px");
        }
        finally { window.Close(); }
    }

    // ぴったり合わせたのだからスクロールするものは無い。バーが出ていたら、
    // その幅ぶんどこかが足りていない。
    [WpfFact]
    public void FitToImage_LeavesNothingToScroll()
    {
        WpfApplicationFixture.Ensure();
        var root = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString());
        var storage = new StorageService(root);
        var note = NoteWithImage(storage, 600, 600, hiddenTitleBar: true);
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), storage);
        try
        {
            Open(window, note);
            FitToImages(window);

            var box = (RichTextBox)window.FindName("ContentBox");
            var scrollViewer = FindScrollViewer(box);

            Assert.NotNull(scrollViewer);
            Assert.Equal(0, scrollViewer!.ScrollableHeight, 0);
            Assert.Equal(0, scrollViewer.ScrollableWidth, 0);
        }
        finally { window.Close(); }
    }

    // 元の寸法より小さい付箋に合わせても、画像を引き伸ばしはしない。
    // 小さい画像は等倍のまま、付箋のほうがその大きさに縮む。
    [WpfFact]
    public void FitToImage_KeepsASmallImageAtItsOwnSize()
    {
        WpfApplicationFixture.Ensure();
        var root = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString());
        var storage = new StorageService(root);
        var note = NoteWithImage(storage, 120, 90, hiddenTitleBar: true);
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), storage);
        try
        {
            Open(window, note);
            FitToImages(window);

            var box = (RichTextBox)window.FindName("ContentBox");
            var image = SingleImage(box);
            var dpi = VisualTreeHelper.GetDpi(window);

            Assert.Equal(120 / dpi.DpiScaleX, image.ActualWidth, 1);
            var below = box.ActualHeight - box.Padding.Top - box.Padding.Bottom - image.ActualHeight;
            Assert.True(below < 2, $"gap below the image: {below:F1}px");
        }
        finally { window.Close(); }
    }

    private static StickyNote NoteWithImage(
        StorageService storage, int pixelWidth, int pixelHeight, bool hiddenTitleBar)
    {
        var note = new StickyNote
        {
            Width = 400,
            Height = 500,
            IsTitleBarHidden = hiddenTitleBar,
            Content = "![](assets/p.png)",
        };
        var assets = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assets);
        var pixels = new byte[pixelWidth * pixelHeight * 4];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = 200;
        var bitmap = BitmapSource.Create(
            pixelWidth, pixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, pixelWidth * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(assets, "p.png"));
        encoder.Save(stream);
        return note;
    }

    private static void Open(StickyNoteWindow window, StickyNote note)
    {
        window.Show();
        window.UpdateLayout();
        typeof(StickyNoteWindow)
            .GetMethod("LoadContent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, new object[] { note.Content });
        window.UpdateLayout();
    }

    private static void FitToImages(StickyNoteWindow window)
    {
        typeof(StickyNoteWindow)
            .GetMethod("FitWindowToMarkdownImages", BindingFlags.Instance | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null)!
            .Invoke(window, null);
        window.UpdateLayout();
    }

    private static Image SingleImage(RichTextBox box)
        => box.Document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .OfType<InlineUIContainer>()
            .Select(container => (Image)container.Child)
            .Single();

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer scrollViewer) return scrollViewer;
            if (FindScrollViewer(child) is { } found) return found;
        }

        return null;
    }
}
