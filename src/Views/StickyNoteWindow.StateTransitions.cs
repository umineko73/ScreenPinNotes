using ScreenPinNotes.Services;

namespace ScreenPinNotes.Views;

public partial class StickyNoteWindow
{
    private enum DisplayMode { Folded, View, BodyEdit, TitleEdit }

    // All mode requests settle the previous transition before reading bounds or
    // applying edit sizes. The core methods only apply their own presentation.
    private void TransitionTo(DisplayMode target, Action? onUnfolded = null)
    {
        if (_isClosed) return;
        if ((target is DisplayMode.BodyEdit or DisplayMode.TitleEdit) && IsContentReadOnly())
        {
            ShowSizeOverlay(LocalizationService.T("EditLockNotice"));
            return;
        }

        CompleteFoldAnimation();
        if (target == DisplayMode.Folded)
        {
            if (!ViewModel.IsFolded) ApplyFoldState(true);
            return;
        }

        if (ViewModel.IsFolded)
        {
            ApplyFoldState(false, () =>
            {
                if (_isClosed) return;
                if (target is DisplayMode.BodyEdit or DisplayMode.TitleEdit)
                    TransitionTo(target);
                onUnfolded?.Invoke();
            });
            return;
        }

        switch (target)
        {
            case DisplayMode.BodyEdit: EnterEditModeCore(); break;
            case DisplayMode.TitleEdit: EnterTitleEditModeCore(); break;
            case DisplayMode.View: EnterViewModeCore(); break;
        }
        onUnfolded?.Invoke();
    }

    private void ToggleFold(Action? onUnfolded = null)
        => TransitionTo(ViewModel.IsFolded ? DisplayMode.View : DisplayMode.Folded, onUnfolded);

    private void EnterEditMode() => TransitionTo(DisplayMode.BodyEdit);
    private void EnterTitleEditMode() => TransitionTo(DisplayMode.TitleEdit);
    private void EnterViewMode()
    {
        if (_isEditMode && !_suppressViewMode) TransitionTo(DisplayMode.View);
    }
}
