using System.Reflection;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

public class NoteTransitionRegressionTests
{
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
            typeof(StickyNoteWindow).GetMethod("SnapToAll", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Assert.Equal(195, window.Left);
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
}
