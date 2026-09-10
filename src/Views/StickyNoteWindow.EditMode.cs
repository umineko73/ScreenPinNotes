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

    public void StartEditingNewNote()
    {
        // Wait until the creating button/tray menu has finished handling input,
        // so its focus restoration does not steal the new note's caret.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            if (_isClosed || !IsVisible) return;
            Activate();
            EnterEditMode();
        }));
    }

    private void EnterEditModeCore()
    {
        if (_isEditMode && BodyEditBox.Visibility == Visibility.Visible) return;
        var startingEdit = !_isEditMode;
        _isEditMode = true;
        EditingBadge.Visibility = Visibility.Visible;
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
        var (width, height) = _geometry.GetSize(editing);
        SuppressWindowBoundsSave(() =>
        {
            // 編集モードの大きさと同じく、位置も一時的なものとして扱う。
            // 閲覧へ戻すときに記憶してある展開時の左上へ戻さないと、編集中に
            // 左辺・上辺を引っ張ったぶんや、広げた枠を作業領域へ押し戻したぶんだけ、
            // 大きさだけ元へ戻って位置がずれた付箋になる。
            if (!editing)
            {
                Left = model.X;
                Top = model.Y;
            }
            Width = Math.Max(MinWidth, width);
            Height = Math.Max(MinHeight, height);
            KeepInsideWorkArea(Width, Height);
            UpdateLayout();
        });
        // 編集へ入るときは書き戻さない。ここで動くのはアプリ都合の一時的なずれで、
        // ユーザーが決めた位置ではない。戻すときだけ、作業領域からはみ出して
        // 補正された場合に備えて記録し直す。
        if (!editing)
        {
            var (dpiX, dpiY) = GetDpi();
            _geometry.StorePosition(
                NoteGeometryState.PreserveLogicalValue(model.X, Left, dpiX),
                NoteGeometryState.PreserveLogicalValue(model.Y, Top, dpiY));
        }
        UpdateEditToolbarPlacement();
    }

    private void EnterTitleEditModeCore()
    {
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
        // タイトルバーを隠していると入力欄ごと消えていて、見えない欄に
        // フォーカスだけが入ってしまう。タイトルを編集している間だけ出す。
        // 元に戻すのは、入力欄を閉じる側の ApplyTitleBarVisibility()。
        if (ViewModel.IsTitleBarHidden)
            TitleBar.Visibility = Visibility.Visible;
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
        if (sender is System.Windows.Controls.TextBox editor)
        {
            UndoButton.CommandTarget = editor;
            RedoButton.CommandTarget = editor;
        }
        if (sender is System.Windows.Controls.Control control)
            Dispatcher.BeginInvoke(() => EnableImeForFocusedControl(control));
    }

    private void EnterViewModeCore()
    {
        if (!_isEditMode || _suppressViewMode) return;
        if (BodyEditBox.Visibility == Visibility.Visible && !TrySetNoteContent(BodyEditBox.Text))
            return;
        FlushPendingSave();

        _isEditMode = false;
        EditingBadge.Visibility = Visibility.Collapsed;
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
        ApplyTitleBarVisibility();   // タイトル編集のために出していた場合に戻す
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
            // Release the old bounds before applying a different folded height.
            MinHeight = 0;
            MaxHeight = double.PositiveInfinity;
            var height = FoldedHeight;
            Height = height;
            MinHeight = height;
            MaxHeight = height;
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
            TitleLargerButton, FontButton, IconButton, ColorButton, UndoButton, RedoButton })
            button.Foreground = ViewModel.TextForeground;
    }

    private void EditToolbarPopup_Opened(object? sender, EventArgs e)
    {
        StatusBar.Background = ToolbarBackground;
        StatusBar.BorderBrush = PopupBorderBrush();
        StatusBar.SetValue(TextElement.ForegroundProperty, ViewModel.TextForeground);
        foreach (var button in new[] { FontSmallerButton, FontLargerButton, TitleSmallerButton,
            TitleLargerButton, FontButton, IconButton, ColorButton, UndoButton, RedoButton })
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
        EditToolbarPopup.HorizontalOffset = 0;
        EditToolbarPopup.PlacementTarget = RootBorder;
        EditToolbarPopup.CustomPopupPlacementCallback = PlaceEditToolbar;
        EditToolbarPopup.Placement = PlacementMode.Custom;
        // Changing an offset forces WPF to reposition an already open Popup.
        if (EditToolbarPopup.IsOpen)
            EditToolbarPopup.VerticalOffset = 0.01;
        EditToolbarPopup.VerticalOffset = 0;
    }

    private CustomPopupPlacement[] PlaceEditToolbar(System.Windows.Size popupSize, System.Windows.Size targetSize, System.Windows.Point offset)
    {
        const double Gap = 2;
        var origin = RootBorder.PointToScreen(new System.Windows.Point());
        var workArea = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var (dpiX, dpiY) = GetDpi();
        // WPF passes transformed (device pixel) sizes to this callback. Keep the
        // working area and returned positions in those same units.
        var left = workArea.Left - origin.X;
        var top = workArea.Top - origin.Y;
        var right = workArea.Right - origin.X;
        var bottom = workArea.Bottom - origin.Y;
        var x = Math.Clamp(4 * dpiX, left, Math.Max(left, right - popupSize.Width));
        var y = targetSize.Height + Gap * dpiY;
        if (y + popupSize.Height > bottom)
            y = -popupSize.Height - Gap * dpiY;
        // A maximized-height note may leave no space outside either edge.
        y = Math.Clamp(y, top, Math.Max(top, bottom - popupSize.Height));
        return [new CustomPopupPlacement(new System.Windows.Point(x, y), PopupPrimaryAxis.None)];
    }

    private void NoteSurface_SizeChanged(object sender, SizeChangedEventArgs e)
        => ApplyNoteSurfaceClip(e.NewSize);

    /// <summary>
    /// タイトル背景が外枠の丸みに重ならないよう、内側の輪郭で切り抜く。
    /// 本文だけの表示でも同じ輪郭を使う。
    /// </summary>
    private void ApplyNoteSurfaceClip(System.Windows.Size size)
    {
        var radius = Math.Max(0, NoteCornerRadius - RootBorder.BorderThickness.Left);
        var clip = new System.Windows.Media.RectangleGeometry(new Rect(size), radius, radius);
        clip.Freeze();
        NoteSurface.Clip = clip;
    }

    private void RootBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyRootBorderClip(e.NewSize);

        if (EditToolbarPopup?.IsOpen == true)
            UpdateEditToolbarPlacement();
    }

    /// <summary>
    /// Border.CornerRadius だけでは子要素の背景が角にはみ出すため、
    /// 表示領域全体をクリップする。ネイティブのリサイズ枠は変更しない。
    /// </summary>
    private void ApplyRootBorderClip(System.Windows.Size size)
    {
        var clip = new System.Windows.Media.RectangleGeometry(new Rect(size), NoteCornerRadius, NoteCornerRadius);
        clip.Freeze();
        RootBorder.Clip = clip;
    }

    // RootBorder.CornerRadius ではなく設定を直接読む。バインディングは
    // DataBind 優先度で後から反映されるので、最初の SizeChanged の時点では
    // まだ既定値（0）のことがあり、角が落ちないまま切り抜いてしまう。
    private double NoteCornerRadius => ViewModel.NoteCornerRadius.TopLeft;

    /// <summary>
    /// 丸みの設定を変えても大きさは変わらないので SizeChanged は飛ばない。
    /// 設定を読み直したときは、切り抜きだけここで引き直す。
    /// </summary>
    private void RefreshCornerClips()
    {
        // 生成中はまだ大きさが決まっていない。最初の切り抜きは SizeChanged に任せる。
        if (_isInitializing) return;
        UpdateLayout();
        ApplyRootBorderClip(new System.Windows.Size(RootBorder.ActualWidth, RootBorder.ActualHeight));
        ApplyNoteSurfaceClip(new System.Windows.Size(NoteSurface.ActualWidth, NoteSurface.ActualHeight));
    }

    private void HideEditToolbar()
    {
        _toolbarHideTimer.Stop();
        EditToolbarPopup.IsOpen = false;
    }

    private void Window_Activated(object? sender, EventArgs e)
    {
        // 触った付箋を覚えてもらう。直後に走る重なり順の並べ直しで、
        // せっかく前に出たこの付箋を奥へ送り返さないようにするため。
        App.Current?.NoteTouched(this);
        if (EditToolbarPopup != null) ShowEditToolbar();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        ClosePickerPopups();
        SetTemporaryRaise(false);
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
        // アイコンがある付箋はそれ自体がつかみ代になるので、持ち手は要らない。
        OverlayMoveHandle.Visibility = string.IsNullOrEmpty(ViewModel.Icon)
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateTitleBarOverlayOffset();
    }

    /// <summary>XAML で TitleBarOverlay に付けている右余白。</summary>
    private const double TitleBarOverlayRightMargin = 4;

    /// <summary>
    /// 縦スクロールバーが出ている間は、その幅ぶんオーバーレイを左へ寄せる。
    /// どちらも本文の右上にあるので、そのままだとアイコンがつまみに重なる。
    /// </summary>
    private void UpdateTitleBarOverlayOffset()
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(IsBodyEditing() ? (DependencyObject)BodyEditBox : ContentBox);
        var barHeight = 0.0;
        if (scrollViewer?.ComputedHorizontalScrollBarVisibility == Visibility.Visible)
        {
            var bar = scrollViewer.Template.FindName("PART_HorizontalScrollBar", scrollViewer) as System.Windows.Controls.Primitives.ScrollBar;
            barHeight = bar?.ActualHeight > 0 ? bar.ActualHeight : SystemParameters.HorizontalScrollBarHeight;
        }
        var badgeMargin = EditingBadge.Margin;
        var bottom = 6 + barHeight;
        if (Math.Abs(badgeMargin.Bottom - bottom) >= 0.5)
            EditingBadge.Margin = new Thickness(badgeMargin.Left, badgeMargin.Top, badgeMargin.Right, bottom);

        var barWidth = scrollViewer?.ComputedVerticalScrollBarVisibility == Visibility.Visible
            ? SystemParameters.VerticalScrollBarWidth
            : 0;

        var margin = TitleBarOverlay.Margin;
        var right = TitleBarOverlayRightMargin + barWidth;
        if (Math.Abs(margin.Right - right) < 0.5)
            return;

        TitleBarOverlay.Margin = new Thickness(margin.Left, margin.Top, right, margin.Bottom);
    }

    /// <summary>
    /// スクロールバーは本文の量やウィンドウの大きさで出入りするので、
    /// そのたびにオーバーレイの位置を合わせ直す。
    /// </summary>
    private void Content_ScrollChanged(object sender, ScrollChangedEventArgs e)
        => UpdateTitleBarOverlayOffset();

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
    /// <summary>
    /// タイトルバーを隠して畳んでいるときの本文は、1行目を装飾なしで見せている
    /// だけの「見出し」で、編集も選択もする場所ではない。一方そのままだと掴む所が
    /// アイコン脇の数十pxしか無い。この状態の本文はタイトルバーそのものとして扱い、
    /// 移動も折りたたみ切り替えも、タイトルバーと同じ操作・同じ設定
    /// （シングル/ダブルクリック）で行えるようにする。
    /// </summary>
    private bool BodyActsAsTitleBar
        => !_isEditMode && ViewModel.IsFolded && ViewModel.IsTitleBarHidden;

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
                EnterEditMode();
        }
        else if (target != null && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            OpenTarget(target);
            e.Handled = true;
        }
    }

    private void ContentBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 本文がスクロールしないときはここで抜けるので、先に拾っておく。
        CaptureContextMenuImage(e.OriginalSource);

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
        // 畳んだ1行表示でも通常の矢印のままにする。掴んで動かせる状態ではあるが、
        // 本文の上に十字カーソルが出ると付箋の見た目を損なうため。
        ContentBox.Cursor = target != null ? WpfCursors.Hand : WpfCursors.Arrow;
    }

    /// <summary>
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
