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

using System.Windows;
using System.Windows.Controls;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Views;

public partial class StickyNoteWindow
{
    private MenuItem BuildMarkdownFormattingMenu()
    {
        var menu = new MenuItem { Header = LocalizationService.T("MarkdownFormatting") };
        foreach (var (key, marker, line) in new[]
        {
            ("FormatBold", "**", false), ("FormatItalic", "*", false),
            ("FormatStrike", "~~", false), ("FormatHighlight", "==", false),
            ("FormatCode", "`", false),
            ("FormatHeading1", "# ", true), ("FormatHeading2", "## ", true), ("FormatHeading3", "### ", true),
            ("FormatBullets", "- ", true), ("FormatNumbered", "1. ", true), ("FormatTasks", "- [ ] ", true),
            ("FormatQuote", "> ", true),
        })
        {
            var item = new MenuItem { Header = LocalizationService.T(key) };
            item.Click += (_, _) => ApplyMarkdownFormat(marker, line);
            menu.Items.Add(item);
        }
        var linkItem = new MenuItem { Header = LocalizationService.T("FormatLink") };
        linkItem.Click += (_, _) => InsertOrEditMarkdownLink();
        var clearItem = new MenuItem { Header = LocalizationService.T("FormatClear") };
        clearItem.Click += (_, _) => ClearMarkdownFormatting();
        menu.Items.Add(new Separator());
        menu.Items.Add(linkItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(clearItem);
        return menu;
    }

    private void ApplyMarkdownFormat(string marker, bool line)
    {
        if (!IsBodyEditing() || IsContentReadOnly()) return;
        var edit = line
            ? MarkdownFormatting.Lines(BodyEditBox.Text, BodyEditBox.SelectionStart, BodyEditBox.SelectionLength, marker)
            : MarkdownFormatting.Inline(BodyEditBox.Text, BodyEditBox.SelectionStart, BodyEditBox.SelectionLength, marker);
        ApplyMarkdownEdit(edit);
    }

    private void ClearMarkdownFormatting()
    {
        if (!IsBodyEditing() || IsContentReadOnly()) return;
        ApplyMarkdownEdit(MarkdownFormatting.Clear(BodyEditBox.Text, BodyEditBox.SelectionStart, BodyEditBox.SelectionLength));
    }

    /// <summary>
    /// 選んだ文字をリンクにする。カーソルが既存のリンクの上なら、そのリンクを編集する。
    /// ダイアログはメニューやツールバーが閉じてから開く。
    /// </summary>
    private void InsertOrEditMarkdownLink()
    {
        var start = BodyEditBox.SelectionStart;
        var length = BodyEditBox.SelectionLength;
        var existing = MarkdownLinkEditor.FindAt(BodyEditBox.Text, start);
        OpenPickerAfterContextMenuClosed(() =>
        {
            if (!IsBodyEditing() || IsContentReadOnly()) return;
            if (existing != null) { EditMarkdownLink(existing); return; }
            var original = BodyEditBox.Text;
            if (!MarkdownFormatting.IsRangeValid(original, start, length)) return;
            _isLinkEditDialogOpen = true;
            _suppressViewMode = true;
            HideEditToolbar();
            try
            {
                var dialog = new LinkEditDialog(this, original.Substring(start, length), "");
                if (dialog.ShowDialog() != true || original != BodyEditBox.Text) return;
                var replacement = MarkdownLinkFormatter.Build(dialog.LinkLabel, dialog.LinkTarget);
                ApplyMarkdownEdit(new(start, length, replacement, start, replacement.Length));
            }
            finally
            {
                _isLinkEditDialogOpen = false;
                _suppressViewMode = false;
                if (!_isClosed && IsVisible) { BodyEditBox.Focus(); ShowEditToolbar(); }
            }
        });
    }

    private void ApplyMarkdownEdit(MarkdownFormatting.Edit edit)
    {
        if (!IsBodyEditing() || IsContentReadOnly()) return;
        var original = BodyEditBox.Text;
        if (!MarkdownFormatting.IsRangeValid(original, edit.Start, edit.Length)) return;
        var result = original.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        if (!MarkdownFormatting.IsRangeValid(result, edit.SelectionStart, edit.SelectionLength)) return;
        if (!CanAcceptNoteContent(result)) return;
        var selectionStart = BodyEditBox.SelectionStart;
        var selectionLength = BodyEditBox.SelectionLength;
        var previousSuppression = _suppressTextChange;
        BodyEditBox.BeginChange();
        _suppressTextChange = true;
        try
        {
            BodyEditBox.Select(edit.Start, edit.Length);
            BodyEditBox.SelectedText = edit.Replacement;
            BodyEditBox.Select(edit.SelectionStart, edit.SelectionLength);
            if (!TrySetNoteContent(result)) throw new InvalidOperationException("Markdown edit was rejected.");
        }
        catch (Exception ex)
        {
            BodyEditBox.Text = original;
            BodyEditBox.Select(selectionStart, selectionLength);
            ViewModel.Content = NormalizeLineEndings(original);
            RequestSave();
            ErrorReporter.ReportNonFatal("Apply Markdown formatting; original restored", ex);
        }
        finally
        {
            _suppressTextChange = previousSuppression;
            BodyEditBox.EndChange();
        }
    }
}
