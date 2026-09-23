namespace ScreenPinNotes.Services;

public enum NoteDisplayMode { Folded, View, BodyEdit, TitleEdit }
public enum NoteTransitionPermission { Allowed, Busy, ReadOnly }

/// <summary>Persistent presentation mode, independent of focus, animations and WPF controls.</summary>
public sealed class NoteDisplayState(bool folded)
{
    public NoteDisplayMode Mode { get; private set; } = folded ? NoteDisplayMode.Folded : NoteDisplayMode.View;
    public bool IsEditing => Mode is NoteDisplayMode.BodyEdit or NoteDisplayMode.TitleEdit;

    public static NoteTransitionPermission CheckRequest(NoteDisplayMode target, bool closed, bool sizing, bool readOnly)
        => closed || sizing ? NoteTransitionPermission.Busy
            : readOnly && target is NoteDisplayMode.BodyEdit or NoteDisplayMode.TitleEdit
                ? NoteTransitionPermission.ReadOnly : NoteTransitionPermission.Allowed;

    public void BeginBodyEdit() => Mode = NoteDisplayMode.BodyEdit;

    // Focusing the title while the body editor is open keeps both editors available.
    public void BeginTitleEdit()
    {
        if (Mode != NoteDisplayMode.BodyEdit) Mode = NoteDisplayMode.TitleEdit;
    }

    public void EndEditing(bool folded) => Mode = folded ? NoteDisplayMode.Folded : NoteDisplayMode.View;

    public void SetFolded(bool folded)
    {
        if (folded || Mode == NoteDisplayMode.Folded) EndEditing(folded);
    }
}
