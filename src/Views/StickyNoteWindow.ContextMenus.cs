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
        _pasteExcelTableItem = BuildPasteExcelTableMenuItem();
        _copyExcelTableItem = new MenuItem { Header = LocalizationService.T("CopyExcelTable"), IsEnabled = false };
        _openLinkItem.Click    += OpenLink_Click;
        _convertLinkItem.Click += ConvertLink_Click;
        _copyExcelTableItem.Click += CopyExcelTable_Click;

        var cm = new ContextMenu();
        cm.Items.Add(new MenuItem { Header = LocalizationService.T("Cut"), Command = ApplicationCommands.Cut, CommandTarget = ContentBox });
        cm.Items.Add(new MenuItem { Header = LocalizationService.T("Copy"), Command = ApplicationCommands.Copy, CommandTarget = ContentBox });
        cm.Items.Add(new MenuItem { Header = LocalizationService.T("Paste"), Command = ApplicationCommands.Paste, CommandTarget = ContentBox });
        cm.Items.Add(new Separator());
        cm.Items.Add(_pasteExcelTableItem);
        cm.Items.Add(_copyExcelTableItem);
        cm.Items.Add(new Separator());
        cm.Items.Add(_openLinkItem);
        cm.Items.Add(_convertLinkItem);
        cm.Items.Add(new Separator());
        var deleteItem = new MenuItem { Header = LocalizationService.T("Delete") };
        deleteItem.Click += Close_Click;
        cm.Items.Add(deleteItem);
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
        var bringToFrontItem = new MenuItem { Header = LocalizationService.T("BringToFront") };
        var sendToBackItem = new MenuItem { Header = LocalizationService.T("SendToBack") };
        var deleteItem = new MenuItem { Header = LocalizationService.T("Delete") };

        editItem.Click += (_, _) => EnterTitleEditMode();
        cutItem.Click += (_, _) => TitleEditBox.Cut();
        copyItem.Click += (_, _) =>
        {
            if (_isEditMode && TitleEditBox.Visibility == Visibility.Visible &&
                TitleEditBox.SelectionLength > 0)
                TitleEditBox.Copy();
            else if (!string.IsNullOrEmpty(ViewModel.DisplayTitle))
                System.Windows.Clipboard.SetText(ViewModel.DisplayTitle);
        };
        pasteItem.Click += (_, _) => TitleEditBox.Paste();
        selectAllItem.Click += (_, _) => TitleEditBox.SelectAll();
        bringToFrontItem.Click += (_, _) => MoveInZOrder(HwndTop);
        sendToBackItem.Click += (_, _) => MoveInZOrder(HwndBottom);
        deleteItem.Click += Close_Click;
        zOrderItem.Items.Add(bringToFrontItem);
        zOrderItem.Items.Add(sendToBackItem);

        var cm = new ContextMenu();
        cm.Items.Add(editItem);
        cm.Items.Add(new Separator());
        cm.Items.Add(cutItem);
        cm.Items.Add(copyItem);
        cm.Items.Add(pasteItem);
        cm.Items.Add(selectAllItem);
        cm.Items.Add(new Separator());
        cm.Items.Add(zOrderItem);
        cm.Items.Add(opacityItem);
        cm.Items.Add(new Separator());
        cm.Items.Add(deleteItem);
        cm.Opened += (_, _) =>
        {
            bool editing = cm.PlacementTarget == TitleEditBox && _isEditMode;
            editItem.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            cutItem.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            pasteItem.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            selectAllItem.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            copyItem.IsEnabled = editing
                ? TitleEditBox.SelectionLength > 0
                : !string.IsNullOrEmpty(ViewModel.DisplayTitle);
            cutItem.IsEnabled = TitleEditBox.SelectionLength > 0;
            pasteItem.IsEnabled = System.Windows.Clipboard.ContainsText();
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
        // メニューが実際に開く前にフラグを立てる。ここで立てないと、
        // メニューが開く際のフォーカス移動で LostKeyboardFocus が先に発火し
        // EnterViewMode() が走ってしまう（ドキュメント再構築・IsReadOnly=true）。
        _suppressViewMode = true;
        _isContentContextMenuOpen = true;

        _contextMenuLink = GetHyperlinkAtCaret();
        _openLinkItem.IsEnabled = _contextMenuLink != null;

        var sel = ContentBox.Selection.IsEmpty ? "" : ContentBox.Selection.Text.Trim();
        _convertLinkItem.IsEnabled = LinkDetector.IsLink(sel);
        _copyExcelTableItem.IsEnabled = MarkdownTableClipboard.TryCopyableTableTextToTabularText(sel, out _);
        _pasteExcelTableItem.IsEnabled =
            _isEditMode &&
            System.Windows.Clipboard.ContainsText() &&
            MarkdownTableClipboard.TryTabularTextToMarkdownTable(System.Windows.Clipboard.GetText(), useFirstRowAsHeader: true, out _);

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

    private void ConvertLink_Click(object sender, RoutedEventArgs e)
    {
        if (ContentBox.Selection.IsEmpty) return;
        var sel = ContentBox.Selection.Text.Trim();
        if (!LinkDetector.IsLink(sel)) return;

        // 選択範囲をリンクに置換してドキュメント全体を再構築
        var plainText = GetPlainText();
        var before   = plainText[..GetOffsetOfPointer(ContentBox.Selection.Start)];
        var after    = plainText[GetOffsetOfPointer(ContentBox.Selection.End)..];
        var newText  = before + sel + after;    // sel は URL なので LoadPlainContent でリンク検出される
        var caretOff = before.Length + sel.Length;

        LoadPlainContent(newText);
        RestoreCaretAt(caretOff);

        ViewModel.Content = newText;
        RequestSave();
    }

}
