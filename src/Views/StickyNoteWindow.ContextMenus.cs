// ScreenStickyNotes - a desktop sticky notes app for Windows 11
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

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using ScreenStickyNotes.Models;
using ScreenStickyNotes.Services;
using ScreenStickyNotes.ViewModels;
using SkiaSharp;
using WpfBrushes     = System.Windows.Media.Brushes;
using WpfButton      = System.Windows.Controls.Button;
using WpfBitmapImage = System.Windows.Media.Imaging.BitmapImage;
using WpfCheckBox    = System.Windows.Controls.CheckBox;
using WpfColor       = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfCursors     = System.Windows.Input.Cursors;
using WpfDataFormats = System.Windows.DataFormats;
using WpfFontFamily  = System.Windows.Media.FontFamily;
using WpfImage       = System.Windows.Controls.Image;
using WpfListBox     = System.Windows.Controls.ListBox;
using WpfSolidBrush  = System.Windows.Media.SolidColorBrush;


namespace ScreenStickyNotes.Views;

public partial class StickyNoteWindow
{
    // ─── コンテキストメニュー ────────────────────────────────────

    private ContextMenu BuildContentContextMenu()
    {
        _openLinkItem    = new MenuItem { Header = LocalizationService.T("OpenLink"), IsEnabled = false };
        _convertLinkItem = new MenuItem { Header = LocalizationService.T("ConvertLink"), IsEnabled = false };
        _pasteMarkdownLinkItem = new MenuItem { Header = LocalizationService.T("PasteMarkdownLink"), IsEnabled = false };
        _pasteExcelTableItem = BuildPasteExcelTableMenuItem();
        _copyExcelTableItem = new MenuItem { Header = LocalizationService.T("CopyExcelTable"), IsEnabled = false };
        _fitWindowToImagesItem = new MenuItem { Header = LocalizationService.T("FitWindowToImages"), IsEnabled = false };
        var cutItem = new MenuItem { Header = LocalizationService.T("Cut"), Command = ApplicationCommands.Cut, CommandTarget = ContentBox };
        var pasteItem = new MenuItem { Header = LocalizationService.T("Paste"), Command = ApplicationCommands.Paste, CommandTarget = ContentBox };
        var readOnlyItem = BuildReadOnlyMenuItem();
        var externalItem = BuildExternalContentMenuItem();
        var deleteItem = new MenuItem { Header = LocalizationService.T("Delete") };
        _openLinkItem.Click    += OpenLink_Click;
        _convertLinkItem.Click += ConvertLink_Click;
        _pasteMarkdownLinkItem.Click += PasteMarkdownLink_Click;
        _copyExcelTableItem.Click += CopyExcelTable_Click;
        _fitWindowToImagesItem.Click += (_, _) => FitWindowToMarkdownImages();
        deleteItem.Click += Close_Click;

        var cm = new ContextMenu();
        cm.Items.Add(cutItem);
        cm.Items.Add(new MenuItem { Header = LocalizationService.T("Copy"), Command = ApplicationCommands.Copy, CommandTarget = ContentBox });
        cm.Items.Add(pasteItem);
        cm.Items.Add(_pasteMarkdownLinkItem);
        cm.Items.Add(new Separator());
        cm.Items.Add(_pasteExcelTableItem);
        cm.Items.Add(_copyExcelTableItem);
        cm.Items.Add(new Separator());
        cm.Items.Add(_fitWindowToImagesItem);
        cm.Items.Add(new Separator());
        cm.Items.Add(_openLinkItem);
        cm.Items.Add(_convertLinkItem);
        cm.Items.Add(new Separator());
        cm.Items.Add(externalItem);
        var hideItem = new MenuItem { Header = LocalizationService.T("HideNote") };
        hideItem.Click += (_, _) => App.Current.HideNote(ViewModel.Model.Id);
        cm.Items.Add(hideItem);
        cm.Items.Add(readOnlyItem);
        cm.Items.Add(deleteItem);
        cm.Opened += (_, _) =>
        {
            var canEdit = !IsContentReadOnly();
            cutItem.IsEnabled = canEdit && _isEditMode && ContentBox.Selection.IsEmpty == false;
            pasteItem.IsEnabled = canEdit && _isEditMode && (TryGetClipboardText(out _) || ClipboardHasImage());
            readOnlyItem.IsChecked = ViewModel.IsReadOnly;
            readOnlyItem.IsEnabled = !ViewModel.Model.IsExternalContent;
            externalItem.Visibility = ViewModel.Model.IsExternalContent ? Visibility.Visible : Visibility.Collapsed;
            deleteItem.Header = ViewModel.Model.IsExternalContent
                ? LocalizationService.T("UnlinkExternalNote")
                : LocalizationService.T("Delete");
            deleteItem.IsEnabled = !ViewModel.IsReadOnly || ViewModel.Model.IsExternalContent;
        };
        return cm;
    }

