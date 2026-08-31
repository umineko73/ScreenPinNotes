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

public partial class StickyNoteWindow : Window
{
    private AppSettings Settings => App.Current.Settings;

    /// <summary>折りたたんだときのウィンドウ高さ（枠線込み）。</summary>
    private double FoldedHeight => ViewModel.TitleBarHeight + Settings.Layout.RootBorderThickness * 2;

    private static readonly string[] FontList =
    [
        "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo",
        "BIZ UDGothic", "BIZ UDPGothic", "BIZ UDMincho",
        "MS Gothic", "MS Mincho", "Segoe UI", "Consolas",
    ];

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
    private FileSystemWatcher? _externalContentWatcher;
    private System.Windows.Point _paneScrollStartPoint;
    private double     _paneScrollStartHorizontalOffset;
    private double     _paneScrollStartVerticalOffset;
    private readonly Dictionary<WpfImage, MarkdownImageContext> _markdownImageContexts = [];
    private readonly Dictionary<string, (DateTime WriteTimeUtc, System.Windows.Media.Imaging.BitmapSource Bitmap)> _normalizedImageCache = [];
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

    public StickyNoteViewModel ViewModel => (StickyNoteViewModel)DataContext;

    public StickyNoteWindow(StickyNoteViewModel vm, StorageService? storage = null)
    {
        InitializeComponent();
        DataContext = vm;
        _storage = storage ?? new StorageService();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(StickyNoteViewModel.Icon) or null)
                UpdateIconImage();
            if (e.PropertyName is nameof(StickyNoteViewModel.IsReadOnly) or null)
                ApplyReadOnlyState();
        };
        UpdateIconImage();

        // コンストラクタ・Loaded での初期値設定は SizeChanged/LocationChanged を
        // 発火させる。ガードしないと、例えば折りたたみ状態で開いたときに
        // 「Width = vm.Model.Width（展開時の幅）」という初期代入だけで
        // SizeChanged が走り、IsFolded==true 判定から Model.FoldedWidth が
        // 展開時の幅で上書きされてしまう（初期化の途中でモデルを汚染する）。
        _isInitializing = true;

        Left    = vm.IsFolded ? vm.Model.FoldedX ?? vm.Model.X : vm.Model.X;
        Top     = vm.IsFolded ? vm.Model.FoldedY ?? vm.Model.Y : vm.Model.Y;
        Width   = vm.IsFolded ? vm.Model.FoldedWidth ?? vm.Model.Width : vm.Model.Width;
        Height  = vm.Model.Height;
        Topmost = vm.IsTopmost;
        _unfoldedHeight = vm.Model.Height;

        ConfigurePopups();
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

        // アプリ切り替え時もビューモードへ。IME の候補/変換ウィンドウで一時的に
        // Deactivated になることがあるため、即時ではなく遅延して実フォーカスを見る。
        Deactivated += (_, _) => ScheduleEnterViewModeIfFocusLeft();

        Loaded += (_, _) =>
        {
            LoadContent(vm.Content);
            if (vm.IsFolded)
            {
                ContentBox.Visibility = Visibility.Collapsed;
                BodyEditBox.Visibility = Visibility.Collapsed;
                Height = FoldedHeight;
            }
            ConfigureExternalContentWatcher();
            // 展開状態でも必ず通す。ここを通さないと WindowChrome が
            // XAML の初期値（全辺 5px）のままになり、タイトルバー上端が
            // リサイズ枠として残ってしまう。
            SetResizeEnabled(!vm.IsFolded);
            ApplyReadOnlyState();
            UpdateTitleBarButtonsVisibility();
            // 初期値設定はここまで。以降の SizeChanged/LocationChanged は
            // 通常どおりモデルに書き戻してよい。
            _isInitializing = false;
        };
        Closed += (_, _) => DisposeExternalContentWatcher();
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
    }

    private static void ClosePopup(Popup? popup)
    {
        if (popup != null)
            popup.IsOpen = false;
    }

    private void Popup_Opened(object? sender, EventArgs e)
    {
        _suppressViewMode = true;
        ShowEditToolbar();
    }

    private void Popup_Closed(object? sender, EventArgs e)
    {
        _suppressViewMode = false;
        if (_isEditMode) Dispatcher.BeginInvoke(() => ContentBox.Focus());
        ScheduleHideEditToolbar();
    }

    private void ContentContextMenu_Closed(object? sender, RoutedEventArgs e)
    {
        _isContentContextMenuOpen = false;
        _suppressViewMode = false;
        if (_isEditMode) Dispatcher.BeginInvoke(() => ContentBox.Focus());
        ScheduleHideEditToolbar();
    }

    private void ApplySettings()
    {
        _overlayTimer.Interval = TimeSpan.FromMilliseconds(Settings.Timings.SizeOverlayDurationMs);
        _toolbarHideTimer.Interval = TimeSpan.FromMilliseconds(Settings.Timings.ToolbarHideDelayMs);
        _titlePreviewTimer.Interval = TimeSpan.FromMilliseconds(Settings.Timings.TitlePreviewDelayMs);
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
        ContentBox.Visibility = ViewModel.IsFolded ? Visibility.Collapsed : Visibility.Visible;
        ContentBox.Cursor = WpfCursors.Arrow;
        ContentBox.BorderThickness = new Thickness(0);
        ContentBox.BorderBrush = WpfBrushes.Transparent;
        TitleText.Visibility = Visibility.Visible;
        TitleEditBox.Visibility = Visibility.Collapsed;
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
    }

    // WPFはSegoe UI Emojiのカラーフォントを直接描画できないため、
    // SkiaSharpで一度PNGへ描画してImageとして表示する。
    private void UpdateIconImage()
    {
        IconImage.Source = RenderEmoji(ViewModel.Icon);
    }

    private static WpfBitmapImage? RenderEmoji(string icon)
    {
        if (string.IsNullOrEmpty(icon)) return null;

        const int pixelSize = 64;
        using var bitmap = new SKBitmap(pixelSize, pixelSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var typeface = SKTypeface.FromFamilyName("Segoe UI Emoji");
        using var font = new SKFont(typeface, 52) { Subpixel = true };
        using var paint = new SKPaint
        {
            IsAntialias = true,
        };

        var bounds = new SKRect();
        font.MeasureText(icon, out bounds, paint);
        var x = (pixelSize - bounds.Width) / 2 - bounds.Left;
        var y = (pixelSize - bounds.Height) / 2 - bounds.Top;
        canvas.DrawText(icon, x, y, font, paint);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();
        var result = new WpfBitmapImage();
        result.BeginInit();
        result.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        result.StreamSource = stream;
        result.EndInit();
        result.Freeze();
        return result;
    }

    // ─── ウィンドウイベント ──────────────────────────────────────

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isInitializing) return; // コンストラクタ〜Loaded の初期値設定はモデルに書き戻さない
        if (_suppressWindowBoundsSave) return;
        if (_isFoldAnimationRunning) return; // アニメーション途中の高さを展開時サイズとして保存しない
        // 幅は展開時/折りたたみ時で別々のフィールドに保存する
        // （ToggleFold() が状態切り替え時にどちらか一方へスナップする）。
        // 高さは折りたたみ中は見た目上の折りたたみ高さでしかないため、
        // 展開時のみ保存する（編集モードで一時的に伸ばしたぶんも除く）。
        if (ViewModel.IsFolded)
            ViewModel.Model.FoldedWidth = Width;
        else
            ViewModel.Model.Width = Width;

        if (!ViewModel.IsFolded)
            ViewModel.Model.Height = Height - _statusBarDelta;
        if (!_isEditMode && !ViewModel.IsFolded)
            Dispatcher.BeginInvoke(() =>
            {
                // 実行時点で編集モードに入っている可能性があるため再確認する
                // （そうでないと編集中の内容が描画済みドキュメントで上書きされる）。
                if (!_isEditMode && !ViewModel.IsFolded)
                    LoadContent(ViewModel.Content);
            }, System.Windows.Threading.DispatcherPriority.Background);
        RequestSave();
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (_isDragging || _isInitializing) return;
        if (_suppressWindowBoundsSave) return;
        SaveCurrentPositionToModel();
        RequestSave();
    }

    private void SuppressWindowBoundsSave(Action action)
    {
        _suppressWindowBoundsSave = true;
        try
        {
            action();
        }
        finally
        {
            _suppressWindowBoundsSave = false;
        }
    }

    private void SaveCurrentPositionToModel()
    {
        var syncOtherState = !_dragSeparatesFoldedPosition && !IsControlPressed();
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

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        => FlushPendingSave();

    // ─── 自動保存（デバウンス） ──────────────────────────────────

    private System.Threading.Timer? _saveTimer;
    private bool _savePending;

    private void RequestSave()
    {
        _savePending = true;
        _saveTimer?.Dispose();
        _saveTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                    return;

                Dispatcher.Invoke(() =>
                {
                    _savePending = false;
                    App.Current.SaveAll();
                });
            }
            catch (Exception ex)
            {
                // シャットダウン競合（InvalidOperationException/TaskCanceledException/
                // Win32Exception）だけでなく、ディスクI/Oエラー等の保存失敗も
                // ここで捕まえてアプリ全体のクラッシュを防ぐ。
                ErrorReporter.ReportNonFatal("Deferred save", ex);
            }
        }, null, Settings.Timings.SaveDebounceMs, System.Threading.Timeout.Infinite);
    }

    /// <summary>
    /// 保留中の保存をただちに実行する。
    /// 終了・ログオフ・ウィンドウを閉じたときに、デバウンス待ちの
    /// 変更が失われないようにするために呼ぶ。
    /// </summary>
    public void FlushPendingSave()
    {
        if (!_savePending) return;
        _savePending = false;
        _saveTimer?.Dispose();
        _saveTimer = null;
        App.Current.SaveAll();
    }

}
