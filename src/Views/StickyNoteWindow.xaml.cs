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

public partial class StickyNoteWindow : Window
{
    private AppSettings Settings => App.Current.Settings;

    /// <summary>
    /// アイコンピッカーを開くボタンの絵文字。パレットに実際に収録されている
    /// ものから選ぶ（AppSettings.IconGroups の IconAnimals）。このボタンで選べない
    /// 絵文字を看板にすると、探しても見つからない。
    /// </summary>
    private const string IconPickerGlyph = "🦊";

    /// <summary>
    /// 折りたたんだときのウィンドウ高さ（枠線込み）。通常はタイトルバーだけを残すが、
    /// タイトルバーを隠す設定のときは畳む先が無いので、本文の1行目だけを残す。
    /// </summary>
    private double FoldedHeight => ViewModel.IsTitleBarHidden
        ? FirstBodyLineHeight
        : ViewModel.TitleBarHeight + Settings.Layout.RootBorderThickness * 2;

    /// <summary>本文1行ぶんの高さ（本文の余白と枠線込み）。</summary>
    private double FirstBodyLineHeight
    {
        get
        {
            // 行送りは書体ごとに違うので、フォント側の値から出す。
            // 未知の書体名は WPF 側でフォールバックされ、その行送りが返る。
            var lineSpacing = new WpfFontFamily(ViewModel.FontFamily).LineSpacing;
            if (!double.IsFinite(lineSpacing) || lineSpacing <= 0) lineSpacing = 1.3;
            // ここは「タイトルバーを隠して畳んだときの高さ」を計算しているので、
            // 実際にまだ畳んでいなくても常にタイトル文字サイズを基準にする。
            // 見出しであっても拡大せず、タイトル文字サイズをそのまま優先する。
            var fontSize = MarkdownRenderer.GetFirstLineFontSize(
                ViewModel.Content, ViewModel.TitleFontSize, ignoreHeadingSize: true);
            return Math.Ceiling(lineSpacing * fontSize)
                 + ContentBox.Padding.Top + ContentBox.Padding.Bottom
                 + Settings.Layout.RootBorderThickness * 2;
        }
    }

    private double     _unfoldedHeight;
    // コンストラクタ〜Loaded の初期値設定中は true。
    // その間の SizeChanged/LocationChanged はモデルへ書き戻さない。
    private bool       _isInitializing;
    private Popup?     _colorPopup;
    private Popup?     _fontPopup;
    private bool       _isDragging;
    private bool       _dragSeparatesFoldedPosition;
    private double     _dragOffsetX, _dragOffsetY;
    private bool       _dragMoved;               // しきい値を超えて実際に動かしたか
    private System.Drawing.Point _dragStartCursor; // ドラッグ開始時のカーソル位置（しきい値判定用）
    private bool       _suppressTextChange;
    private bool       _suppressWindowBoundsSave;
    private bool       _isEditMode;
    private bool       _suppressViewMode;
    private bool       _isTaskCheckboxUpdatePending;
    private bool       _isContentContextMenuOpen;
    private bool       _isFoldAnimationRunning;
    private double     _requiredMarkdownPageWidth;
    private PendingMarkdownImageResize? _pendingMarkdownImageResize;
    private bool       _isMarkdownImageResizeQueued;
    private bool       _isPaneScrollDragPending;
    private bool       _isPaneScrollDragging;
    private bool       _suppressNextContentContextMenu;
    private bool       _isClosed;
    private FileSystemWatcher? _externalContentWatcher;
    private System.Windows.Point _paneScrollStartPoint;
    private double     _paneScrollStartHorizontalOffset;
    private double     _paneScrollStartVerticalOffset;
    private readonly Dictionary<WpfImage, MarkdownImageContext> _markdownImageContexts = [];
    private readonly Dictionary<string, (DateTime WriteTimeUtc, System.Windows.Media.Imaging.BitmapSource Bitmap)> _normalizedImageCache = [];
    private readonly Stack<ContentUndoEntry> _contentUndoStack = [];
    private WrapPanel? _colorPanel;