    private ContextMenu BuildTitleContextMenu()
    {
        var editItem = new MenuItem { Header = LocalizationService.T("EditTitle") };
        var cutItem = new MenuItem { Header = LocalizationService.T("Cut") };
        var copyItem = new MenuItem { Header = LocalizationService.T("Copy") };
        var pasteItem = new MenuItem { Header = LocalizationService.T("Paste") };
        var selectAllItem = new MenuItem { Header = LocalizationService.T("SelectAll") };
        var zOrderItem = new MenuItem { Header = LocalizationService.T("ZOrder") };
        var opacityItem = BuildOpacityMenuItem();
        var readOnlyItem = BuildReadOnlyMenuItem();
        var externalItem = BuildExternalContentMenuItem();
        var setUnfoldedPositionItem = new MenuItem { Header = LocalizationService.T("SetUnfoldedPositionHere") };
        var bringToFrontItem = new MenuItem { Header = LocalizationService.T("BringToFront") };
        var sendToBackItem = new MenuItem { Header = LocalizationService.T("SendToBack") };
        var hideItem = new MenuItem { Header = LocalizationService.T("HideNote") };
        var deleteItem = new MenuItem { Header = LocalizationService.T("Delete") };
        var editSeparator = new Separator();

        editItem.Click += (_, _) => EnterTitleEditMode();
        cutItem.Click += (_, _) => TitleEditBox.Cut();
        copyItem.Click += (_, _) =>
        {
            if (_isEditMode && TitleEditBox.Visibility == Visibility.Visible &&
                TitleEditBox.SelectionLength > 0)
                TitleEditBox.Copy();
            else if (!string.IsNullOrEmpty(ViewModel.DisplayTitle))
                TrySetClipboardText(ViewModel.DisplayTitle);
        };
        pasteItem.Click += (_, _) => TitleEditBox.Paste();
        selectAllItem.Click += (_, _) => TitleEditBox.SelectAll();
        setUnfoldedPositionItem.Click += (_, _) => SetUnfoldedPositionHere();
        bringToFrontItem.Click += (_, _) => MoveInZOrder(HwndTop);
        sendToBackItem.Click += (_, _) => MoveInZOrder(HwndBottom);
        hideItem.Click += (_, _) => App.Current.HideNote(ViewModel.Model.Id);
        deleteItem.Click += Close_Click;
        zOrderItem.Items.Add(bringToFrontItem);
        zOrderItem.Items.Add(sendToBackItem);

        var cm = new ContextMenu();
        cm.Items.Add(editItem);
        cm.Items.Add(editSeparator);
        cm.Items.Add(cutItem);
        cm.Items.Add(copyItem);
        cm.Items.Add(pasteItem);
        cm.Items.Add(selectAllItem);
        cm.Items.Add(new Separator());
        cm.Items.Add(zOrderItem);
        cm.Items.Add(opacityItem);
        cm.Items.Add(setUnfoldedPositionItem);
        cm.Items.Add(externalItem);
        cm.Items.Add(readOnlyItem);
        cm.Items.Add(new Separator());
        cm.Items.Add(hideItem);
        cm.Items.Add(deleteItem);
        cm.Opened += (_, _) =>
        {
            bool editing = cm.PlacementTarget == TitleEditBox && _isEditMode;
            var canEdit = !IsContentReadOnly();
            editItem.Visibility = editing || !canEdit ? Visibility.Collapsed : Visibility.Visible;
            editSeparator.Visibility = editItem.Visibility;
            cutItem.Visibility = editing && canEdit ? Visibility.Visible : Visibility.Collapsed;
            pasteItem.Visibility = editing && canEdit ? Visibility.Visible : Visibility.Collapsed;
            selectAllItem.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            copyItem.IsEnabled = editing
                ? TitleEditBox.SelectionLength > 0
                : !string.IsNullOrEmpty(ViewModel.DisplayTitle);
            cutItem.IsEnabled = canEdit && TitleEditBox.SelectionLength > 0;
            pasteItem.IsEnabled = canEdit && TryGetClipboardText(out _);
            setUnfoldedPositionItem.IsEnabled = ViewModel.IsFolded;
            readOnlyItem.IsChecked = ViewModel.IsReadOnly;
            readOnlyItem.IsEnabled = !ViewModel.Model.IsExternalContent;
            externalItem.Visibility = ViewModel.Model.IsExternalContent ? Visibility.Visible : Visibility.Collapsed;
            deleteItem.Header = ViewModel.Model.IsExternalContent
                ? LocalizationService.T("UnlinkExternalNote")
                : LocalizationService.T("Delete");
            deleteItem.IsEnabled = !ViewModel.IsReadOnly || ViewModel.Model.IsExternalContent;
            UpdateOpacityMenuChecks(opacityItem);
        };
        cm.Closed += (_, _) =>
        {
            _suppressViewMode = false;
            if (_isEditMode && TitleEditBox.Visibility == Visibility.Visible)
                Dispatcher.BeginInvoke(() => TitleEditBox.Focus());
        };
        return cm;
    }

