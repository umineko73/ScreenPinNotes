using ScreenPinNotes.Models;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Services;

/// <summary>Owns note-window lifecycle. Shell ordering and tray presentation stay in App.</summary>
/// <summary>ごみ箱へ移した付箋。元に戻すときに使う。</summary>
public sealed record RemovedNote(StickyNote Note, string TrashedPath);

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

    public bool Remove(string id, bool closeWindow) => Remove(id, closeWindow, out _);

    /// <summary>
    /// 付箋を削除する。フォルダはごみ箱へ移り、<paramref name="removed"/> に
    /// 元に戻すための情報（付箋の中身とごみ箱の場所）を返す。
    /// </summary>
    public bool Remove(string id, bool closeWindow, out RemovedNote? removed)
    {
        removed = null;
        var window = Find(id);
        if (closeWindow && window == null) return false;
        if (window?.ViewModel.Model is { IsReadOnly: true, IsExternalContent: false }) return false;
        // ごみ箱に移す前に書きかけの内容を書き出す。戻したときに最後の編集まで残るように。
        window?.SaveNote();
        // Disable pending saves only after deletion succeeds, so failed deletion loses no edits.
        var trashed = storage().DeleteNote(id);
        window?.DisableSaving();
        windows.RemoveAll(w => w.ViewModel.Model.Id == id);
        if (closeWindow) window?.Close();
        if (window != null && trashed != null)
            removed = new RemovedNote(window.ViewModel.Model, trashed);
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
