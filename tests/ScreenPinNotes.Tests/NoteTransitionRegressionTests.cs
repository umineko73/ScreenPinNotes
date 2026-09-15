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

using System.Reflection;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

public class NoteTransitionRegressionTests
{
    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualFoldedWidthSurvivesContentChangesAndReload(bool hiddenTitle)
    {
        WpfApplicationFixture.Ensure();
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        var settings = new AppSettings { EnableFoldAnimation = false };
        var note = new StickyNote { Width = 1000, IsFolded = true, IsTitleBarHidden = hiddenTitle, Content = "短い文章" };
        var storage = new StorageService(root);
        var vm = new StickyNoteViewModel(note, settings);
        var window = new StickyNoteWindow(vm, storage);
        void Call(string name, params object?[] args) => typeof(StickyNoteWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        var rect = System.Runtime.InteropServices.Marshal.AllocHGlobal(16);
        try
        {
            window.Show(); window.UpdateLayout();
            Assert.Null(note.ManualFoldedWidth);
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX;
            var pixels = (int)Math.Round(700 * dpi);
            System.Runtime.InteropServices.Marshal.Copy(new[] { 100, 100, 100 + pixels, 140 }, 0, rect, 4);
            Call("WndProc", IntPtr.Zero, 0x0214, (IntPtr)2, rect, false);
            Assert.InRange(note.ManualFoldedWidth!.Value, 680, 720); // edge snapping may adjust a few pixels
            window.Width = note.ManualFoldedWidth.Value; // apply the sizing rectangle as Windows would
            var manual = window.Width;
            vm.Content = "変更後";
            Call("LoadContent", vm.Content);
            Call("SetTitleFontSize", 30d);
            Assert.Equal(manual, window.Width);
            Call("ToggleFold", (object?)null);
            Assert.Equal(1000, window.Width);
            Call("ToggleFold", (object?)null);
            Assert.Equal(manual, window.Width);
            Call("SaveNote");
            var restored = Assert.Single(storage.Load());
            Assert.Equal(manual, restored.ManualFoldedWidth);
            var reopened = new StickyNoteWindow(new StickyNoteViewModel(restored, settings), storage);
            try { reopened.Show(); reopened.UpdateLayout(); Assert.Equal(manual, reopened.Width); }
            finally { reopened.Close(); }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(rect);
            window.Close();
            if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
        }
    }

    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FoldFitsTextAndRestoresWideBody(bool hiddenTitle)
    {
        WpfApplicationFixture.Ensure();
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        var settings = new AppSettings { EnableFoldAnimation = false };
        var note = new StickyNote { Width = 1000, Height = 320, FoldedWidth = 1000,
            IsTitleBarHidden = hiddenTitle, Content = "短い文章\n![写真](missing.png)" };
        var vm = new StickyNoteViewModel(note, settings);
        var window = new StickyNoteWindow(vm, new StorageService(root));
        void Call(string name, params object?[] args) => typeof(StickyNoteWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        try
        {
            window.Show(); window.UpdateLayout();
            Call("ToggleFold", (object?)null); window.UpdateLayout();
            var shortWidth = window.Width;
            Assert.InRange(shortWidth, window.MinWidth, 300);
            Assert.Equal(1000, note.Width);
            Call("ToggleFold", (object?)null);
            Assert.Equal(1000, window.Width);
            vm.Content = new string('あ', 200) + "\n![写真](missing.png)";
            Call("ToggleFold", (object?)null); window.UpdateLayout();
            Assert.True(window.Width > shortWidth);
            Assert.InRange(window.Width, window.MinWidth, 480);
            Call("ToggleFold", (object?)null);
            Assert.Equal(1000, window.Width);
            Assert.Equal(320, note.Height);
        }
        finally { window.Close(); if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
    }

    [WpfFact]
    public void MoveDoesNotSnapToHiddenNote()
    {
        WpfApplicationFixture.Ensure();
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        var storage = new StorageService(root);
        var window = new StickyNoteWindow(new StickyNoteViewModel(new StickyNote { X = 195, Y = 300, Width = 140 }, App.Current.Settings), storage);
        var hidden = new StickyNoteWindow(new StickyNoteViewModel(new StickyNote { X = 200, Y = 600, Width = 140, IsHidden = true }, App.Current.Settings), storage);
        var windows = (List<StickyNoteWindow>)typeof(App).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(App.Current)!;
        try
        {
            windows.Add(hidden);
            window.Show(); window.UpdateLayout();
            Assert.False(hidden.IsVisible);
            var leftBeforeSnap = window.Left;
            typeof(StickyNoteWindow).GetMethod("SnapToAll", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Assert.Equal(leftBeforeSnap, window.Left);
        }
        finally { windows.Remove(hidden); window.Close(); hidden.Close(); if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
    }

    [WpfTheory]
    [InlineData("title", false)]
    [InlineData("font", false)]
    [InlineData("edit", false)]
    [InlineData("titleEdit", false)]
    [InlineData("title", true)]
    [InlineData("font", true)]
    [InlineData("edit", true)]
    [InlineData("titleEdit", true)]
    public async Task InterruptAnimation(string operation, bool hiddenTitle)
    {
        WpfApplicationFixture.Ensure();
        var settings = App.Current.Settings;
        var old = settings.EnableFoldAnimation;
        settings.EnableFoldAnimation = true;
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        var editing = operation is "edit" or "titleEdit";
        var note = new StickyNote { IsFolded = editing, IsTitleBarHidden = hiddenTitle, Width = 420, Height = 320, EditWidth = 520, EditHeight = 420, Content = "日本語" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, settings), new StorageService(root));
        void Call(string name, params object?[] args) => typeof(StickyNoteWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        try
        {
            window.Show(); window.UpdateLayout();
            Call("ToggleFold", (object?)null);
            if (operation == "title") Call("ToggleTitleBarHidden");
            if (operation == "font") Call("SetTitleFontSize", 20d);
            if (operation == "edit") Call("EnterEditMode");
            if (operation == "titleEdit") Call("EnterTitleEditMode");
            await Task.Delay(settings.Timings.FoldAnimationMs + 200);
            window.UpdateLayout();
            Assert.False((bool)typeof(StickyNoteWindow).GetField("_isFoldAnimationRunning", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!);
            if (editing)
            {
                Assert.Equal(420, window.Height);
                Assert.Equal(520, window.Width);
                Assert.Equal(320, note.Height);
                Assert.Equal(420, note.EditHeight);
            }
        }
        finally { window.Close(); settings.EnableFoldAnimation = old; if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
    }

    /// <summary>
    /// 1行表示で幅を調節している最中に展開されると、その幅は1行表示のものなのに
    /// 開いた表示の幅として保存され、展開しても1行表示と同じ幅のままになっていた。
    /// ドラッグ最後のサイズ変更が展開処理の後に届く場合も同じ。
    /// </summary>
    [WpfTheory]
    [InlineData(false)] // the drag continues past the unfold
    [InlineData(true)]  // only the drag's last message lands after the unfold
    public void WidthDraggedWhileFolded_DoesNotBecomeTheExpandedWidth(bool trailingOnly)
    {
        WpfApplicationFixture.Ensure();
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        var note = new StickyNote { Width = 520, Height = 340, Content = "line one\nline two\nline three" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), new StorageService(root));
        void Call(string name, params object?[] args) => typeof(StickyNoteWindow)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        // Windows brackets an edge drag with WM_ENTERSIZEMOVE / WM_EXITSIZEMOVE and
        // resizes the window to each WM_SIZING rectangle in between.
        var rect = System.Runtime.InteropServices.Marshal.AllocHGlobal(16);
        var foldedHeight = 0d;
        // Windows sizes from the rectangle grabbed at the start of the drag, so the
        // height it applies stays the one-line height even after the note expands.
        void SizeTo(double width)
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX;
            System.Runtime.InteropServices.Marshal.Copy(
                new[] { 100, 100, 100 + (int)Math.Round(width * dpi), 100 + (int)Math.Round(foldedHeight * dpi) }, 0, rect, 4);
            Call("WndProc", IntPtr.Zero, 0x0214, (IntPtr)2, rect, false);
            var applied = new int[4];
            System.Runtime.InteropServices.Marshal.Copy(rect, applied, 0, 4);
            window.Width = (applied[2] - applied[0]) / dpi;
            window.Height = (applied[3] - applied[1]) / dpi;
        }
        try
        {
            window.Show(); window.UpdateLayout();
            Call("ToggleFold", (object?)null);
            window.UpdateLayout();
            foldedHeight = window.Height;

            Call("WndProc", IntPtr.Zero, 0x0231, IntPtr.Zero, IntPtr.Zero, false); // WM_ENTERSIZEMOVE
            SizeTo(700);
            Assert.Equal(700, note.ManualFoldedWidth);
            Call("ToggleFold", (object?)null); // expands while the edge is still held
            SizeTo(trailingOnly ? 700 : 650);
            Call("WndProc", IntPtr.Zero, 0x0232, IntPtr.Zero, IntPtr.Zero, false); // WM_EXITSIZEMOVE
            window.UpdateLayout();

            Assert.Equal(520, note.Width);
            Assert.Equal(340, note.Height);
            Assert.Equal(520, window.Width);
            Assert.Equal(340, window.Height); // not left one line tall at the expanded width
            Assert.Equal(700, note.ManualFoldedWidth); // the folded width is still the user's
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(rect);
            window.Close();
            if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
        }
    }
}
