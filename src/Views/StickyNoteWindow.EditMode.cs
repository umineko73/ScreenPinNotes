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
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
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


namespace ScreenPinNotes.Views;

public partial class StickyNoteWindow
{
    // ─── Edit / View モード ──────────────────────────────────────

    private void EnterEditMode()
    {
        if (IsContentReadOnly())
        {
            ShowSizeOverlay(LocalizationService.T("EditLockNotice"));
            return;
        }

        if (_isEditMode && BodyEditBox.Visibility == Visibility.Visible) return;
        var startingEdit = !_isEditMode;
        _isEditMode = true;
        EditingOutline.Visibility = Visibility.Visible;
        if (startingEdit) ApplyEditingSize(true);
        ViewModel.SetForceOpaque(true);
        _suppressTextChange = true;
        try
        {
            BodyEditBox.Text = ViewModel.Content;
            BodyEditBox.Select(BodyEditBox.Text.Length, 0);
        }
        finally
        {
            _suppressTextChange = false;
        }

        ContentBox.Visibility = Visibility.Collapsed;
        BodyEditBox.Visibility = Visibility.Visible;
        EnableIme(BodyEditBox);
        ContentBox.ToolTip = null;
        // タイトルも同時に編集可能にする。フォーカスは本文に置いたままにし、
        // タイトルを直したい人だけ自分でクリックしてもらう。
        TitleText.Visibility    = Visibility.Collapsed;
        TitleEditBox.Visibility = Visibility.Visible;
        UpdateControlsVisibility();
        if (!BodyEditBox.IsKeyboardFocusWithin)
        {
            BodyEditBox.Focus();
            Keyboard.Focus(BodyEditBox);
        }
        Dispatcher.BeginInvoke(() => EnableImeForFocusedControl(BodyEditBox));
    }

    private bool IsBodyEditing()
        => _isEditMode && BodyEditBox.Visibility == Visibility.Visible;

    private void ApplyEditingSize(bool editing)
    {
        if (ViewModel.IsFolded) return;
        var model = ViewModel.Model;
        static double Valid(double? value, double fallback)
            => value is double size && double.IsFinite(size) && size > 0 ? size : fallback;
        var width = editing ? Valid(model.EditWidth, model.Width) : model.Width;
        var height = editing ? Valid(model.EditHeight, model.Height) : model.Height;
        if (editing)
        {
            // Editing must never make either dimension smaller than expanded view.
            width = Math.Max(width, model.Width);
            height = Math.Max(height, model.Height);
        }
        SuppressWindowBoundsSave(() =>
        {
            Width = Math.Max(MinWidth, width);
            Height = Math.Max(MinHeight, height);
            KeepInsideWorkArea(Width, Height);
            UpdateLayout();
        });
        SaveCurrentPositionToModel();
        UpdateEditToolbarPlacement();
    }

    private void EnterTitleEditMode()
    {
        if (IsContentReadOnly())
        {
            ShowSizeOverlay(LocalizationService.T("EditLockNotice"));
            return;
        }

        ViewModel.SetForceOpaque(true);
        if (!_isEditMode)
        {
            _isEditMode = true;
            ApplyEditingSize(true);
            ContentBox.IsReadOnly = true;
            EnableIme(TitleEditBox);
            BodyEditBox.Visibility = Visibility.Collapsed;
            ContentBox.Visibility = Visibility.Visible;
            ContentBox.Cursor = WpfCursors.Arrow;
            ContentBox.BorderThickness = new Thickness(0);
            ContentBox.BorderBrush = WpfBrushes.Transparent;
            ContentBox.ToolTip = LocalizationService.T("EditBodyTooltip");
        }

        TitleText.Visibility    = Visibility.Collapsed;
        TitleEditBox.Visibility = Visibility.Visible;
        UpdateControlsVisibility();
        TitleEditBox.Focus();
        Keyboard.Focus(TitleEditBox);
        TitleEditBox.SelectAll();
        Dispatcher.BeginInvoke(() => EnableImeForFocusedControl(TitleEditBox));
    }