    private readonly System.Windows.Threading.DispatcherTimer _overlayTimer =
        new();
    private readonly System.Windows.Threading.DispatcherTimer _toolbarHideTimer =
        new();
    private readonly System.Windows.Threading.DispatcherTimer _titlePreviewTimer =
        new();
    private WrapPanel? _iconPanel;
    private Popup?     _iconPopup;

    // タイトルバーに付けられるアイコン。先頭の "" は「アイコンなし」で固定。
    // それ以降は settings.json の IconPalette で差し替えられる。
    private IEnumerable<string> IconList => new[] { "" }.Concat(Settings.IconPalette);
    private Hyperlink? _contextMenuLink;
    private MenuItem   _openLinkItem  = new();
    private MenuItem   _convertLinkItem = new();
    private MenuItem   _pasteMarkdownLinkItem = new();
    private MenuItem   _pasteExcelTableItem = new();
    private MenuItem   _copyExcelTableItem = new();
    private MenuItem   _fitWindowToImagesItem = new();
    private readonly StorageService _storage;
    private readonly System.Windows.Threading.Dispatcher _uiDispatcher;

    public StickyNoteViewModel ViewModel => (StickyNoteViewModel)DataContext;

    public System.Windows.Media.Brush ToolbarBackground =>
        string.Equals(App.Current.Settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase)
            ? new WpfSolidBrush(WpfColor.FromRgb(31, 41, 55))
            : new WpfSolidBrush(WpfColor.FromRgb(255, 255, 255));

    // Keep the toolbar edge identical to context-menu edges in every theme.
    public System.Windows.Media.Brush ToolbarBorderBrush => PopupBorderBrush();

    public string PositionSeparatedTooltip => LocalizationService.T("PositionSeparatedTooltip");

    public string MoveNoteTooltip => LocalizationService.T("MoveNoteTooltip");

    private sealed record ContentUndoEntry(string Before, string After);

