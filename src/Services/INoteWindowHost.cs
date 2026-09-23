using ScreenPinNotes.Models;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Services;

/// <summary>Application operations requested by an individual note window.</summary>
public interface INoteWindowHost
{
    bool HideNoteOnWindowClose(StickyNoteWindow window);
    void HideNote(string id);
    bool RemoveNote(string id);
    void AddNewNote(StickyNote? template = null, double? x = null, double? y = null, double? scale = null);
    StickyNoteWindow DuplicateNote(StickyNoteWindow window);
    void MoveNoteLayers(ISet<string> ids, LayerMove move);
    void SetReminder(string id, DateTime? nextAt, ReminderSettings? options = null);
    void NoteTouched(StickyNoteWindow window);
}