    private MenuItem BuildReadOnlyMenuItem()
    {
        var item = new MenuItem
        {
            Header = LocalizationService.T("EditLock"),
            IsCheckable = true,
            IsChecked = ViewModel.IsReadOnly,
        };
        item.Click += (_, _) => ToggleReadOnly();
        return item;
    }

    private void ToggleReadOnly()
    {
        if (ViewModel.Model.IsExternalContent)
            return;

        if (_isEditMode)
            EnterViewMode();

        ViewModel.IsReadOnly = !ViewModel.IsReadOnly;
        RequestSave();
    }

    private MenuItem BuildExternalContentMenuItem()
    {
        var item = new MenuItem { Header = LocalizationService.T("ExternalFile") };
        item.Items.Add(new MenuItem
        {
            Header = LocalizationService.T("OpenExternalFile"),
            Command = new RelayCommand(_ => OpenExternalFile()),
        });
        item.Items.Add(new MenuItem
        {
            Header = LocalizationService.T("OpenExternalFolder"),
            Command = new RelayCommand(_ => OpenExternalFolder()),
        });
        item.Items.Add(new Separator());
        item.Items.Add(new MenuItem
        {
            Header = LocalizationService.T("ConvertExternalToNormal"),
            Command = new RelayCommand(_ => ConvertExternalToNormalNote()),
        });
        return item;
    }

    private MenuItem BuildOpacityMenuItem()
    {
        var opacityItem = new MenuItem { Header = LocalizationService.T("Opacity") };
        for (var percent = 10; percent <= 100; percent += 10)
        {
            var percentItem = new MenuItem
            {
                Header = $"{percent}%",
                IsCheckable = true,
                IsChecked = ViewModel.OpacityPercent == percent,
            };
            var selectedPercent = percent;
            percentItem.Click += (_, _) => SetNoteOpacity(selectedPercent);
            opacityItem.Items.Add(percentItem);
        }
        return opacityItem;
    }

    private void UpdateOpacityMenuChecks(MenuItem opacityItem)
    {
        foreach (var item in opacityItem.Items.OfType<MenuItem>())
        {
            if (item.Header is string header && header.EndsWith("%", StringComparison.Ordinal) &&
                int.TryParse(header[..^1], out var percent))
            {
                item.IsChecked = ViewModel.OpacityPercent == percent;
            }
        }
    }

    private void SetNoteOpacity(int percent)
    {
        ViewModel.OpacityPercent = percent;
        RequestSave();
    }

    private void SetUnfoldedPositionHere()
    {
        ViewModel.Model.X = Left;
        ViewModel.Model.Y = Top;
        RequestSave();
    }

