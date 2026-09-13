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
