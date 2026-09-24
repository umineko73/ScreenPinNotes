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
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

/// <summary>
/// クリップボードから作る付箋の本文と、起動時に表示しない設定・新しい文言。
/// 実際のクリップボードは他のプロセスと共有なので、DataObject を直接渡して確かめる。
/// </summary>
public class ClipboardNoteTests
{
    [WpfFact]
    public void Text_BecomesTheNoteBody()
    {
        var (root, storage) = CreateStorage();
        try
        {
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, "hello\r\nworld\r\n");

            Assert.True(StickyNoteWindow.TryBuildClipboardNoteContent(data, storage, Guid.NewGuid().ToString(), out var content));
            Assert.Equal("hello\nworld", content);
        }
        finally { Delete(root); }
    }

    /// <summary>Excel や Word は文字と一緒に選択範囲の絵も載せる。付箋にするのは文字のほう。</summary>
    [WpfFact]
    public void Text_WinsOverThePictureCopiedAlongsideIt()
    {
        var (root, storage) = CreateStorage();
        try
        {
            var id = Guid.NewGuid().ToString();
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, "A\tB");
            data.SetImage(CreateBitmap());

            Assert.True(StickyNoteWindow.TryBuildClipboardNoteContent(data, storage, id, out var content));
            Assert.Equal("A\tB", content);
            Assert.False(Directory.Exists(storage.GetNoteAssetsDirectoryPath(id)));
        }
        finally { Delete(root); }
    }

    [WpfFact]
    public void Image_IsSavedIntoTheNotesAssets()
    {
        var (root, storage) = CreateStorage();
        try
        {
            var id = Guid.NewGuid().ToString();
            var data = new DataObject();
            data.SetImage(CreateBitmap());

            Assert.True(StickyNoteWindow.TryBuildClipboardNoteContent(data, storage, id, out var content));
            Assert.Matches(@"^!\[image\]\(assets/image-[^)]+\.png\)$", content);
            Assert.Single(Directory.GetFiles(storage.GetNoteAssetsDirectoryPath(id)));
        }
        finally { Delete(root); }
    }

    [WpfFact]
    public void ImageFiles_AreCopiedIntoTheNotesAssets()
    {
        var (root, storage) = CreateStorage();
        try
        {
            var id = Guid.NewGuid().ToString();
            var source = Path.Combine(root, "photo.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(CreateBitmap()));
            using (var stream = File.Create(source)) encoder.Save(stream);
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { source });

            Assert.True(StickyNoteWindow.TryBuildClipboardNoteContent(data, storage, id, out var content));
            Assert.StartsWith("![photo", content);
            Assert.Contains("](assets/", content);
            Assert.Single(Directory.GetFiles(storage.GetNoteAssetsDirectoryPath(id)));
            Assert.True(File.Exists(source)); // the original is left alone
        }
        finally { Delete(root); }
    }

    [WpfTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CopiedPng_PreservesAlphaEvenWhenBitmapHasLostIt(bool pasteIntoExisting, bool fullyTransparent)
    {
        WpfApplicationFixture.Ensure();
        var (root, storage) = CreateStorage();
        StickyNoteWindow? window = null;
        try
        {
            byte[] pixels = [10, 20, 30, 0, 40, 50, 60, 128, 70, 80, 90, 255];
            if (fullyTransparent) { pixels[7] = 0; pixels[11] = 0; }
            var bitmap = BitmapSource.Create(3, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 12);
            var data = (DataObject)typeof(StickyNoteWindow)
                .GetMethod("BuildImageDataObject", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [bitmap])!;
            var png = Assert.IsType<MemoryStream>(data.GetData("PNG"));
            // OLE の Bitmap 変換で透明部分が不透明になった状態を再現する。
            var flattened = pixels.ToArray();
            for (var i = 3; i < flattened.Length; i += 4) flattened[i] = 255;
            data.SetImage(BitmapSource.Create(3, 1, 96, 96, PixelFormats.Bgra32, null, flattened, 12));
            png.Position = png.Length; // ストリーム位置にも依存せず、繰り返し貼れること。
            var note = new StickyNote();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (pasteIntoExisting)
                {
                    window ??= new StickyNoteWindow(
                        new ScreenPinNotes.ViewModels.StickyNoteViewModel(note, new AppSettings()), storage);
                    typeof(StickyNoteWindow).GetMethod("PasteFromDataObject", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(window, [data]);
                }
                else
                {
                    Assert.True(StickyNoteWindow.TryBuildClipboardNoteContent(data, storage, note.Id, out _));
                }
                Assert.Equal(png.Length, png.Position);
            }
            var savedFiles = Directory.GetFiles(storage.GetNoteAssetsDirectoryPath(note.Id));
            Assert.Equal(2, savedFiles.Length);
            foreach (var path in savedFiles)
            {
                using var file = File.OpenRead(path);
                var saved = new PngBitmapDecoder(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                var bgra = new FormatConvertedBitmap(saved, PixelFormats.Bgra32, null, 0);
                var actual = new byte[12];
                bgra.CopyPixels(actual, 12, 0);
                Assert.Equal(pixels, actual);
                if (window != null)
                {
                    var rendered = (BitmapSource)typeof(StickyNoteWindow)
                        .GetMethod("GetOrLoadNormalizedImage", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(window, [path])!;
                    new FormatConvertedBitmap(rendered, PixelFormats.Bgra32, null, 0).CopyPixels(actual, 12, 0);
                    Assert.Equal(pixels, actual);
                }
            }
        }
        finally { window?.Close(); Delete(root); }
    }

    [WpfTheory]
    [InlineData("PNG", false)]
    [InlineData("image/png", true)]
    public void PngWithoutBitmap_IsAccepted(string format, bool asBytes)
    {
        var (root, storage) = CreateStorage();
        try
        {
            using var png = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(CreateBitmap()));
            encoder.Save(png);
            var data = new DataObject();
            data.SetData(format, asBytes ? (object)png.ToArray() : png);
            Assert.True(StickyNoteWindow.TryBuildClipboardNoteContent(data, storage, Guid.NewGuid().ToString(), out _));
        }
        finally { Delete(root); }
    }

    [WpfFact]
    public void InvalidPng_FallsBackToBitmap()
    {
        var (root, storage) = CreateStorage();
        try
        {
            var data = new DataObject();
            data.SetData("PNG", new MemoryStream([1, 2, 3]));
            data.SetImage(CreateBitmap());
            Assert.True(StickyNoteWindow.TryBuildClipboardNoteContent(data, storage, Guid.NewGuid().ToString(), out _));
        }
        finally { Delete(root); }
    }

    [WpfTheory]
    [InlineData(null)]
    [InlineData("  \r\n\t")]
    public void NothingUsable_IsRejected(string? text)
    {
        var (root, storage) = CreateStorage();
        try
        {
            var data = new DataObject();
            if (text != null) data.SetData(DataFormats.UnicodeText, text);

            Assert.False(StickyNoteWindow.TryBuildClipboardNoteContent(data, storage, Guid.NewGuid().ToString(), out var content));
            Assert.Equal("", content);
        }
        finally { Delete(root); }
    }

    [WpfFact]
    public void StartHidden_OpensNotesWithoutShowingThemUntilShowAll()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var (root, storage) = CreateStorage();
        var windowsField = typeof(App).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var storageField = typeof(App).GetField("_storage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var windows = (List<StickyNoteWindow>)windowsField.GetValue(app)!;
        var previous = windows.ToList();
        var previousStorage = storageField.GetValue(app);
        StickyNoteWindow? window = null;
        try
        {
            windows.Clear();
            storageField.SetValue(app, storage);
            var note = new StickyNote { Title = "hidden at start" };

            window = (StickyNoteWindow)typeof(App).GetMethod("OpenNoteWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(app, [note, false])!;
            Assert.False(window.IsVisible);
            Assert.False(note.IsHidden); // the note's own hidden flag is not rewritten

            app.ShowAllNotes();
            Assert.True(window.IsVisible);
        }
        finally
        {
            windows.Clear();
            windows.AddRange(previous);
            storageField.SetValue(app, previousStorage);
            window?.Close();
            Delete(root);
        }
    }

    [Fact]
    public void Normalize_FallsBackToTheDefaultClipboardShortcutOnlyWhenInvalid()
    {
        var settings = new AppSettings { ClipboardNoteHotkey = "N" };
        settings.Normalize();
        Assert.Equal("Ctrl+Alt+Shift+N", settings.ClipboardNoteHotkey);

        settings.ClipboardNoteHotkey = "";
        settings.Normalize();
        Assert.Equal("", settings.ClipboardNoteHotkey); // disabled stays disabled
    }

    [Theory]
    [InlineData("ja", "TrayNewNoteFromClipboard", "クリップボードから付箋を作成")]
    [InlineData("en", "TrayNewNoteFromClipboard", "New note from clipboard")]
    [InlineData("ja", "SettingsStartHidden", "起動時に付箋を表示しない（トレイで待機）")]
    [InlineData("en", "SettingsStartHidden", "Start with notes hidden (wait in the tray)")]
    [InlineData("ja", "SettingsClipboardNoteHotkey", "クリップボードから作成するショートカット")]
    [InlineData("en", "SettingsClipboardNoteHotkey", "New note from clipboard shortcut")]
    [InlineData("ja", "ClipboardNoteEmpty", "クリップボードに付箋にできる内容（文字・画像）がありません。")]
    [InlineData("en", "ClipboardNoteEmpty", "The clipboard has no text or image to make a note from.")]
    public void NewTextIsTranslated(string language, string key, string expected)
        => Assert.Equal(expected, LocalizationService.T(key, language));

    [Theory]
    [InlineData("ja")]
    [InlineData("en")]
    public void ClipboardHotkeyHintNamesItsDefault(string language)
        => Assert.Contains(GlobalNoteHotkey.DefaultClipboardGesture, LocalizationService.T("ClipboardHotkeyHint", language));

    private static (string Root, StorageService Storage) CreateStorage()
    {
        var root = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return (root, new StorageService(root));
    }

    private static BitmapSource CreateBitmap()
    {
        var pixels = Enumerable.Repeat((byte)200, 4 * 4 * 4).ToArray();
        var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Delete(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch (IOException) { }
    }
}
