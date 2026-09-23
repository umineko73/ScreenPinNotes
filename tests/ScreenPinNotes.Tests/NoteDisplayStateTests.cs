using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class NoteDisplayStateTests
{
    [Fact]
    public void TitleFocusKeepsBodyEditor_AndFoldClearsEditing()
    {
        var state = new NoteDisplayState(false);
        state.BeginBodyEdit();
        state.BeginTitleEdit();
        Assert.Equal(NoteDisplayMode.BodyEdit, state.Mode);
        state.SetFolded(true);
        Assert.False(state.IsEditing);
        Assert.Equal(NoteDisplayMode.Folded, state.Mode);
        state.SetFolded(false);
        Assert.Equal(NoteDisplayMode.View, state.Mode);
        state.BeginTitleEdit();
        Assert.Equal(NoteDisplayMode.TitleEdit, state.Mode);
    }

    [Theory]
    [InlineData(NoteDisplayMode.BodyEdit, false, false, true, NoteTransitionPermission.ReadOnly)]
    [InlineData(NoteDisplayMode.TitleEdit, false, false, true, NoteTransitionPermission.ReadOnly)]
    [InlineData(NoteDisplayMode.Folded, false, false, true, NoteTransitionPermission.Allowed)]
    [InlineData(NoteDisplayMode.View, true, false, false, NoteTransitionPermission.Busy)]
    [InlineData(NoteDisplayMode.BodyEdit, false, true, false, NoteTransitionPermission.Busy)]
    public void TransitionRulesAreIndependentOfWindowControls(NoteDisplayMode target, bool closed,
        bool sizing, bool readOnly, NoteTransitionPermission expected)
        => Assert.Equal(expected, NoteDisplayState.CheckRequest(target, closed, sizing, readOnly));
}
