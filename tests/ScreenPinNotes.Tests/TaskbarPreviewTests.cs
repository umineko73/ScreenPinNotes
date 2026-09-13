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

using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

[Collection("WPF")]
public class TaskbarPreviewTests
{
    private static object? Call(StickyNoteWindow window, string method, params object?[] args)
        => typeof(StickyNoteWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewRendersExpandedContentWithoutChangingFoldedNote(bool hiddenTitle)
    {
        WpfApplicationFixture.Ensure();
        var storage = new StorageService(Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString()));
        var note = new StickyNote { IsFolded = true, IsTitleBarHidden = hiddenTitle, Width = 400, Height = 500,
            Content = "# Preview heading\n\nVisible body text\n\n| Item | Value |\n| --- | --- |\n| Width | 400 |" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), storage);
        try
        {
            window.Show(); window.UpdateLayout();
            var before = JsonSerializer.Serialize(note);
            var bounds = new Rect(window.Left, window.Top, window.Width, window.Height);
            var first = (BitmapSource)Call(window, "RenderExpandedTaskbarPreview", 240, 180)!;
            Assert.Equal(144, first.PixelWidth);
            Assert.Equal(180, first.PixelHeight);
            Assert.Equal(before, JsonSerializer.Serialize(note));
            Assert.Equal(bounds, new Rect(window.Left, window.Top, window.Width, window.Height));
            Assert.True(note.IsFolded);
            var pixels = Pixels(first);
            Assert.True(pixels.Distinct().Count() > 30);
            note.Content = "# Changed body\n\nCompletely different content";
            var changed = (BitmapSource)Call(window, "RenderExpandedTaskbarPreview", 240, 180)!;
            Assert.False(pixels.SequenceEqual(Pixels(changed)));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(first));
            using var file = File.Create(Path.Combine(Path.GetTempPath(), $"ScreenPinNotes-taskbar-{hiddenTitle}.png"));
            encoder.Save(file);
        }
        finally { window.Close(); }
    }

    [WpfTheory]
    [InlineData(0x0006, 1, true)] // Shell/keyboard activation
    [InlineData(0x0006, 2, false)] // Clicking the note
    [InlineData(0x0006, 0, false)] // Deactivation
    [InlineData(0x0112, 0xf120, true)] // Restore from taskbar
    public void TaskbarSelectionOpensFoldedNoteButDirectClickDoesNot(int message, int parameter, bool opens)
    {
        WpfApplicationFixture.Ensure();
        var storage = new StorageService(Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString()));
        var note = new StickyNote { IsFolded = true, Width = 400, Height = 500, Content = "Body" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), storage);
        try
        {
            window.Show(); window.Activate(); window.UpdateLayout();
            window.ShowInTaskbar = true;
            Call(window, "HandleTaskbarMessage", IntPtr.Zero, message, (IntPtr)parameter, IntPtr.Zero, false);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Call(window, "CompleteFoldAnimation");
            Assert.Equal(!opens, note.IsFolded);
            if (opens) Assert.Equal(500, window.Height, 1);
        }
        finally { window.Close(); }
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    [WpfFact]
    public void InitiallyFoldedImageNoteRendersImageAndDoesNotUnfoldOnStartup()
    {
        WpfApplicationFixture.Ensure();
        var storage = new StorageService(Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString()));
        var note = new StickyNote { IsFolded = true, IsTitleBarHidden = true, Width = 400, Height = 300,
            Content = "![](assets/red.png)" };
        var assets = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assets);
        var source = BitmapSource.Create(100, 100, 96, 96, PixelFormats.Bgra32, null,
            Enumerable.Range(0, 100 * 100).SelectMany(_ => new byte[] { 0, 0, 255, 255 }).ToArray(), 400);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using (var file = File.Create(Path.Combine(assets, "red.png"))) encoder.Save(file);
        var settings = ScreenPinNotes.App.Current.Settings;
        var wasInTaskbar = settings.ShowNotesInTaskbar;
        settings.ShowNotesInTaskbar = true;
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, settings), storage);
        try
        {
            window.Show(); window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(note.IsFolded);
            var bitmap = (BitmapSource)Call(window, "RenderExpandedTaskbarPreview", 200, 150)!;
            var pixels = Pixels(bitmap);
            Assert.True(Enumerable.Range(0, pixels.Length / 4).Count(i =>
                pixels[i * 4] < 10 && pixels[i * 4 + 1] < 10 && pixels[i * 4 + 2] > 240) > 100);

            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var enabled = 1;
            Assert.Equal(0, DwmSetWindowAttribute(hwnd, 7, ref enabled, sizeof(int)));
            Assert.Equal(0, DwmSetWindowAttribute(hwnd, 10, ref enabled, sizeof(int)));
            // DwmSetIconicThumbnail requires a real outstanding shell request;
            // fabricated WM_DWMSENDICONICTHUMBNAIL messages are rejected by DWM.
            Assert.True(note.IsFolded);
        }
        finally { window.Close(); settings.ShowNotesInTaskbar = wasInTaskbar; }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
