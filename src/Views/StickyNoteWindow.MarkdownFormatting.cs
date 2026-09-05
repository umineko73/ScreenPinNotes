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
            ("FormatBold", "**", false), ("FormatStrike", "~~", false), ("FormatCode", "`", false),
            ("FormatHeading1", "# ", true), ("FormatHeading2", "## ", true), ("FormatHeading3", "### ", true),
            ("FormatBullets", "- ", true), ("FormatTasks", "- [ ] ", true),
        })
        {
            var item = new MenuItem { Header = LocalizationService.T(key) };
            item.Click += (_, _) =>
            {
                if (!IsBodyEditing() || IsContentReadOnly()) return;
                var edit = line
                    ? MarkdownFormatting.Lines(BodyEditBox.Text, BodyEditBox.SelectionStart, BodyEditBox.SelectionLength, marker)
                    : MarkdownFormatting.Inline(BodyEditBox.Text, BodyEditBox.SelectionStart, BodyEditBox.SelectionLength, marker);
                ApplyMarkdownEdit(edit);
            };
            menu.Items.Add(item);
        }
        var linkItem = new MenuItem { Header = LocalizationService.T("FormatLink") };
        linkItem.Click += (_, _) =>
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
        };
        menu.Items.Add(new Separator());
        menu.Items.Add(linkItem);
        return menu;
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
            ViewModel.Content = original;
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
