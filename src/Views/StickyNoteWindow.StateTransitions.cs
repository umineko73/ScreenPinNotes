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

using ScreenPinNotes.Services;

namespace ScreenPinNotes.Views;

public partial class StickyNoteWindow
{

    // All mode requests settle the previous transition before reading bounds or
    // applying edit sizes. The core methods only apply their own presentation.
    private void TransitionTo(NoteDisplayMode target, Action? onUnfolded = null)
    {
        var permission = NoteDisplayState.CheckRequest(target, _isClosed, IsMouseSizingGesture, IsContentReadOnly());
        if (permission == NoteTransitionPermission.Busy) return;
        if (DiagnosticTrace.Enabled)
            Trace($"TransitionTo {target} blocked={IsMouseSizingGesture} caller={DiagnosticTrace.Caller()}");
        // 辺をドラッグしている最中は切り替えない（IsMouseSizingGesture 参照）。
        if (permission == NoteTransitionPermission.ReadOnly)
        {
            ShowSizeOverlay(LocalizationService.T("EditLockNotice"));
            return;
        }

        CompleteFoldAnimation();
        if (target == NoteDisplayMode.Folded)
        {
            if (!ViewModel.IsFolded) ApplyFoldState(true);
            return;
        }

        if (ViewModel.IsFolded)
        {
            ApplyFoldState(false, () =>
            {
                if (_isClosed) return;
                if (target is NoteDisplayMode.BodyEdit or NoteDisplayMode.TitleEdit)
                    TransitionTo(target);
                onUnfolded?.Invoke();
            });
            return;
        }

        switch (target)
        {
            case NoteDisplayMode.BodyEdit: EnterEditModeCore(); break;
            case NoteDisplayMode.TitleEdit: EnterTitleEditModeCore(); break;
            case NoteDisplayMode.View: EnterViewModeCore(); break;
        }
        onUnfolded?.Invoke();
    }

    private void ToggleFold(Action? onUnfolded = null)
        => TransitionTo(ViewModel.IsFolded ? NoteDisplayMode.View : NoteDisplayMode.Folded, onUnfolded);

    private void EnterEditMode() => TransitionTo(NoteDisplayMode.BodyEdit);
    private void EnterTitleEditMode() => TransitionTo(NoteDisplayMode.TitleEdit);
    private void EnterViewMode()
    {
        if (_isEditMode && !_suppressViewMode) TransitionTo(NoteDisplayMode.View);
    }
}