    private static void EnableIme(System.Windows.Controls.Control control)
    {
        InputMethod.SetIsInputMethodEnabled(control, true);
        InputMethod.SetPreferredImeState(control, InputMethodState.On);
        InputMethod.SetPreferredImeConversionMode(control, ImeConversionModeValues.Native | ImeConversionModeValues.FullShape);
    }

    private static void EnableImeForFocusedControl(System.Windows.Controls.Control control)
    {
        EnableIme(control);
        if (control.IsKeyboardFocusWithin)
        {
            InputMethod.Current.ImeState = InputMethodState.On;
            InputMethod.Current.ImeConversionMode = ImeConversionModeValues.Native | ImeConversionModeValues.FullShape;
        }
    }

    private void EditableControl_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.Control control)
            Dispatcher.BeginInvoke(() => EnableImeForFocusedControl(control));
    }

    private void EnterViewMode()
    {
        if (!_isEditMode || _suppressViewMode) return;
        if (BodyEditBox.Visibility == Visibility.Visible && !TrySetNoteContent(BodyEditBox.Text))
            return;

        _isEditMode = false;
        EditingOutline.Visibility = Visibility.Collapsed;
        ApplyEditingSize(false);
        ViewModel.SetForceOpaque(false);
        // ドキュメントを再構築してMarkdown表示とリンクを正しく復元する
        LoadContent(ViewModel.Content);
        ContentBox.IsReadOnly = true;
        BodyEditBox.Visibility = Visibility.Collapsed;
        ApplyFoldedContentPresentation();
        ContentBox.Cursor = WpfCursors.Arrow;
        ContentBox.BorderThickness = new Thickness(0);
        ContentBox.BorderBrush = WpfBrushes.Transparent;
        ContentBox.ToolTip = LocalizationService.T("EditBodyTooltip");
        TitleText.Visibility    = Visibility.Visible;
        TitleEditBox.Visibility = Visibility.Collapsed;
        UpdateControlsVisibility();
        HideEditToolbar();
        Keyboard.ClearFocus();
    }

    // リサイズ可否を切り替える。
    //
    // ResizeMode は XAML で CanResize 固定にしてある。実行時に切り替えると
    // Window テンプレートが再適用され ResizeGrip が作り直されてしまうため。
    // （CanResize ではグリップ自体がテンプレートに生成されない）
    //
    // 実際のリサイズ抑止は次の2つで行う:
    //   1. WindowChrome.ResizeBorderThickness = 0 … 当たり判定を消す
    //   2. Min/Max を現在値で固定           … 寸法変更そのものを封じる
    //
    // 閉じた表示（enabled=false）でも幅だけは変更できるようにしている。
    // 上下だけ 0 にして左右は残す。開いた表示の上下リサイズは許可し、
    // 上下枠のダブルクリックによる Windows 標準の縦方向最大化だけは
    // WndProc 側で抑止する。
    private void SetResizeEnabled(bool enabled)
    {
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome != null)
        {
            if (chrome.IsFrozen)
            {
                chrome = (WindowChrome)chrome.Clone();
                WindowChrome.SetWindowChrome(this, chrome);
            }
            var resizeBorder = Settings.Layout.ResizeBorder;
            chrome.ResizeBorderThickness = enabled
                ? new Thickness(resizeBorder)
                : new Thickness(resizeBorder, 0, resizeBorder, 0);
        }

        MinWidth = Settings.Layout.UnfoldedMinWidth;
        MaxWidth = double.PositiveInfinity;

        if (enabled)
        {
            MinHeight = FoldedHeight;
            MaxHeight = double.PositiveInfinity;
        }
        else
        {
            MinHeight = MaxHeight = FoldedHeight;
        }
    }

    // 閉じた表示と編集モードの両方を考慮してステータスバーの表示を更新
    private void UpdateControlsVisibility()
    {
        IconButton.Visibility = ViewModel.Model.IsExternalContent
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (_isEditMode && !ViewModel.IsFolded)
            ShowEditToolbar();
        else
            HideEditToolbar();
    }

    private bool ShouldKeepEditToolbarOpen()
        => _isContentContextMenuOpen ||
           RootBorder.IsMouseOver ||
           StatusBar.IsMouseOver ||
           (_colorPopup?.IsOpen ?? false) ||
           (_fontPopup?.IsOpen ?? false) ||
           (_iconPopup?.IsOpen ?? false);

    private void ShowEditToolbar()
    {
        if (!IsActive) { HideEditToolbar(); return; }
        if (!_isEditMode || ViewModel.IsFolded || _isContentContextMenuOpen || _isLinkEditDialogOpen) return;
        _toolbarHideTimer.Stop();
        UpdateEditToolbarPlacement();
        StatusBar.Background = ToolbarBackground;
        StatusBar.BorderBrush = PopupBorderBrush();
        StatusBar.SetValue(TextElement.ForegroundProperty, ViewModel.TextForeground);
        EditToolbarPopup.IsOpen = true;
        foreach (var button in new[] { FontSmallerButton, FontLargerButton, TitleSmallerButton,
            TitleLargerButton, FontButton, IconButton, ColorButton })
            button.Foreground = ViewModel.TextForeground;
    }

    private void EditToolbarPopup_Opened(object? sender, EventArgs e)
    {
        StatusBar.Background = ToolbarBackground;
        StatusBar.BorderBrush = PopupBorderBrush();
        StatusBar.SetValue(TextElement.ForegroundProperty, ViewModel.TextForeground);
        foreach (var button in new[] { FontSmallerButton, FontLargerButton, TitleSmallerButton,
            TitleLargerButton, FontButton, IconButton, ColorButton })
            button.Foreground = ViewModel.TextForeground;
        SyncEditToolbarZOrder();
    }

    private void SyncEditToolbarZOrder()
    {
        if (PresentationSource.FromVisual(StatusBar) is not HwndSource source) return;
        // WPF Popup defaults to HWND_TOPMOST, independently of its owning note.
        SetWindowPos(source.Handle, new IntPtr(Topmost ? -1 : -2), 0, 0, 0, 0,
            SetWindowPosFlags.NoMove | SetWindowPosFlags.NoSize | SetWindowPosFlags.NoActivate);
    }

    private void UpdateEditToolbarPlacement()
    {
        const double Gap = 2;
        EditToolbarPopup.PlacementTarget = RootBorder;
        EditToolbarPopup.Placement = PlacementMode.Bottom;
        // Changing an offset forces WPF to reposition an already open Popup.
        if (EditToolbarPopup.IsOpen)
            EditToolbarPopup.VerticalOffset = Gap + 0.01;
        EditToolbarPopup.VerticalOffset = Gap;
    }

    private void RootBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (EditToolbarPopup?.IsOpen == true)
            UpdateEditToolbarPlacement();
    }

    private void HideEditToolbar()
    {
        _toolbarHideTimer.Stop();
        EditToolbarPopup.IsOpen = false;
    }

    private void Window_Activated(object? sender, EventArgs e)
    {
        if (EditToolbarPopup != null) ShowEditToolbar();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        HideEditToolbar();
    }

    private void ScheduleHideEditToolbar()
    {
        if (_isEditMode && !ViewModel.IsFolded)
            return;

        _toolbarHideTimer.Stop();
        _toolbarHideTimer.Start();
    }

    private void UpdateTitleBarButtonsVisibility()
    {
        var visibility = TitleBar.IsMouseOver
            ? Visibility.Visible
            : Visibility.Collapsed;

        AddNoteButton.Visibility = visibility;
        PinButton.Visibility = visibility;
        FoldButton.Visibility = Settings.ShowFoldButton ? visibility : Visibility.Collapsed;
        // ShowFoldButton はタイトルバーの折りたたみボタンを出すかどうかの設定。
        // タイトルバーを隠しているとそのボタン自体が無く、これを従うと畳む手段が
        // ダブルクリックだけになって見つけられないので、こちらは常に出す。
        OverlayFoldButton.Visibility = Visibility.Visible;
        UpdateTitleBarOverlayVisibility();
    }

    /// <summary>
    /// タイトルバーを隠しているときだけ、右上のオーバーレイをホバー中に出す。
    /// オーバーレイは RootBorder の中にあるので、そこへマウスを移しても
    /// RootBorder の MouseLeave は起きず、ちらつかない。
    /// </summary>
    /// <summary>常時表示のタイトルバーを出すかどうかを反映する。</summary>
    private void ApplyTitleBarVisibility()
        => TitleBar.Visibility = ViewModel.TitleBarVisibility;

    private void UpdateTitleBarOverlayVisibility()
    {
        var active = IsMouseOver || _isDragging;
        // タイトルバーが無いとアイコンが唯一の付箋の見分けになるので、
        // 畳んでいるかどうかにかかわらず出しておく。
        // アイコン未設定の付箋で空の帯だけが浮くのは避ける。
        var showIconAlone = !string.IsNullOrEmpty(ViewModel.Icon);

        TitleBarOverlay.Visibility = ViewModel.IsTitleBarHidden && (active || showIconAlone)
            ? Visibility.Visible
            : Visibility.Collapsed;
        TitleBarOverlayActions.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        TitleBarOverlayBackdrop.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
    }

    // ─── ステータスバーぶんウィンドウを伸縮させる ────────────────
    //
    // ステータスバーを本文と同じ領域に押し込むと、背の低い付箋では
    // 本文が隠れてしまう。編集モードの間だけウィンドウを下に伸ばし、
    // 本文の表示領域を変えないようにする。

    private double _statusBarDelta;

    private void GrowForStatusBar()
    {
        if (_statusBarDelta > 0) return;          // すでに伸ばしてある

        UpdateLayout();                           // 実際の高さを確定させる
        double barHeight = StatusBar.ActualHeight;
        if (barHeight <= 0) return;

        _statusBarDelta = barHeight;

        // 表示切り替えアニメーションが Height プロパティを掴んだままだと、
        // 以下の直接代入がその場では効いても次のレイアウトパスで
        // アニメーションの最終値に上書きされてしまう。先に解除する。
        BeginAnimation(HeightProperty, null);

        // 伸ばす前に上下の制限を緩めておく（閉じた表示用の固定が残っていることがある）
        MaxHeight = double.PositiveInfinity;
        Height += barHeight;

        KeepInsideWorkArea();
    }

    private void ShrinkAfterStatusBar()
    {
        if (_statusBarDelta <= 0) return;

        // Height を変えると SizeChanged が走る。そこで差分を引く処理と
        // 二重に引かないよう、先にクリアしておく。
        double delta = _statusBarDelta;
        _statusBarDelta = 0;
        BeginAnimation(HeightProperty, null); // 同上の理由で解除してから代入する
        Height = Math.Max(MinHeight, Height - delta);
    }

    // 下に伸ばした結果、画面外にはみ出すなら上へずらす
    private void KeepInsideWorkArea()
        => KeepInsideWorkArea(Width, Height);

    private void KeepInsideWorkArea(double targetWidth, double targetHeight)
    {
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var (dpiX, dpiY) = GetDpi();
        double workBottom = screen.WorkingArea.Bottom / dpiY;
        double workTop    = screen.WorkingArea.Top    / dpiY;
        double workRight  = screen.WorkingArea.Right  / dpiX;
        double workLeft   = screen.WorkingArea.Left   / dpiX;

        if (Top + targetHeight > workBottom)
            Top = Math.Max(workTop, workBottom - targetHeight);
        if (Top < workTop)
            Top = workTop;
        if (Left + targetWidth > workRight)
            Left = Math.Max(workLeft, workRight - targetWidth);
        if (Left < workLeft)
            Left = workLeft;
    }

    // View モード: クリックでリンクを直接開く / 非リンクならEdit モードへ
    // Edit モード: Ctrl+クリックでリンクを開く
    private void ContentBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isEditMode && IsDescendantOfType<WpfCheckBox>(e.OriginalSource as DependencyObject))
            return;

        var target = GetHyperlinkAt(e.GetPosition(ContentBox));

        if (_isEditMode && ContentBox.IsReadOnly && !ViewModel.IsFolded)
        {
            EnterEditMode();
            return;
        }

        if (!_isEditMode)
        {
            if (target != null)
            {
                OpenTarget(target);
                e.Handled = true;
                return;
            }
            // シングルクリックでは編集モードに入らない。誤って文字を
            // 選択しただけで編集が始まるのを避けるため、ダブルクリックを要求する。
            if (e.ClickCount == 2)
            {
                // タイトルバーを隠していると、畳んだ状態でも本文が1行だけ見えている。
                // そこで編集に入っても書ける場所がないので、タイトルバーの
                // ダブルクリックと同じくまず開く。
                if (ViewModel.IsFolded)
                {
                    ToggleFold();
                    e.Handled = true;
                    return;
                }
                EnterEditMode();
            }
        }
        else if (target != null && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            OpenTarget(target);
            e.Handled = true;
        }
    }

    private void ContentBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(ContentBox);
        if (scrollViewer == null ||
            scrollViewer.ScrollableWidth <= 0 && scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        _isPaneScrollDragPending = true;
        _paneScrollStartPoint = e.GetPosition(ContentBox);
        _paneScrollStartHorizontalOffset = scrollViewer.HorizontalOffset;
        _paneScrollStartVerticalOffset = scrollViewer.VerticalOffset;
    }

    private void ContentBox_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPaneScrollDragging)
        {
            _isPaneScrollDragPending = false;
            return;
        }

        EndPaneScrollDrag();
        e.Handled = true;
    }

    // View モードでハイパーリンク上にカーソルが来たら Hand に切り替え
    private void ContentBox_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isPaneScrollDragging)
        {
            UpdatePaneScrollDrag(e.GetPosition(ContentBox));
            e.Handled = true;
            return;
        }

        if (_isPaneScrollDragPending && e.RightButton == MouseButtonState.Pressed)
        {
            var current = e.GetPosition(ContentBox);
            if (Math.Abs(current.X - _paneScrollStartPoint.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(current.Y - _paneScrollStartPoint.Y) >= SystemParameters.MinimumVerticalDragDistance)
            {
                BeginPaneScrollDrag(_paneScrollStartPoint);
                UpdatePaneScrollDrag(current);
                e.Handled = true;
                return;
            }
        }
        else
        {
            _isPaneScrollDragPending = false;
        }

        if (_isEditMode) return;
        var target = GetHyperlinkAt(e.GetPosition(ContentBox));
        ContentBox.Cursor = target != null ? WpfCursors.Hand : WpfCursors.Arrow;
    }

    private void ContentBox_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        => EndPaneScrollDrag();

    private void ContentBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // フォーカスが外れても編集モードは維持する。本文の確定は明示操作で行う。
    }

    private void BodyEditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // フォーカスが外れても編集モードは維持する。本文の確定は明示操作で行う。
    }

    private void TitleEditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_suppressViewMode) return;
        if (IsDescendantOf(e.NewFocus as DependencyObject, StatusBar)) return;
        if (IsDescendantOf(e.NewFocus as DependencyObject, ContentBox))
        {
            if (!ViewModel.IsFolded)
                EnterEditMode();
            return;
        }
        if (IsDescendantOf(e.NewFocus as DependencyObject, BodyEditBox))
            return;
        // フォーカスが外れても編集モードは維持する。タイトルの確定は明示操作で行う。
    }

    private static bool IsDescendantOf(DependencyObject? child, DependencyObject? ancestor)
    {
        while (child != null)
        {
            if (child == ancestor) return true;
            child = GetParentObject(child);
        }
        return false;
    }

    private static bool IsDescendantOfType<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T) return true;
            child = GetParentObject(child);
        }
        return false;
    }

    private static DependencyObject? GetParentObject(DependencyObject child)
    {
        if (child is Visual or System.Windows.Media.Media3D.Visual3D)
            return VisualTreeHelper.GetParent(child);

        if (child is FrameworkContentElement fce)
            return fce.Parent;

        return LogicalTreeHelper.GetParent(child);
    }

    private void ContentBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
            _isEditMode)
        {
            EnterViewMode();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z &&
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
            TryUndoLastContentChange(sender))
        {
            e.Handled = true;
            return;
        }

        if (sender == BodyEditBox &&
            e.Key == Key.V &&
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            PasteFromClipboard();
            e.Handled = true;
            return;
        }

        if (sender == TitleEditBox && e.Key == Key.Enter && _isEditMode)
        {
            TitleEditBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            RequestSave();
            FlushPendingSave();
            EnterViewMode();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _isEditMode)
        {
            EnterViewMode();
            e.Handled = true;
        }
    }

    private bool TryUndoLastContentChange(object sender)
    {
        if (sender == BodyEditBox && BodyEditBox.CanUndo)
            return false;
        if (sender == TitleEditBox && TitleEditBox.CanUndo)
            return false;
        if (_contentUndoStack.Count == 0)
            return false;

        var entry = _contentUndoStack.Peek();
        if (!string.Equals(ViewModel.Content, entry.After, StringComparison.Ordinal))
            return false;

        _contentUndoStack.Pop();
        ViewModel.Content = entry.Before;
        RequestSave();
        if (IsBodyEditing())
        {
            BodyEditBox.Text = entry.Before;
            BodyEditBox.Select(BodyEditBox.Text.Length, 0);
        }
        else
        {
            LoadContent(ViewModel.Content);
            ContentBox.Focus();
        }
        return true;
    }

    // テキストポインタを辿りハイパーリンクを探す（ヒットテストのコア）
    private string? GetHyperlinkAt(System.Windows.Point pt)
    {
        var tp = ContentBox.GetPositionFromPoint(pt, snapToText: false);
        if (tp == null) return null;
        var el = tp.Parent as TextElement;
        while (el != null)
        {
            if (el is Hyperlink h && h.Tag is string t) return t;
            el = el.Parent as TextElement;
        }
        return null;
    }

    // ─── テキスト変更 ────────────────────────────────────────────

    private void ContentBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChange) return;
        if (_isTaskCheckboxUpdatePending) return;
        if (!_isEditMode || ContentBox.IsReadOnly) return;
        try
        {
            if (!TrySetNoteContent(GetPlainText()))
                LoadPlainContent(ViewModel.Content);
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Content text changed", ex);
        }
    }

    private void BodyEditBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChange) return;
        if (!_isEditMode || BodyEditBox.Visibility != Visibility.Visible) return;

        if (!TrySetNoteContent(BodyEditBox.Text))
            RevertBodyEditBoxToCurrentContent();
    }

    private bool TrySetNoteContent(string text)
    {
        var normalized = NormalizeLineEndings(text);
        if (!CanAcceptNoteContent(normalized))
        {
            ShowSizeOverlay(string.Format(
                LocalizationService.T("NoteContentTooLarge"),
                FormatByteSize(Settings.MaxNoteContentBytes)));
            return false;
        }

        ViewModel.Content = normalized;
        RequestSave();
        return true;
    }

    private bool CanAcceptNoteContent(string text)
    {
        var nextBytes = Encoding.UTF8.GetByteCount(NormalizeLineEndings(text));
        if (nextBytes <= Settings.MaxNoteContentBytes)
            return true;

        var currentBytes = Encoding.UTF8.GetByteCount(NormalizeLineEndings(ViewModel.Content));
        return currentBytes > Settings.MaxNoteContentBytes && nextBytes <= currentBytes;
    }

    private void RevertBodyEditBoxToCurrentContent()
    {
        var caret = Math.Min(BodyEditBox.SelectionStart, ViewModel.Content.Length);
        _suppressTextChange = true;
        try
        {
            BodyEditBox.Text = ViewModel.Content;
            BodyEditBox.Select(caret, 0);
        }
        finally
        {
            _suppressTextChange = false;
        }
    }

    private static string FormatByteSize(int bytes)
    {
        if (bytes >= 1024 * 1024)
            return FormattableString.Invariant($"{bytes / 1024.0 / 1024.0:0.#} MB");
        if (bytes >= 1024)
            return FormattableString.Invariant($"{bytes / 1024.0:0.#} KB");
        return FormattableString.Invariant($"{bytes} B");
    }

    // Title 自体の値は TwoWay バインディングが更新するので、ここでは保存の予約だけ行う
    private void TitleEditBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsContentReadOnly())
            RequestSave();
    }

}
