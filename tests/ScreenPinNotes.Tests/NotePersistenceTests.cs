using System.IO;
using System.Reflection;
using System.Windows.Controls;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

public class NotePersistenceTests
{
    [WpfFact]
    public void EditingFlush_SavesToInjectedStoreWithoutAppRegistration()
    {
        WpfApplicationFixture.Ensure();
        var root = TempRoot();
        var storage = new StorageService(root);
        var note = new StickyNote();
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), storage);
        try
        {
            window.Show();
            Edit(window, "保存される日本語🦊");
            window.FlushPendingSave();
            Assert.Equal("保存される日本語🦊", Assert.Single(storage.Load()).Content);
            Assert.True(((TextBox)window.FindName("BodyEditBox")).CanUndo);
        }
        finally { window.Close(); DeleteTemp(root); }
    }

    [WpfFact]
    public void FailedFlush_KeepsPendingChangesForRetry()
    {
        WpfApplicationFixture.Ensure();
        var root = TempRoot();
        var storage = new StorageService(root);
        var window = new StickyNoteWindow(new StickyNoteViewModel(new StickyNote(), App.Current.Settings), storage);
        try
        {
            window.Show();
            Edit(window, "失敗後も保持");
            Directory.CreateDirectory(root);
            // A file in place of the notes directory makes the real store fail.
            File.WriteAllText(storage.NotesRoot, "blocked");
            Assert.ThrowsAny<IOException>(() => window.FlushPendingSave());
            File.Delete(storage.NotesRoot);
            window.FlushPendingSave();
            Assert.Equal("失敗後も保持", Assert.Single(storage.Load()).Content);
        }
        finally
        {
            if (File.Exists(storage.NotesRoot)) File.Delete(storage.NotesRoot);
            window.Close();
            DeleteTemp(root);
        }
    }

    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteWithPendingSave_DoesNotRecreateNote(bool fromManager)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var root = TempRoot();
        var storage = new StorageService(root);
        var field = typeof(App).GetField("_storage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previousStore = field.GetValue(app);
        var windows = (List<StickyNoteWindow>)typeof(App).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        var previousWindows = windows.ToArray();
        var note = new StickyNote();
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, app.Settings), storage);
        try
        {
            windows.Clear();
            windows.Add(window);
            field.SetValue(app, storage);
            window.Show();
            Edit(window, "削除前の保留中本文");
            Assert.True(fromManager ? app.RemoveNoteFromManager(note.Id) : app.RemoveNote(note.Id));
            if (!fromManager) window.Close();
            window.FlushPendingSave();
            typeof(StickyNoteWindow).GetMethod("SaveNote", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            await Task.Delay(app.Settings.Timings.SaveDebounceMs + 100);
            Assert.Empty(storage.Load());
            Assert.False(Directory.Exists(storage.GetNoteDirectoryPath(note.Id)));
        }
        finally
        {
            windows.Clear();
            window.Close();
            windows.AddRange(previousWindows);
            field.SetValue(app, previousStore);
            DeleteTemp(root);
        }
    }

    private static void Edit(StickyNoteWindow window, string text)
    {
        typeof(StickyNoteWindow).GetMethod("EnterEditMode", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        ((TextBox)window.FindName("BodyEditBox")).SelectedText = text;
    }

    private static string TempRoot() => Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
    private static void DeleteTemp(string root) { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