    public StickyNoteWindow(StickyNoteViewModel vm, StorageService? storage = null)
    {
        InitializeComponent();
        _uiDispatcher = Dispatcher;
        DataContext = vm;
        ContentBox.SizeChanged += (_, _) => UpdateImagePathPreview();
        TitleText.SizeChanged += (_, _) => UpdateImagePathPreview();
        _storage = storage ?? new StorageService();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(StickyNoteViewModel.Icon) or null)
            {
                UpdateIconImage();
                // アイコンの有無で UpdateImagePathPreview の予約幅（iconWidth）が
                // 変わる。アイコンだけを付け外ししてもウィンドウ幅は変わらず
                // SizeChanged が飛ばないので、ここで明示的に引き直す。
                UpdateImagePathPreview();
            }
            if (e.PropertyName is nameof(StickyNoteViewModel.IsReadOnly) or null)
                ApplyReadOnlyState();
        };
        UpdateIconImage();

        // コンストラクタ・Loaded での初期値設定は SizeChanged/LocationChanged を
        // 発火させる。ガードしないと、例えば閉じた表示で起動したときに
        // 「Width = vm.Model.Width（開いた表示の幅）」という初期代入だけで
        // SizeChanged が走り、IsFolded==true 判定から Model.FoldedWidth が
        // 開いた表示の幅で上書きされてしまう（初期化の途中でモデルを汚染する）。
        _isInitializing = true;

        Left    = vm.IsFolded ? vm.Model.FoldedX ?? vm.Model.X : vm.Model.X;
        Top     = vm.IsFolded ? vm.Model.FoldedY ?? vm.Model.Y : vm.Model.Y;
        Width   = vm.IsFolded ? vm.Model.FoldedWidth ?? vm.Model.Width : vm.Model.Width;
        // 閉じた表示の高さはここで決めきる。Loaded まで開いた表示の高さのままだと、
        // ウィンドウが先にその高さで表示されてから縮むため、起動時に縦長の枠が
        // 一瞬見える。本文も同じ理由でここで畳んでおく。
        Height  = vm.IsFolded ? FoldedHeight : vm.Model.Height;
        Topmost = vm.IsTopmost;
        ShowInTaskbar = Settings.ShowNotesInTaskbar;
        _unfoldedHeight = vm.Model.Height;
        // バインディングは DispatcherPriority.DataBind で後から反映されるので、
        // Show() より前のここで決めきる。任せると初回フレームで一瞬見えてしまう。
        ApplyTitleBarVisibility();
        if (vm.IsFolded)
        {
            ApplyFoldedContentPresentation();
            BodyEditBox.Visibility = Visibility.Collapsed;
        }

        ConfigurePopups();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
                HideTransientPopups();
        };
        ApplySettings();
        ApplyLocalizedText();
        ConfigureContextMenus();
        TitleText.ContextMenuOpening += TitleContextMenuOpening;
        TitleEditBox.ContextMenuOpening += TitleContextMenuOpening;
        System.Windows.DataObject.AddPastingHandler(ContentBox, OnPaste);
        System.Windows.DataObject.AddPastingHandler(BodyEditBox, OnPaste);

        // ポップアップ・コンテキストメニューは別HWNDのため開くとウィンドウが
        // 非アクティブになり、フォーカスもそちらへ移る。ContentBox 自身の
        // 右クリックメニュー（切り取り/コピー/貼り付け）を素通りさせてしまうと、
        // メニューを開いただけで EnterViewMode() が発火して LoadContent() が
        // ドキュメントを再構築し、IsReadOnly も true に戻る。結果、右クリックの
        // 「貼り付け」がキャレット位置を失って機能しない（貼り付け先が末尾に
        // ずれて見える）。開いている間はビューモードへの移行を抑止する。
        // handlers are attached by ConfigurePopups().
        // ContextMenu.Opened では遅い（開く際のフォーカス移動が先に起きて
        // LostKeyboardFocus が発火してしまう）ため、開く"前"に呼ばれる
        // FrameworkElement.ContextMenuOpening（ContentBox_ContextMenuOpening）
        // 側でフラグを立てる。閉じたときの解除だけ Closed で行う。

        _overlayTimer.Tick += (_, _) => FadeOutSizeOverlay();
        _toolbarHideTimer.Tick += (_, _) =>
        {
            _toolbarHideTimer.Stop();
            if (!ShouldKeepEditToolbarOpen())
                HideEditToolbar();
        };
        _titlePreviewTimer.Tick += (_, _) =>
        {
            _titlePreviewTimer.Stop();
            UpdateTitlePreviewVisibility();
        };

        Loaded += (_, _) =>
        {
            LoadContent(vm.Content);
            ConfigureExternalContentWatcher();
            // 開いた表示でも必ず通す。ここを通さないと WindowChrome が
            // XAML の初期値（全辺 5px）のままになり、タイトルバー上端が
            // リサイズ枠として残ってしまう。
            SetResizeEnabled(!vm.IsFolded);
            ApplyReadOnlyState();
            UpdateTitleBarButtonsVisibility();
            // 初期値設定はここまで。以降の SizeChanged/LocationChanged は
            // 通常どおりモデルに書き戻してよい。
            _isInitializing = false;
        };
        Closed += (_, _) =>
        {
            StopReminderFlash();
            _isClosed = true;
            DisposeExternalContentWatcher();
        };
        PreviewMouseDown += (_, _) => StopReminderFlash();
        PreviewKeyDown += (_, _) => StopReminderFlash();
        IsVisibleChanged += (_, _) => { if (!IsVisible) StopReminderFlash(); };
    }

    public void RefreshSettings()
    {
        ViewModel.RefreshSettings();
        ConfigurePopups();
        ApplySettings();
        ApplyLocalizedText();
        ConfigureContextMenus();
        if (!_isEditMode)
            LoadContent(ViewModel.Content);
    }

    private void ConfigureContextMenus()
    {
        if (ContentBox.ContextMenu != null)
            ContentBox.ContextMenu.Closed -= ContentContextMenu_Closed;
        if (BodyEditBox.ContextMenu != null)
            BodyEditBox.ContextMenu.Closed -= ContentContextMenu_Closed;

        ContentBox.ContextMenu = BuildContentContextMenu();
        ContentBox.ContextMenu.Closed += ContentContextMenu_Closed;
        BodyEditBox.ContextMenu = BuildBodyEditContextMenu();
        BodyEditBox.ContextMenu.Closed += ContentContextMenu_Closed;

        var titleContextMenu = BuildTitleContextMenu();
        TitleText.ContextMenu = titleContextMenu;
        TitleEditBox.ContextMenu = titleContextMenu;
    }

    private void ConfigurePopups()
    {
        ClosePopup(_colorPopup);
        ClosePopup(_fontPopup);
        ClosePopup(_iconPopup);

        _colorPopup = BuildColorPopup();
        _fontPopup  = BuildFontPopup();
        _iconPopup  = BuildIconPopup();

        foreach (var popup in new[] { _colorPopup, _fontPopup, _iconPopup })
        {
            popup.Opened += Popup_Opened;
            popup.Closed += Popup_Closed;
        }
        // Keep the picker open through the toolbar's mouse-down. Its Click handler
        // can then toggle the actual open state, without WPF dismissing it first.
        _colorPopup.StaysOpen = true;
        _iconPopup.StaysOpen = true;
    }

    private bool _watchingPickerInput;

    private void Picker_PreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is not MouseButtonEventArgs mouse ||
            mouse.RoutedEvent != Mouse.PreviewMouseDownEvent) return;

        foreach (var popup in new[] { _colorPopup, _iconPopup })
        {
            if (popup?.IsOpen != true) continue;
            if (popup.Child is FrameworkElement child &&
                new Rect(child.RenderSize).Contains(mouse.GetPosition(child))) continue;
            if (popup.PlacementTarget is WpfButton button &&
                new Rect(button.RenderSize).Contains(mouse.GetPosition(button))) continue;
            popup.IsOpen = false;
        }
    }

    private static void ClosePopup(Popup? popup)
    {
        if (popup != null)
            popup.IsOpen = false;
    }

    private void Popup_Opened(object? sender, EventArgs e)
    {
        _suppressViewMode = true;
        if (!_watchingPickerInput)
        {
            InputManager.Current.PreProcessInput += Picker_PreProcessInput;
            _watchingPickerInput = true;
        }
        ShowEditToolbar();
        RaisePickerPopups();
    }

    internal void RaisePickerPopups()
    {
        foreach (var popup in new[] { _colorPopup, _fontPopup, _iconPopup })
        {
            if (popup?.IsOpen != true || PresentationSource.FromVisual(popup.Child) is not HwndSource source)
                continue;
            SetWindowPos(source.Handle, new IntPtr(-1), 0, 0, 0, 0,
                SetWindowPosFlags.NoMove | SetWindowPosFlags.NoSize | SetWindowPosFlags.NoActivate);
        }
    }

    private void Popup_Closed(object? sender, EventArgs e)
    {
        if (_watchingPickerInput && !new[] { _colorPopup, _fontPopup, _iconPopup }.Any(p => p?.IsOpen == true))
        {
            InputManager.Current.PreProcessInput -= Picker_PreProcessInput;
            _watchingPickerInput = false;
        }
        _suppressViewMode = new[] { _colorPopup, _fontPopup, _iconPopup }.Any(p => p?.IsOpen == true);
        if (_isEditMode && IsVisible) Dispatcher.BeginInvoke(() =>
        {
            if (_isClosed || !IsActive || _suppressViewMode) return;
            if (IsBodyEditing()) BodyEditBox.Focus();
            else TitleEditBox.Focus();
        });
        ScheduleHideEditToolbar();
    }

    // Popup は別ウィンドウなので、親の付箋を Hide しても自動では消えない。
    private void HideTransientPopups()
    {
        _toolbarHideTimer.Stop();
        ClosePopup(_colorPopup);
        ClosePopup(_fontPopup);
        ClosePopup(_iconPopup);
        TitlePreviewPopup.IsOpen = false;
        EditToolbarPopup.IsOpen = false;
    }

    private void ContentContextMenu_Closed(object? sender, RoutedEventArgs e)
    {
        _isContentContextMenuOpen = false;
        _suppressViewMode = false;
        ShowEditToolbar();
        if (_isEditMode) Dispatcher.BeginInvoke(() =>
        {
            if (!_isClosed && IsActive && !_isContentContextMenuOpen)
                ContentBox.Focus();
        });
        ScheduleHideEditToolbar();
    }

    private void ApplySettings()
    {
        var editBackground = IsDarkTheme() ? WpfBrushes.White : WpfBrushes.Black;
        var editForeground = IsDarkTheme() ? WpfBrushes.Black : WpfBrushes.White;
        EditingBadge.Background = editBackground;
        EditingBadge.BorderBrush = editForeground;
        EditingBadgeText.Foreground = editForeground;
        DoneEditingButton.Background = editBackground;
        DoneEditingButton.Foreground = editForeground;

        _overlayTimer.Interval = TimeSpan.FromMilliseconds(Settings.Timings.SizeOverlayDurationMs);
        _toolbarHideTimer.Interval = TimeSpan.FromMilliseconds(Settings.Timings.ToolbarHideDelayMs);
        _titlePreviewTimer.Interval = TimeSpan.FromMilliseconds(Settings.Timings.TitlePreviewDelayMs);
        ShowInTaskbar = Settings.ShowNotesInTaskbar;
    }

    private void ApplyLocalizedText()
    {
        TitleEditBox.ToolTip = LocalizationService.T("TitleFallbackTooltip");
        AddNoteButton.ToolTip = LocalizationService.T("AddNoteTooltip");
        PinButton.ToolTip = LocalizationService.T("TopmostTooltip");
        FoldButton.ToolTip = LocalizationService.T("FoldTooltip");
        ContentBox.ToolTip = GetContentBoxTooltip();
        UpdateToolbarTooltips();
    }

    private string GetContentBoxTooltip()
        => IsContentReadOnly()
            ? LocalizationService.T("EditLockBodyTooltip")
            : LocalizationService.T("EditBodyTooltip");

    private void ApplyReadOnlyState()
    {
        if (!IsContentReadOnly())
        {
            ContentBox.ToolTip = GetContentBoxTooltip();
            return;
        }

        ContentBox.IsReadOnly = true;
        BodyEditBox.Visibility = Visibility.Collapsed;
        ApplyFoldedContentPresentation();
        ContentBox.Cursor = WpfCursors.Arrow;
        ContentBox.BorderThickness = new Thickness(0);
        ContentBox.BorderBrush = WpfBrushes.Transparent;
        TitleText.Visibility = Visibility.Visible;
        TitleEditBox.Visibility = Visibility.Collapsed;
        ApplyTitleBarVisibility();   // タイトル編集のために出していた場合に戻す
        ContentBox.ToolTip = GetContentBoxTooltip();
        HideEditToolbar();
        _isEditMode = false;
        ViewModel.SetForceOpaque(false);
        LoadContent(ViewModel.Content);
        Keyboard.ClearFocus();
    }

    private bool IsContentReadOnly()
        => ViewModel.IsReadOnly || ViewModel.Model.IsExternalContent;

    private void UpdateToolbarTooltips()
    {
        FontSmallerButton.ToolTip = string.Format(LocalizationService.T("FontSmallerTooltip"), ViewModel.FontSize);
        FontLargerButton.ToolTip = string.Format(LocalizationService.T("FontLargerTooltip"), ViewModel.FontSize);
        TitleSmallerButton.ToolTip = string.Format(LocalizationService.T("TitleSmallerTooltip"), ViewModel.TitleFontSize);
        TitleLargerButton.ToolTip = string.Format(LocalizationService.T("TitleLargerTooltip"), ViewModel.TitleFontSize);
        FontButton.ToolTip = LocalizationService.T("FontTooltip");
        IconButton.ToolTip = LocalizationService.T("IconTooltip");
        ColorButton.ToolTip = LocalizationService.T("ColorTooltip");
        UndoButton.ToolTip = LocalizationService.T("UndoTooltip");
        RedoButton.ToolTip = LocalizationService.T("RedoTooltip");
        UndoButton.Content = "↶";
        RedoButton.Content = "↷";
        IconButton.Content = new WpfImage { Source = RenderEmoji(IconPickerGlyph), Width = 20, Height = 20 };
        ColorButton.Content = new WpfImage { Source = RenderEmoji("🎨"), Width = 20, Height = 20 };
        DoneEditingButton.Content = "✓";
        DoneEditingButton.ToolTip = LocalizationService.T("DoneEditingTooltip");
        EditingBadgeText.Text = LocalizationService.T("EditingBadge");
    }

    // WPFはSegoe UI Emojiのカラーフォントを直接描画できないため、
    // SkiaSharpで一度PNGへ描画してImageとして表示する。
    private void UpdateIconImage()
    {
        IconImage.Source = RenderEmoji(ViewModel.Icon);
        OverlayIconImage.Source = IconImage.Source;
        // アイコンの有無で、畳んでいるときに帯を出すかどうかが変わる。
        UpdateTitleBarOverlayVisibility();
    }

    // 絵文字の画像化は設定画面とも共有する。実装は Services/EmojiRenderer.cs。
    private static WpfBitmapImage? RenderEmoji(string icon) => EmojiRenderer.Render(icon);

    // ─── ウィンドウイベント ──────────────────────────────────────

    private System.Windows.Threading.DispatcherOperation? _resizeContentRefresh;

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isInitializing) return; // コンストラクタ〜Loaded の初期値設定はモデルに書き戻さない
        if (_suppressWindowBoundsSave) return;
        if (_isFoldAnimationRunning) return; // アニメーション途中の高さを開いた表示サイズとして保存しない
        if (_isEditMode && !ViewModel.IsFolded)
        {
            ViewModel.Model.EditWidth = Width;
            ViewModel.Model.EditHeight = Height;
            RequestSave();
            return;
        }
        // 幅は開いた表示/閉じた表示で別々のフィールドに保存する
        // （ToggleFold() が状態切り替え時にどちらか一方へスナップする）。
        // 高さは閉じた表示中は見た目上のタイトルバー高さでしかないため、
        // 開いた表示のみ保存する（編集モードで一時的に伸ばしたぶんも除く）。
        if (ViewModel.IsFolded)
            ViewModel.Model.FoldedWidth = Width;
        else
            ViewModel.Model.Width = Width;

        if (!ViewModel.IsFolded)
            ViewModel.Model.Height = Height;
        if (e.WidthChanged && !_isEditMode && !ViewModel.IsFolded &&
            _resizeContentRefresh?.Status != System.Windows.Threading.DispatcherOperationStatus.Pending)
            _resizeContentRefresh = Dispatcher.BeginInvoke(() =>
            {
                // 実行時点で編集モードに入っている可能性があるため再確認する
                // （そうでないと編集中の内容が描画済みドキュメントで上書きされる）。
                if (!_isClosed && !_isEditMode && !ViewModel.IsFolded && !_isFoldAnimationRunning)
                    LoadContent(ViewModel.Content);
            }, System.Windows.Threading.DispatcherPriority.Background);
        RequestSave();
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (EditToolbarPopup?.IsOpen == true)
            UpdateEditToolbarPlacement();
        if (_isDragging || _isInitializing) return;
        if (_suppressWindowBoundsSave) return;
        SaveCurrentPositionToModel();
        RequestSave();
    }

    private void SuppressWindowBoundsSave(Action action)
    {
        var previous = _suppressWindowBoundsSave;
        _suppressWindowBoundsSave = true;
        try
        {
            action();
        }
        finally
        {
            _suppressWindowBoundsSave = previous;
        }
    }

    private void SaveCurrentPositionToModel()
    {
        if (_dragSeparatesFoldedPosition || IsControlPressed())
            ViewModel.IsPositionSeparated = true;
        var syncOtherState = !ViewModel.IsPositionSeparated;
        if (ViewModel.IsFolded)
        {
            ViewModel.Model.FoldedX = Left;
            ViewModel.Model.FoldedY = Top;
            if (syncOtherState)
            {
                ViewModel.Model.X = Left;
                ViewModel.Model.Y = Top;
            }
        }
        else
        {
            ViewModel.Model.X = Left;
            ViewModel.Model.Y = Top;
            if (syncOtherState)
            {
                ViewModel.Model.FoldedX = Left;
                ViewModel.Model.FoldedY = Top;
            }
        }
    }

    private void MarkPositionSeparatedIfOpenViewMovedAwayFromClosedView()
    {
        if (ViewModel.IsFolded || ViewModel.IsPositionSeparated)
            return;
        if (!ViewModel.Model.FoldedX.HasValue || !ViewModel.Model.FoldedY.HasValue)
            return;

        const double PositionTolerance = 0.5;
        if (Math.Abs(ViewModel.Model.FoldedX.Value - Left) > PositionTolerance ||
            Math.Abs(ViewModel.Model.FoldedY.Value - Top) > PositionTolerance)
        {
            ViewModel.IsPositionSeparated = true;
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        FlushPendingSave();
        // タスクバーの×や Alt+F4 のように、アプリを通さずウィンドウだけ閉じられた
        // ときは、付箋を削除せず非表示にして閉じるのを取りやめる。閉じてしまうと
        // 一覧には残るのに Show() できない状態になり、全表示で例外になる。
        if (App.Current.HideNoteOnWindowClose(this))
            e.Cancel = true;
    }

    // ─── 自動保存（デバウンス） ──────────────────────────────────

    private System.Windows.Threading.DispatcherTimer? _saveTimer;
    private bool _savePending;
    private bool _savingDisabled;
    private long _savePendingSince;

    private void RequestSave()
    {
        if (_savingDisabled || _isClosed) return;
        if (!_savePending) _savePendingSince = Environment.TickCount64;
        _savePending = true;
        if (_saveTimer == null)
        {
            _saveTimer = new System.Windows.Threading.DispatcherTimer();
            _saveTimer.Tick += (_, _) =>
            {
                try { FlushPendingSave(); }
                catch (Exception ex)
                {
                    ErrorReporter.ReportNonFatal("Deferred save", ex);
                }
            };
        }
        _saveTimer.Stop();
        // Keep saving during continuous typing, without modifying the editor or its undo history.
        var remaining = Math.Max(0, 5000 - (Environment.TickCount64 - _savePendingSince));
        _saveTimer.Interval = TimeSpan.FromMilliseconds(Math.Min(Settings.Timings.SaveDebounceMs, remaining));
        _saveTimer.Start();
    }

    /// <summary>
    /// 保留中の保存をただちに実行する。
    /// 終了・ログオフ・ウィンドウを閉じたときに、デバウンス待ちの
    /// 変更が失われないようにするために呼ぶ。
    /// </summary>
    public void FlushPendingSave()
    {
        if (!_savePending) return;
        SaveNote();
    }

    // Both automatic saves and application-wide saves use the injected store.
    internal void SaveNote()
    {
        if (_savingDisabled) return;
        _storage.SaveNote(ViewModel.Model);
        _saveTimer?.Stop();
        _savePending = false;
    }

    internal void DisableSaving()
    {
        _savingDisabled = true;
        _saveTimer?.Stop();
        _savePending = false;
    }

}