    private void MoveInZOrder(IntPtr insertAfter)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
            SetWindowPosFlags.NoMove |
            SetWindowPosFlags.NoSize |
            SetWindowPosFlags.NoActivate);
    }

    private void TitleContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender == TitleEditBox)
            _suppressViewMode = true;
    }

    private void ContentBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_suppressNextContentContextMenu)
        {
            _suppressNextContentContextMenu = false;
            e.Handled = true;
            return;
        }

        // メニューが実際に開く前にフラグを立てる。ここで立てないと、
        // メニューが開く際のフォーカス移動で LostKeyboardFocus が先に発火し
        // EnterViewMode() が走ってしまう（ドキュメント再構築・IsReadOnly=true）。
        _suppressViewMode = true;
        _isContentContextMenuOpen = true;

        _contextMenuLink = GetHyperlinkAtCaret();
        _openLinkItem.IsEnabled = _contextMenuLink != null;

        var sel = ContentBox.Selection.IsEmpty ? "" : ContentBox.Selection.Text.Trim();
        var hasClipboardLink = TryGetClipboardText(out var clipboardText) &&
            LinkDetector.IsExactLink(clipboardText);
        _convertLinkItem.IsEnabled = _isEditMode && LinkDetector.IsExactLink(sel);
        if (IsContentReadOnly())
            _convertLinkItem.IsEnabled = false;
        _copyExcelTableItem.IsEnabled = MarkdownTableClipboard.TryCopyableTableTextToTabularText(sel, out _);
        _pasteMarkdownLinkItem.IsEnabled = !IsContentReadOnly() && _isEditMode && hasClipboardLink;
        _pasteExcelTableItem.IsEnabled =
            !IsContentReadOnly() &&
            _isEditMode &&
            TryGetClipboardText(out clipboardText) &&
            MarkdownTableClipboard.TryTabularTextToMarkdownTable(clipboardText, useFirstRowAsHeader: true, out _);
        _fitWindowToImagesItem.IsEnabled = !_isEditMode && _markdownImageContexts.Count > 0;

        ShowEditToolbar();
    }

    private MenuItem BuildPasteExcelTableMenuItem()
    {
        var pasteItem = new MenuItem { Header = LocalizationService.T("PasteExcelTable"), IsEnabled = false };
        var withHeaderItem = new MenuItem { Header = LocalizationService.T("PasteExcelTableWithHeader") };
        var withoutHeaderItem = new MenuItem { Header = LocalizationService.T("PasteExcelTableWithoutHeader") };
        withHeaderItem.Click += PasteExcelTable_Click;
        withoutHeaderItem.Click += PasteExcelTableWithoutHeader_Click;
        pasteItem.Items.Add(withHeaderItem);
        pasteItem.Items.Add(withoutHeaderItem);
        return pasteItem;
    }

    private Hyperlink? GetHyperlinkAtCaret()
    {
        var el = ContentBox.CaretPosition.Parent as TextElement;
        while (el != null)
        {
            if (el is Hyperlink h) return h;
            el = el.Parent as TextElement;
        }
        return null;
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuLink?.Tag is string t) OpenTarget(t);
    }

    private void PasteMarkdownLink_Click(object sender, RoutedEventArgs e)
    {
        if (IsContentReadOnly()) return;
        if (!_isEditMode) return;
        if (!TryGetClipboardText(out var target)) return;

        target = target.Trim();
        if (!LinkDetector.IsExactLink(target)) return;

        var label = ShowMarkdownLinkLabelDialog(GetDefaultMarkdownLinkLabel(target));
        if (label == null)
            return;
        if (label.Length == 0)
            label = target;

        InsertTextAtSelection(BuildMarkdownLink(label, target));
    }

    private void ConvertLink_Click(object sender, RoutedEventArgs e)
    {
        if (IsContentReadOnly()) return;
        if (!_isEditMode) return;
        if (ContentBox.Selection.IsEmpty) return;
        var sel = ContentBox.Selection.Text.Trim();
        if (!LinkDetector.IsExactLink(sel)) return;

        // 選択範囲を明示的な Markdown リンクに置換してドキュメント全体を再構築
        var plainText = GetPlainText();
        var before   = plainText[..GetOffsetOfPointer(ContentBox.Selection.Start)];
        var after    = plainText[GetOffsetOfPointer(ContentBox.Selection.End)..];
        var markdown = LinkDetector.IsImageTarget(sel)
            ? BuildMarkdownImage(sel)
            : BuildMarkdownLink(sel, sel);
        var newText  = before + markdown + after;
        var caretOff = before.Length + markdown.Length;

        LoadPlainContent(newText);
        RestoreCaretAt(caretOff);

        ViewModel.Content = newText;
        RequestSave();
    }

    private string? ShowMarkdownLinkLabelDialog(string defaultLabel)
    {
        var input = new System.Windows.Controls.TextBox
        {
            Text = defaultLabel,
            MinWidth = 320,
            Margin = new Thickness(0, 6, 0, 12),
        };
        input.SelectAll();

        var okButton = new System.Windows.Controls.Button
        {
            Content = "OK",
            IsDefault = true,
            MinWidth = 72,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var cancelButton = new System.Windows.Controls.Button
        {
            Content = LocalizationService.T("Cancel"),
            IsCancel = true,
            MinWidth = 72,
        };
        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = LocalizationService.T("MarkdownLinkLabelPrompt") });
        panel.Children.Add(input);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = LocalizationService.T("MarkdownLinkLabelTitle"),
            Content = panel,
            Owner = this,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Topmost = Topmost,
        };
        okButton.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) => input.Focus();

        return dialog.ShowDialog() == true
            ? input.Text.Trim()
            : null;
    }

    private static string GetDefaultMarkdownLinkLabel(string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? uri.Host[4..]
                : uri.Host;
        }

        var trimmed = target.TrimEnd('\\', '/');
        var fileName = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(fileName) ? target : fileName;
    }

    private static string BuildMarkdownLink(string label, string target)
        => $"[{EscapeMarkdownLinkLabel(label)}]({target})";

    private static string BuildMarkdownImage(string target)
        => $"![image]({target})";

    private static string EscapeMarkdownLinkLabel(string label)
        => label.Replace("\\", "\\\\").Replace("]", "\\]");

}
