using ScreenPinNotes.Models;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Services;

/// <summary>Owns note-window lifecycle. Shell ordering and tray presentation stay in App.</summary>
public sealed class NoteWorkspace(
    List<StickyNoteWindow> windows,
    Func<StorageService> storage,
    Func<StickyNote, StickyNoteWindow> createWindow)
{
    public StickyNoteWindow Open(StickyNote note, bool show)
    {
        var window = createWindow(note);
        windows.Add(window);
        if (show && !note.IsHidden) window.Show();
        return window;
    }

    public StickyNoteWindow? Find(string id) => windows.FirstOrDefault(w => w.ViewModel.Model.Id == id);

    public bool Hide(string id)
    {
        if (Find(id) is not { } window) return false;
        window.ViewModel.Model.IsHidden = true;
        window.Hide();
        return true;
    }

    public bool Show(string id)
    {
        if (Find(id) is not { } window) return false;
        window.ViewModel.Model.IsHidden = false;
        window.Show();
        window.Activate();
        return true;
    }

    public bool Remove(string id, bool closeWindow)
    {
        var window = Find(id);
        if (closeWindow && window == null) return false;
        if (window?.ViewModel.Model is { IsReadOnly: true, IsExternalContent: false }) return false;
        // Disable pending saves only after deletion succeeds, so failed deletion loses no edits.
        storage().DeleteNote(id);
        window?.DisableSaving();
        windows.RemoveAll(w => w.ViewModel.Model.Id == id);
        if (closeWindow) window?.Close();
        return true;
    }

    public void CloseAll()
    {
        var previous = windows.ToArray();
        windows.Clear();
        foreach (var window in previous) window.Close();
    }

    public void SaveAll()
        => NoteSaveCoordinator.SaveAll(windows.Select<StickyNoteWindow, Action>(window => window.SaveNote));
}
