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
using ScreenStickyNotes.Services;
using ScreenStickyNotes.ViewModels;
using SkiaSharp;
using WpfBrushes     = System.Windows.Media.Brushes;
using WpfButton      = System.Windows.Controls.Button;
using WpfBitmapImage = System.Windows.Media.Imaging.BitmapImage;
using WpfColor       = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfCursors     = System.Windows.Input.Cursors;
using WpfDataFormats = System.Windows.DataFormats;
using WpfFontFamily  = System.Windows.Media.FontFamily;
using WpfListBox     = System.Windows.Controls.ListBox;
using WpfSolidBrush  = System.Windows.Media.SolidColorBrush;

namespace ScreenStickyNotes.Views;

public partial class StickyNoteWindow : Window
{
    private const double UnfoldedMinWidth = 140;
    private const double ResizeBorder     = 5;
    private const double SnapDistance     = 10;

    // RootBorder の枠線（上下 1px ずつ）。折りたたみ時のウィンドウ高さに
    // この分を足さないとタイトルバーの下端が切り取られ、
    // 文字が上下中央からずれて見える。
    private const double RootBorderThickness = 1;

    /// <summary>折りたたんだときのウィンドウ高さ（枠線込み）。</summary>
    private double FoldedHeight => ViewModel.TitleBarHeight + RootBorderThickness * 2;

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
    private double     _dragOffsetX, _dragOffsetY;
    private bool       _dragMoved;               // しきい値を超えて実際に動かしたか
    private int        _dragClickCount;           // MouseDown 時のクリック回数を Up まで保持
    private bool       _dragStartedOnTitle;       // タイトル文字列上で始まったクリックか
    private System.Drawing.Point _dragStartCursor; // ドラッグ開始時のカーソル位置（しきい値判定用）
    private System.Windows.Threading.DispatcherTimer? _singleClickTimer; // ダブルクリック判定の猶予用
    private bool       _suppressTextChange;
    private bool       _isEditMode;
    private bool       _suppressViewMode;
    private bool       _isContentContextMenuOpen;
    private bool       _isFoldAnimationRunning;
    private WrapPanel? _colorPanel;

    private readonly System.Windows.Threading.DispatcherTimer _overlayTimer =
        new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly System.Windows.Threading.DispatcherTimer _toolbarHideTimer =
        new() { Interval = TimeSpan.FromMilliseconds(180) };
    private WrapPanel? _iconPanel;
    private Popup?     _iconPopup;

    // タイトルバーに付けられるアイコン。先頭の "" は「アイコンなし」。
    private static readonly string[] IconList =
    [
        "",
        "📌", "⭐", "❗", "❓", "✅", "🔥", "💡", "📝",
        "📋", "📅", "⏰", "🔔", "🎯", "🚀", "💼", "🏠",
        "🛒", "🍽", "☕", "🎵", "📚", "✏", "🔧", "🐛",
        "💰", "📞", "✉", "🔑", "🔒", "❤", "👍", "🎉",
        "🎁", "🌟", "⚠", "🚨", "📦", "🗓", "🧪", "🌱",
    ];
    private Hyperlink? _contextMenuLink;
    private MenuItem   _openLinkItem  = new();
    private MenuItem   _convertLinkItem = new();

    public StickyNoteViewModel ViewModel => (StickyNoteViewModel)DataContext;

    public StickyNoteWindow(StickyNoteViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(StickyNoteViewModel.Icon) or null)
                UpdateIconImage();
        };
        UpdateIconImage();

        // コンストラクタ・Loaded での初期値設定は SizeChanged/LocationChanged を
        // 発火させる。ガードしないと、例えば折りたたみ状態で開いたときに
        // 「Width = vm.Model.Width（展開時の幅）」という初期代入だけで
        // SizeChanged が走り、IsFolded==true 判定から Model.FoldedWidth が
        // 展開時の幅で上書きされてしまう（初期化の途中でモデルを汚染する）。
        _isInitializing = true;

        Left    = vm.Model.X;
        Top     = vm.Model.Y;
        Width   = vm.Model.Width;
        Height  = vm.Model.Height;
        Topmost = vm.IsTopmost;
        _unfoldedHeight = vm.Model.Height;

        _colorPopup = BuildColorPopup();
        _fontPopup  = BuildFontPopup();
        _iconPopup  = BuildIconPopup();
        ContentBox.ContextMenu = BuildContentContextMenu();
        System.Windows.DataObject.AddPastingHandler(ContentBox, OnPaste);

        // ポップアップ・コンテキストメニューは別HWNDのため開くとウィンドウが
        // 非アクティブになり、フォーカスもそちらへ移る。ContentBox 自身の
        // 右クリックメニュー（切り取り/コピー/貼り付け）を素通りさせてしまうと、
        // メニューを開いただけで EnterViewMode() が発火して LoadContent() が
        // ドキュメントを再構築し、IsReadOnly も true に戻る。結果、右クリックの
        // 「貼り付け」がキャレット位置を失って機能しない（貼り付け先が末尾に
        // ずれて見える）。開いている間はビューモードへの移行を抑止する。
        foreach (var popup in new[] { _colorPopup, _fontPopup, _iconPopup })
        {
            popup.Opened += (_, _) =>
            {
                _suppressViewMode = true;
                ShowEditToolbar();
            };
            popup.Closed += (_, _) =>
            {
                _suppressViewMode = false;
                if (_isEditMode) Dispatcher.BeginInvoke(() => ContentBox.Focus());
                ScheduleHideEditToolbar();
            };
        }
        // ContextMenu.Opened では遅い（開く際のフォーカス移動が先に起きて
        // LostKeyboardFocus が発火してしまう）ため、開く"前"に呼ばれる
        // FrameworkElement.ContextMenuOpening（ContentBox_ContextMenuOpening）
        // 側でフラグを立てる。閉じたときの解除だけ Closed で行う。
        ContentBox.ContextMenu.Closed += (_, _) =>
        {
            _isContentContextMenuOpen = false;
            _suppressViewMode = false;
            if (_isEditMode) Dispatcher.BeginInvoke(() => ContentBox.Focus());
            ScheduleHideEditToolbar();
        };

        _overlayTimer.Tick += (_, _) => FadeOutSizeOverlay();
        _toolbarHideTimer.Tick += (_, _) =>
        {
            _toolbarHideTimer.Stop();
            if (!ShouldKeepEditToolbarOpen())
                HideEditToolbar();
        };

        // アプリ切り替え時もビューモードへ
        Deactivated += (_, _) => EnterViewMode();

        Loaded += (_, _) =>
        {
            LoadContent(vm.Content);
            if (vm.IsFolded)
            {
                ContentBox.Visibility = Visibility.Collapsed;
                // 折りたたみ時専用の幅が保存されていればそれを使う
                Width  = vm.Model.FoldedWidth ?? vm.Model.Width;
                Height = FoldedHeight;
            }
            // 展開状態でも必ず通す。ここを通さないと WindowChrome が
            // XAML の初期値（全辺 5px）のままになり、タイトルバー上端が
            // リサイズ枠として残ってしまう。
            SetResizeEnabled(!vm.IsFolded);
            UpdateTitleBarButtonsVisibility();
            // 初期値設定はここまで。以降の SizeChanged/LocationChanged は
            // 通常どおりモデルに書き戻してよい。
            _isInitializing = false;
        };
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

    // ─── FlowDocument ↔ プレーンテキスト ─────────────────────────

    private void LoadContent(string text)
    {
        _suppressTextChange = true;
        try
        {
            ContentBox.Document.Blocks.Clear();
            var lines = string.IsNullOrEmpty(text) ? [""] : text.Split('\n');
            foreach (var line in lines)
            {
                var para = new Paragraph { Margin = new Thickness(0) };
                foreach (var seg in LinkDetector.Parse(line))
                    para.Inlines.Add(seg.IsLink ? (Inline)CreateHyperlink(seg.Text) : new Run(seg.Text));
                ContentBox.Document.Blocks.Add(para);
            }
        }
        finally { _suppressTextChange = false; }
    }

    private string GetPlainText()
    {
        var sb    = new StringBuilder();
        bool first = true;
        foreach (Block block in ContentBox.Document.Blocks)
        {
            if (!first) sb.Append('\n');
            first = false;
            if (block is Paragraph para)
            {
                foreach (Inline inline in para.Inlines)
                {
                    sb.Append(inline switch
                    {
                        Run r                              => r.Text,
                        Hyperlink h when h.Tag is string t => t,
                        LineBreak                          => "\n",
                        _ => new TextRange(inline.ContentStart, inline.ContentEnd).Text,
                    });
                }
            }
        }
        return sb.ToString();
    }

    // ─── ハイパーリンク ──────────────────────────────────────────

    private Hyperlink CreateHyperlink(string target)
    {
        var link = new Hyperlink(new Run(target))
        {
            Foreground      = WpfBrushes.RoyalBlue,
            Cursor          = WpfCursors.Hand,
            Tag             = target,
            TextDecorations = TextDecorations.Underline,
        };
        link.Click += (_, _) => OpenTarget(target);
        return link;
    }

    private static void OpenTarget(string target)
    {
        try
        {
            if (LinkDetector.IsFolder(target))
                Process.Start("explorer.exe", $"\"{target}\"");
            else
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    // ─── Edit / View モード ──────────────────────────────────────

    private void EnterEditMode()
    {
        if (_isEditMode && !ContentBox.IsReadOnly) return;
        _isEditMode = true;
        ContentBox.IsReadOnly = false;
        ContentBox.Cursor = WpfCursors.IBeam;
        ContentBox.BorderThickness = new Thickness(2);
        ContentBox.BorderBrush = WpfBrushes.CornflowerBlue;
        ContentBox.ToolTip = null;
        // タイトルも同時に編集可能にする。フォーカスは本文に置いたままにし、
        // タイトルを直したい人だけ自分でクリックしてもらう。
        TitleText.Visibility    = Visibility.Collapsed;
        TitleEditBox.Visibility = Visibility.Visible;
        UpdateControlsVisibility();
        if (!ContentBox.IsKeyboardFocusWithin)
            ContentBox.Focus();
    }

    private void EnterTitleEditMode()
    {
        if (!_isEditMode)
        {
            _isEditMode = true;
            ContentBox.IsReadOnly = true;
            ContentBox.Cursor = WpfCursors.Arrow;
            ContentBox.BorderThickness = new Thickness(0);
            ContentBox.BorderBrush = WpfBrushes.Transparent;
            ContentBox.ToolTip = "ダブルクリックして編集";
        }

        TitleText.Visibility    = Visibility.Collapsed;
        TitleEditBox.Visibility = Visibility.Visible;
        UpdateControlsVisibility();
        TitleEditBox.Focus();
        TitleEditBox.SelectAll();
    }

    private void EnterViewMode()
    {
        if (!_isEditMode || _suppressViewMode) return;
        _isEditMode = false;
        // ドキュメントを再構築してリンクを正しく復元する
        LoadContent(ViewModel.Content);
        ContentBox.IsReadOnly = true;
        ContentBox.Cursor = WpfCursors.Arrow;
        ContentBox.BorderThickness = new Thickness(0);
        ContentBox.BorderBrush = WpfBrushes.Transparent;
        ContentBox.ToolTip = "ダブルクリックして編集";
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
    // 折りたたみ時（enabled=false）でも幅だけは変更できるようにしている。
    // 上下だけ 0 にして左右は残す。タイトルバーは 28px 程度しかなく、
    // その上端 5px が HTTOP になると「畳もうとしてダブルクリック」が
    // Windows 標準の縦方向最大化に化けるため、上辺は常に 0 にする必要がある
    // （下辺も同じ理由で畳んでいる間は 0 のままにする）。
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
            chrome.ResizeBorderThickness = enabled
                ? new Thickness(ResizeBorder, 0, ResizeBorder, ResizeBorder)
                : new Thickness(ResizeBorder, 0, ResizeBorder, 0);
        }

        MinWidth = UnfoldedMinWidth;
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

    // 折りたたみ状態と編集モードの両方を考慮してステータスバーの表示を更新
    private void UpdateControlsVisibility()
    {
        if (_isEditMode && !ViewModel.IsFolded && ShouldKeepEditToolbarOpen())
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
        if (!_isEditMode || ViewModel.IsFolded) return;
        _toolbarHideTimer.Stop();
        UpdateEditToolbarPlacement();
        EditToolbarPopup.IsOpen = true;
    }

    private void UpdateEditToolbarPlacement()
    {
        const double Gap = 2;
        EditToolbarPopup.PlacementTarget = RootBorder;
        EditToolbarPopup.Placement = PlacementMode.Bottom;
        EditToolbarPopup.VerticalOffset = Gap;
    }

    private void HideEditToolbar()
    {
        _toolbarHideTimer.Stop();
        EditToolbarPopup.IsOpen = false;
    }

    private void ScheduleHideEditToolbar()
    {
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
        FoldButton.Visibility = visibility;
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

        // 折りたたみアニメーションが Height プロパティを掴んだままだと、
        // 以下の直接代入がその場では効いても次のレイアウトパスで
        // アニメーションの最終値に上書きされてしまう。先に解除する。
        BeginAnimation(HeightProperty, null);

        // 伸ばす前に上下の制限を緩めておく（折りたたみ用の固定が残っていることがある）
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
    {
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var (dpiX, dpiY) = GetDpi();
        double workBottom = screen.WorkingArea.Bottom / dpiY;
        double workTop    = screen.WorkingArea.Top    / dpiY;

        if (Top + Height > workBottom)
            Top = Math.Max(workTop, workBottom - Height);
    }

    // View モード: クリックでリンクを直接開く / 非リンクならEdit モードへ
    // Edit モード: Ctrl+クリックでリンクを開く
    private void ContentBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
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

    // View モードでハイパーリンク上にカーソルが来たら Hand に切り替え
    private void ContentBox_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isEditMode) return;
        var target = GetHyperlinkAt(e.GetPosition(ContentBox));
        ContentBox.Cursor = target != null ? WpfCursors.Hand : WpfCursors.Arrow;
    }

    private void ContentBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // ステータスバー・タイトル編集欄への移動は編集モードを維持する。
        // ここで Focus() を呼び戻すとボタンのマウスキャプチャを奪い Click が発火しなくなる。
        if (_suppressViewMode) return;
        if (IsDescendantOf(e.NewFocus as DependencyObject, StatusBar)) return;
        if (IsDescendantOf(e.NewFocus as DependencyObject, TitleEditBox)) return;
        EnterViewMode();
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
        EnterViewMode();
    }

    private static bool IsDescendantOf(DependencyObject? child, DependencyObject? ancestor)
    {
        while (child != null)
        {
            if (child == ancestor) return true;
            child = VisualTreeHelper.GetParent(child);
        }
        return false;
    }

    private void ContentBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
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
        ViewModel.Content = GetPlainText();
        RequestSave();
    }

    // Title 自体の値は TwoWay バインディングが更新するので、ここでは保存の予約だけ行う
    private void TitleEditBox_TextChanged(object sender, TextChangedEventArgs e) => RequestSave();

    // ─── 貼り付け（リンク検出付き） ──────────────────────────────

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(WpfDataFormats.UnicodeText)) return;
        e.CancelCommand();

        var clipboard = ((string)e.DataObject.GetData(WpfDataFormats.UnicodeText))
            .Replace("\r\n", "\n").Replace("\r", "\n");

        var plainText = GetPlainText();
        var startOff  = GetOffsetOfPointer(ContentBox.Selection.Start);
        var endOff    = GetOffsetOfPointer(ContentBox.Selection.End);
        var beforeText = plainText[..startOff];
        var afterText  = plainText[endOff..];
        var newText    = beforeText + clipboard + afterText;
        var caretOff   = beforeText.Length + clipboard.Length;

        LoadContent(newText);
        RestoreCaretAt(caretOff);

        ViewModel.Content = newText;
        RequestSave();
    }

    // TextPointer が指す位置の、GetPlainText() が返す文字列上での文字オフセットを求める。
    //
    // 以前は TextRange(from, to).Text を直接使っていたが、WPF の TextRange.Text は
    // 範囲の終端が段落境界と一致するかどうかで末尾の改行の有無が不安定になる
    // （終端が文書末尾かどうか等で余分な改行が付いたり付かなかったりする）。
    // GetPlainText() と同じ辿り方（Run/Hyperlink/LineBreak の順に長さを積み上げる）を
    // することで、その揺れを避けて GetPlainText() の結果と常に一致するオフセットを得る。
    private int GetOffsetOfPointer(TextPointer target)
    {
        int pos = 0;
        bool firstPara = true;
        foreach (Block block in ContentBox.Document.Blocks)
        {
            if (!firstPara) pos++;
            firstPara = false;

            if (block is not Paragraph para) continue;

            bool targetInThisPara =
                target.CompareTo(para.ContentStart) >= 0 &&
                target.CompareTo(para.ContentEnd) <= 0;

            foreach (Inline inline in para.Inlines)
            {
                int len = inline switch
                {
                    Run r                              => r.Text.Length,
                    Hyperlink h when h.Tag is string t => t.Length,
                    LineBreak                          => 1,
                    _ => new TextRange(inline.ContentStart, inline.ContentEnd).Text.Length,
                };

                if (targetInThisPara && target.CompareTo(inline.ContentEnd) <= 0)
                {
                    if (target.CompareTo(inline.ContentStart) <= 0)
                        return pos;

                    var within = new TextRange(inline.ContentStart, target).Text
                        .Replace("\r\n", "\n").Replace("\r", "\n").Length;
                    return pos + Math.Min(within, len);
                }

                pos += len;
            }

            if (targetInThisPara) return pos;
        }
        return pos;
    }

    private void RestoreCaretAt(int target)
    {
        int pos = 0;
        bool firstPara = true;
        foreach (Block block in ContentBox.Document.Blocks)
        {
            if (!firstPara)
            {
                if (pos == target)
                {
                    ContentBox.CaretPosition =
                        block.ContentStart.GetInsertionPosition(LogicalDirection.Forward)
                        ?? ContentBox.Document.ContentEnd;
                    return;
                }
                pos++;
            }
            firstPara = false;

            if (block is Paragraph para)
            {
                foreach (Inline inline in para.Inlines)
                {
                    int len = inline switch
                    {
                        Run r                              => r.Text.Length,
                        Hyperlink h when h.Tag is string t => t.Length,
                        _                                  => 0,
                    };
                    if (pos + len >= target)
                    {
                        var tp = inline.ContentStart;
                        for (int i = 0; i < target - pos; i++)
                            tp = tp.GetNextInsertionPosition(LogicalDirection.Forward) ?? tp;
                        ContentBox.CaretPosition = tp;
                        return;
                    }
                    pos += len;
                }
            }
        }
        ContentBox.CaretPosition = ContentBox.Document.ContentEnd;
    }

    // ─── コンテキストメニュー ────────────────────────────────────

    private ContextMenu BuildContentContextMenu()
    {
        _openLinkItem    = new MenuItem { Header = "リンクを開く",    IsEnabled = false };
        _convertLinkItem = new MenuItem { Header = "リンクとして変換", IsEnabled = false };
        _openLinkItem.Click    += OpenLink_Click;
        _convertLinkItem.Click += ConvertLink_Click;

        var cm = new ContextMenu();
        cm.Items.Add(new MenuItem { Header = "切り取り", Command = ApplicationCommands.Cut,   CommandTarget = ContentBox });
        cm.Items.Add(new MenuItem { Header = "コピー",   Command = ApplicationCommands.Copy,  CommandTarget = ContentBox });
        cm.Items.Add(new MenuItem { Header = "貼り付け", Command = ApplicationCommands.Paste, CommandTarget = ContentBox });
        cm.Items.Add(new Separator());
        cm.Items.Add(_openLinkItem);
        cm.Items.Add(_convertLinkItem);
        cm.Items.Add(new Separator());
        var deleteItem = new MenuItem { Header = "削除" };
        deleteItem.Click += Close_Click;
        cm.Items.Add(deleteItem);
        return cm;
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

        ShowEditToolbar();
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
        var newText  = before + sel + after;    // sel は URL なので LoadContent でリンク検出される
        var caretOff = before.Length + sel.Length;

        LoadContent(newText);
        RestoreCaretAt(caretOff);

        ViewModel.Content = newText;
        RequestSave();
    }

    // ─── ドラッグ & スナップ ─────────────────────────────────────
    //
    // タイトルバーは「動かさなければクリック、動かせばドラッグ」で
    // 意味が変わる。押した瞬間はまだどちらか分からないので、
    // MouseMove でしきい値を超えて初めてドラッグ確定として扱い、
    // 超えなければ MouseUp 時点のクリック回数で判定する:
    //   シングルクリック → 折りたたみ／展開
    //   タイトルをダブルクリック → タイトルだけ編集（折りたたみ状態は維持）
    //   タイトル以外をダブルクリック → 本文編集（畳んでいれば先に展開する）
    //   ドラッグ         → ウィンドウ移動（従来どおり）

    private const double ClickDragThresholdPx = 4; // これ未満の移動はクリックの揺れとみなす

    // タイトルバーのシングルクリック確定までの猶予。
    // タイトル文字列上はダブルクリック編集と競合するため、空白部より少し長く待つ。
    private const int SingleClickGraceMs = 200;
    private const int TitleSingleClickGraceMs = 250;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // タイトル編集欄をクリックしたときは、キャレット配置をそのまま
        // TextBox に任せる。ドラッグ開始・畳み判定もスキップし、
        // ウィンドウが動いたり編集欄が閉じたりしないようにする。
        if (_isEditMode && e.OriginalSource is DependencyObject src && IsDescendantOf(src, TitleEditBox))
            return;

        _isDragging      = true;
        _dragMoved       = false;
        _dragClickCount  = e.ClickCount;
        _dragStartedOnTitle = IsPointerInside(TitleText, e);
        var (dpiX, dpiY) = GetDpi();
        var cur = System.Windows.Forms.Cursor.Position;
        _dragOffsetX     = cur.X / dpiX - Left;
        _dragOffsetY     = cur.Y / dpiY - Top;
        _dragStartCursor = cur;
        ((UIElement)sender).CaptureMouse();
    }

    private void TitleBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging) return;
        var cur = System.Windows.Forms.Cursor.Position;

        if (!_dragMoved)
        {
            if (Math.Abs(cur.X - _dragStartCursor.X) < ClickDragThresholdPx &&
                Math.Abs(cur.Y - _dragStartCursor.Y) < ClickDragThresholdPx)
                return; // まだクリックの範囲内。ドラッグ確定まで動かさない
            _dragMoved = true;
        }

        var (dpiX, dpiY) = GetDpi();
        Left = cur.X / dpiX - _dragOffsetX;
        Top  = cur.Y / dpiY - _dragOffsetY;
        SnapToAll();
    }

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();

        if (_dragMoved)
        {
            ViewModel.Model.X = Left;
            ViewModel.Model.Y = Top;
            RequestSave();
            return;
        }

        // 動かなかった＝クリックとして扱う。
        //
        // 1回目のクリックの MouseUp 時点ではまだ ClickCount==1 でしか
        // 届かない（2回目が来て初めて Windows が ClickCount==2 と判定する）。
        // ここで即座に畳んでしまうと、直後に来る2回目のクリックに間に合わず
        // ダブルクリックとして拾えない。そこで、シングルクリックの確定を
        // 短い猶予だけ遅らせ、その間に2回目が来たら
        // （ClickCount==2 で Up が呼ばれたら）畳む処理をキャンセルして
        // 編集モードへの遷移に差し替える。
        if (_dragClickCount >= 2)
        {
            _singleClickTimer?.Stop();
            _singleClickTimer = null;
            if (_dragStartedOnTitle)
                EnterTitleEditMode();
            else if (ViewModel.IsFolded)
                ToggleFold(EnterEditMode); // 展開アニメーション完了後に編集モードへ
            else
                EnterEditMode();
            return;
        }

        _singleClickTimer?.Stop();
        _singleClickTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(GetSingleClickGraceMs())
        };
        _singleClickTimer.Tick += (_, _) =>
        {
            _singleClickTimer!.Stop();
            _singleClickTimer = null;
            ToggleFold();
        };
        _singleClickTimer.Start();
    }

    private void TitleBar_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        UpdateTitleBarButtonsVisibility();
        UpdateTitlePreviewVisibility();
    }

    private void TitleBar_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        UpdateTitleBarButtonsVisibility();
        TitlePreviewPopup.IsOpen = false;
    }

    private void UpdateTitlePreviewVisibility()
    {
        TitlePreviewPopup.IsOpen = ViewModel.IsFolded &&
            !_isEditMode &&
            !string.IsNullOrWhiteSpace(ViewModel.Content) &&
            TitleBar.IsMouseOver;
    }

    private void RootBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        => ShowEditToolbar();

    private void RootBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        => ScheduleHideEditToolbar();

    private void EditToolbar_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        => ShowEditToolbar();

    private void EditToolbar_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        => ScheduleHideEditToolbar();

    private int GetSingleClickGraceMs()
        => _dragStartedOnTitle ? TitleSingleClickGraceMs : SingleClickGraceMs;

    private static bool IsPointerInside(FrameworkElement element, System.Windows.Input.MouseEventArgs e)
    {
        var p = e.GetPosition(element);
        return p.X >= 0 && p.X <= element.ActualWidth &&
               p.Y >= 0 && p.Y <= element.ActualHeight;
    }

    private (double dpiX, double dpiY) GetDpi()
    {
        var src = PresentationSource.FromVisual(this);
        return (
            src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0,
            src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0
        );
    }

    // ─── スナップ ────────────────────────────────────────────────

    private void SnapToAll()
    {
        var hwnd   = new WindowInteropHelper(this).Handle;
        var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
        var wa     = screen.WorkingArea;

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null) return;
        double dpiX = source.CompositionTarget.TransformToDevice.M11;
        double dpiY = source.CompositionTarget.TransformToDevice.M22;

        double waL = wa.Left / dpiX, waT = wa.Top    / dpiY;
        double waR = wa.Right / dpiX, waB = wa.Bottom / dpiY;

        double? bestLeft = null, bestTop = null;
        double  minLD = SnapDistance, minTD = SnapDistance;

        TrySnap(Left,         waL, ref bestLeft, ref minLD, waL);
        TrySnap(Left + Width, waR, ref bestLeft, ref minLD, waR - Width);
        TrySnap(Top,          waT, ref bestTop,  ref minTD, waT);
        TrySnap(Top + Height, waB, ref bestTop,  ref minTD, waB - Height);

        foreach (var other in App.Current.NoteWindows)
        {
            if (other == this) continue;
            double oL = other.Left, oT = other.Top;
            double oR = oL + other.Width, oB = oT + other.Height;

            TrySnap(Left,         oL, ref bestLeft, ref minLD, oL);
            TrySnap(Left,         oR, ref bestLeft, ref minLD, oR);
            TrySnap(Left + Width, oL, ref bestLeft, ref minLD, oL - Width);
            TrySnap(Left + Width, oR, ref bestLeft, ref minLD, oR - Width);

            TrySnap(Top,          oT, ref bestTop, ref minTD, oT);
            TrySnap(Top,          oB, ref bestTop, ref minTD, oB);
            TrySnap(Top + Height, oT, ref bestTop, ref minTD, oT - Height);
            TrySnap(Top + Height, oB, ref bestTop, ref minTD, oB - Height);
        }

        if (bestLeft.HasValue) Left = bestLeft.Value;
        if (bestTop.HasValue)  Top  = bestTop.Value;
    }

    private static void TrySnap(double myEdge, double target,
        ref double? best, ref double bestDist, double snapTo)
    {
        var d = Math.Abs(myEdge - target);
        if (d < bestDist) { bestDist = d; best = snapTo; }
    }

    // ─── リサイズ中のスナップ（WM_SIZING フック） ────────────────

    private const int WM_SIZING          = 0x0214;
    private const int WM_NCLBUTTONDBLCLK = 0x00A3;
    private const int HTTOP              = 12;
    private const int HTBOTTOM           = 15;
    private const int WMSZ_LEFT          = 1;
    private const int WMSZ_RIGHT         = 2;
    private const int WMSZ_TOP           = 3;
    private const int WMSZ_TOPLEFT       = 4;
    private const int WMSZ_TOPRIGHT      = 5;
    private const int WMSZ_BOTTOM        = 6;
    private const int WMSZ_BOTTOMLEFT    = 7;
    private const int WMSZ_BOTTOMRIGHT   = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource src)
            src.AddHook(WndProc);
        ApplyRoundedCorners();
    }

    // ─── 角丸（Windows 11 の DWM に任せる） ──────────────────────
    //
    // AllowsTransparency + Border.CornerRadius でも実現できるが、
    // WindowChrome のリサイズ処理と相性が悪く描画も重くなる。
    // DWM に角を落としてもらえば OS 側の合成で切り抜かれる。

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUNDSMALL              = 3;   // 小さめの角丸

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void ApplyRoundedCorners()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int preference = DWMWCP_ROUNDSMALL;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference, sizeof(int));
        }
        catch
        {
            // Windows 10 以前では未対応。角丸にならないだけで動作に支障はない
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 上下のリサイズ枠をダブルクリックすると Windows が縦方向に最大化する。
        // 付箋では意図しない動きなので握りつぶす（ドラッグでのリサイズは残る）。
        if (msg == WM_NCLBUTTONDBLCLK)
        {
            int hit = wParam.ToInt32();
            if (hit == HTTOP || hit == HTBOTTOM)
            {
                handled = true;
                return IntPtr.Zero;
            }
        }

        // 折りたたみ中でも幅の変更（左右辺）だけは許可している。上下辺は
        // SetResizeEnabled が常に 0 にしているため、折りたたみ中に届く
        // WM_SIZING は自然と左右辺のみになる。
        if (msg != WM_SIZING) return IntPtr.Zero;

        var rect = Marshal.PtrToStructure<RECT>(lParam);
        if (SnapSizingRect(ref rect, wParam.ToInt32()))
        {
            Marshal.StructureToPtr(rect, lParam, false);
            handled = true;
            return (IntPtr)1;
        }
        return IntPtr.Zero;
    }

    // ドラッグ中の矩形（デバイスpx）を論理pxに直してスナップ先を探し、書き戻す
    private bool SnapSizingRect(ref RECT r, int edge)
    {
        var (dpiX, dpiY) = GetDpi();

        double left   = r.Left   / dpiX;
        double top    = r.Top    / dpiY;
        double right  = r.Right  / dpiX;
        double bottom = r.Bottom / dpiY;

        bool movingLeft   = edge is WMSZ_LEFT   or WMSZ_TOPLEFT    or WMSZ_BOTTOMLEFT;
        bool movingRight  = edge is WMSZ_RIGHT  or WMSZ_TOPRIGHT   or WMSZ_BOTTOMRIGHT;
        bool movingTop    = edge is WMSZ_TOP    or WMSZ_TOPLEFT    or WMSZ_TOPRIGHT;
        bool movingBottom = edge is WMSZ_BOTTOM or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT;

        var xTargets = new List<double>();
        var yTargets = new List<double>();

        // 画面の作業領域
        var wa = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        double waL = wa.Left / dpiX, waR = wa.Right  / dpiX;
        double waT = wa.Top  / dpiY, waB = wa.Bottom / dpiY;

        if (movingLeft)   xTargets.Add(waL);
        if (movingRight)  xTargets.Add(waR);
        if (movingTop)    yTargets.Add(waT);
        if (movingBottom) yTargets.Add(waB);

        foreach (var other in App.Current.NoteWindows)
        {
            if (other == this || !other.IsVisible) continue;

            double oL = other.Left, oT = other.Top;
            double oR = oL + other.ActualWidth, oB = oT + other.ActualHeight;

            // 辺を他の付箋の辺に合わせる
            if (movingLeft)   { xTargets.Add(oL); xTargets.Add(oR); }
            if (movingRight)  { xTargets.Add(oL); xTargets.Add(oR); }
            if (movingTop)    { yTargets.Add(oT); yTargets.Add(oB); }
            if (movingBottom) { yTargets.Add(oT); yTargets.Add(oB); }

            // 幅・高さを他の付箋と揃える（サイズスナップ）
            if (other.ViewModel.IsFolded) continue;
            if (movingRight)  xTargets.Add(left   + other.ActualWidth);
            if (movingLeft)   xTargets.Add(right  - other.ActualWidth);
            if (movingBottom) yTargets.Add(top    + other.ActualHeight);
            if (movingTop)    yTargets.Add(bottom - other.ActualHeight);
        }

        bool changed = false;

        if (movingLeft   && TryNearest(left,   xTargets, out var nl)) { left   = nl; changed = true; }
        if (movingRight  && TryNearest(right,  xTargets, out var nr)) { right  = nr; changed = true; }
        if (movingTop    && TryNearest(top,    yTargets, out var nt)) { top    = nt; changed = true; }
        if (movingBottom && TryNearest(bottom, yTargets, out var nb)) { bottom = nb; changed = true; }

        if (!changed) return false;

        // 最小サイズを割り込むスナップは破棄する
        if (right - left < MinWidth || bottom - top < MinHeight) return false;

        r.Left   = (int)Math.Round(left   * dpiX);
        r.Right  = (int)Math.Round(right  * dpiX);
        r.Top    = (int)Math.Round(top    * dpiY);
        r.Bottom = (int)Math.Round(bottom * dpiY);
        return true;
    }

    private static bool TryNearest(double value, List<double> targets, out double snapped)
    {
        snapped = value;
        double best = SnapDistance;
        bool found = false;
        foreach (var t in targets)
        {
            var d = Math.Abs(value - t);
            if (d < best) { best = d; snapped = t; found = true; }
        }
        return found;
    }

    // ─── 折りたたみ ──────────────────────────────────────────────

    private void Fold_Click(object sender, RoutedEventArgs e) => ToggleFold();

    // onUnfolded: 展開アニメーション完了後に呼ぶコールバック（省略可）。
    // 畳んだ状態から「展開して編集モードに入る」ような、アニメーション完了を
    // 待ってから続けたい処理のために用意している。アニメーション実行中に
    // Height へ直接代入する処理（EnterEditMode 経由の GrowForStatusBar 等）を
    // 呼んでしまうと、進行中のアニメーションが中途半端な値で凍結されてしまう。
    private void ToggleFold(Action? onUnfolded = null)
    {
        if (ViewModel.IsFolded)
        {
            ContentBox.Visibility = Visibility.Visible;
            ViewModel.IsFolded = false;
            UpdateTitleBarButtonsVisibility();
            UpdateTitlePreviewVisibility();
            Width = ViewModel.Model.Width; // 展開時専用の幅に戻す
            SetResizeEnabled(true);
            RunFoldAnimation(FoldedHeight, _unfoldedHeight, () =>
            {
                ViewModel.Model.Height = _unfoldedHeight;
                onUnfolded?.Invoke();
            });
        }
        else
        {
            if (_isEditMode) EnterViewMode(); // 折りたたみ時は閲覧モードに戻す
            _unfoldedHeight = Height;
            // アニメーション中の SizeChanged で Model.Height が
            // 途中の値に上書きされないよう先にフラグを立てる
            ViewModel.IsFolded = true;
            UpdateTitleBarButtonsVisibility();
            UpdateTitlePreviewVisibility();
            HideEditToolbar();
            // 折りたたみ時専用の幅へスナップ（未設定なら現在の幅のまま）
            Width = ViewModel.Model.FoldedWidth ?? Width;
            RunFoldAnimation(Height, FoldedHeight, () =>
            {
                ContentBox.Visibility = Visibility.Collapsed;
                ViewModel.Model.Height = _unfoldedHeight;
                SetResizeEnabled(false); // タイトルバーのみの時はリサイズ不可
            });
        }
        RequestSave();
    }

    // Completed は BeginAnimation の前に購読しないと発火しない。
    // BeginAnimation の時点で Timeline が凍結され AnimationClock が
    // 生成されるため、後から足したハンドラは呼ばれない。
    private void RunFoldAnimation(double from, double to, Action? completed = null)
    {
        _isFoldAnimationRunning = true;
        AnimateHeight(from, to, () =>
        {
            // アニメーション後も Height のベース値を最終値に固定する。
            // これをしないと、後続の BeginAnimation(..., null) で折りたたみ高さへ戻ることがある。
            Height = to;
            BeginAnimation(HeightProperty, null);
            _isFoldAnimationRunning = false;
            completed?.Invoke();
        });
    }

    private void AnimateHeight(double from, double to, Action? completed = null)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        if (completed != null)
            anim.Completed += (_, _) => completed();
        BeginAnimation(HeightProperty, anim);
    }

    // ─── タイトルバーボタン ──────────────────────────────────────

    // 押した付箋の書式を引き継いで新規作成する
    private void AddNote_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = GetNewNotePositionNearCursor();
        App.Current.AddNewNote(ViewModel.Model, x, y);
    }

    private (double x, double y) GetNewNotePositionNearCursor()
    {
        const double Offset = 12;
        const double DefaultWidth = 260;
        const double DefaultHeight = 220;

        var cursor = System.Windows.Forms.Cursor.Position;
        var (dpiX, dpiY) = GetDpi();
        var screen = System.Windows.Forms.Screen.FromPoint(cursor);
        var wa = screen.WorkingArea;

        double left = cursor.X / dpiX + Offset;
        double top  = cursor.Y / dpiY + Offset;

        double minLeft = wa.Left / dpiX;
        double maxLeft = wa.Right / dpiX - DefaultWidth;
        double minTop = wa.Top / dpiY;
        double maxTop = wa.Bottom / dpiY - DefaultHeight;

        return (
            Math.Clamp(left, minLeft, Math.Max(minLeft, maxLeft)),
            Math.Clamp(top, minTop, Math.Max(minTop, maxTop))
        );
    }

    private void Pin_Changed(object sender, RoutedEventArgs e)
    {
        Topmost = ViewModel.IsTopmost;
        RequestSave();
    }

    private void FontSmaller_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.FontSize > 8) { ViewModel.FontSize -= 1; RequestSave(); }
        ShowSizeOverlay($"本文 {ViewModel.FontSize:0}pt");
    }

    private void FontLarger_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.FontSize < 48) { ViewModel.FontSize += 1; RequestSave(); }
        ShowSizeOverlay($"本文 {ViewModel.FontSize:0}pt");
    }

    private void TitleSmaller_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TitleFontSize > 8) SetTitleFontSize(ViewModel.TitleFontSize - 1);
        ShowSizeOverlay($"タイトル {ViewModel.TitleFontSize:0}pt");
    }

    private void TitleLarger_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TitleFontSize < 28) SetTitleFontSize(ViewModel.TitleFontSize + 1);
        ShowSizeOverlay($"タイトル {ViewModel.TitleFontSize:0}pt");
    }

    private void SetTitleFontSize(double size)
    {
        ViewModel.TitleFontSize = size;

        // 折りたたみ中はウィンドウ高さ＝タイトルバー高さなので追従させる
        if (ViewModel.IsFolded)
        {
            BeginAnimation(HeightProperty, null);   // 折りたたみアニメの保持を解除
            SetResizeEnabled(false);                // Min/Max を新しい高さで固定し直す
            Height = FoldedHeight;
        }
        RequestSave();
    }

    // ─── サイズ表示オーバーレイ ──────────────────────────────────
    //
    // ツールチップはクリックで閉じてしまい連打中に読めないため、
    // 音量 OSD のように一時表示してフェードアウトさせる。

    private void ShowSizeOverlay(string text)
    {
        SizeOverlayText.Text = text;

        SizeOverlay.BeginAnimation(OpacityProperty, null);   // 実行中のフェードを解除
        SizeOverlay.Opacity = 1;

        _overlayTimer.Stop();
        _overlayTimer.Start();                               // 連打のたびに表示時間を延長
    }

    private void FadeOutSizeOverlay()
    {
        _overlayTimer.Stop();
        SizeOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(350)));
    }

    private void Font_Click(object sender, RoutedEventArgs e)
    {
        if (_fontPopup != null) { _fontPopup.PlacementTarget = (UIElement)sender; _fontPopup.IsOpen = true; }
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (_colorPopup == null) return;
        UpdateColorSelection();
        _colorPopup.PlacementTarget = (UIElement)sender;
        _colorPopup.IsOpen = true;
    }

    private void Icon_Click(object sender, RoutedEventArgs e)
    {
        if (_iconPopup == null) return;
        UpdateIconSelection();
        _iconPopup.PlacementTarget = (UIElement)sender;
        _iconPopup.IsOpen = true;
    }

    // 現在の色にだけチェックを表示する
    private void UpdateColorSelection()
    {
        if (_colorPanel == null) return;
        foreach (var child in _colorPanel.Children)
            if (child is WpfButton b)
                b.Content = (b.Tag as string) == ViewModel.ColorKey ? "✓" : null;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show("この付箋を削除しますか？", "確認",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            App.Current.RemoveNote(ViewModel.Model.Id);
            Close();
        }
    }

    // ─── ウィンドウイベント ──────────────────────────────────────

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isInitializing) return; // コンストラクタ〜Loaded の初期値設定はモデルに書き戻さない
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
        RequestSave();
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (_isDragging || _isInitializing) return;
        ViewModel.Model.X = Left;
        ViewModel.Model.Y = Top;
        RequestSave();
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
            Dispatcher.Invoke(() =>
            {
                _savePending = false;
                App.Current.SaveAll();
            });
        }, null, 800, System.Threading.Timeout.Infinite);
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

    // ─── カラーピッカー ──────────────────────────────────────────

    private Popup BuildColorPopup()
    {
        const double Swatch  = 28;
        const double Gap     = 3;
        const int    Columns = 6;

        // 端数で列が折り返さないよう僅かに余裕を持たせる
        var panel = new WrapPanel { Width = Columns * (Swatch + Gap * 2) + 2 };
        _colorPanel = panel;

        foreach (var (key, preset) in StickyNoteViewModel.ColorPresets)
        {
            var header = new WpfSolidBrush((WpfColor)WpfColorConverter.ConvertFromString(preset.Header));
            var btn = new WpfButton
            {
                Width = Swatch, Height = Swatch, Margin = new Thickness(Gap),
                Padding         = new Thickness(0),
                Background      = new WpfSolidBrush((WpfColor)WpfColorConverter.ConvertFromString(preset.Bg)),
                BorderThickness = new Thickness(1),
                BorderBrush     = header,   // 枠線でヘッダー色も判るようにする
                Foreground      = header,   // 選択中のチェック記号の色
                FontWeight      = FontWeights.Bold,
                FontSize        = 13,
                Tag             = key,
                ToolTip         = key,
                Cursor          = WpfCursors.Hand,
            };
            btn.Click += (s, _) =>
            {
                if (s is WpfButton b && b.Tag is string k)
                {
                    ViewModel.ColorKey = k;
                    if (_colorPopup != null) _colorPopup.IsOpen = false;
                    RequestSave();
                }
            };
            panel.Children.Add(btn);
        }
        return new Popup
        {
            Child = new Border
            {
                Background = WpfBrushes.White, BorderBrush = WpfBrushes.LightGray,
                BorderThickness = new Thickness(1), Padding = new Thickness(4), Child = panel,
            },
            Placement = PlacementMode.Bottom, StaysOpen = false,
        };
    }

    // ─── アイコンピッカー ────────────────────────────────────────

    private Popup BuildIconPopup()
    {
        const double Cell    = 28;
        const double Gap     = 3;
        const int    Columns = 8;

        var panel = new WrapPanel { Width = Columns * (Cell + Gap * 2) + 2 };
        _iconPanel = panel;

        foreach (var icon in IconList)
        {
            bool isNone = icon.Length == 0;
            var btn = new WpfButton
            {
                Width = Cell, Height = Cell, Margin = new Thickness(Gap),
                Padding    = new Thickness(0),
                Content    = isNone
                    ? new TextBlock
                    {
                        Text = "✕", FontSize = 11, Foreground = WpfBrushes.Gray,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    }
                    : new System.Windows.Controls.Image
                    {
                        Source = RenderEmoji(icon), Width = 20, Height = 20,
                        Stretch = Stretch.Uniform,
                    },
                Tag        = icon,
                ToolTip    = isNone ? "アイコンなし" : null,
                Cursor     = WpfCursors.Hand,
            };

            // マウスが乗ったときだけ拡大する。RenderTransform なのでレイアウトは
            // 動かず、隣のアイコンを押しのけずに手前で大きくなる。
            var scale = new ScaleTransform(1, 1);
            btn.RenderTransform       = scale;
            btn.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            btn.MouseEnter += (s, _) => ZoomIconButton((UIElement)s, scale, 1.6);
            btn.MouseLeave += (s, _) => ZoomIconButton((UIElement)s, scale, 1.0);
            btn.Click += (s, _) =>
            {
                if (s is WpfButton b && b.Tag is string k)
                {
                    ViewModel.Icon = k;
                    if (_iconPopup != null) _iconPopup.IsOpen = false;
                    RequestSave();
                }
            };
            panel.Children.Add(btn);
        }

        return new Popup
        {
            Child = new Border
            {
                Background = WpfBrushes.White, BorderBrush = WpfBrushes.LightGray,
                BorderThickness = new Thickness(1), Padding = new Thickness(4), Child = panel,
            },
            Placement = PlacementMode.Bottom, StaysOpen = false,
        };
    }

    private static void ZoomIconButton(UIElement button, ScaleTransform scale, double to)
    {
        // 拡大中は手前に出す。WrapPanel は後の要素が上に描画されるため、
        // ZIndex を上げないと右隣・下隣のアイコンに欠けて見える。
        System.Windows.Controls.Panel.SetZIndex(button, to > 1 ? 1 : 0);

        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(110))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    // 選択中のアイコンだけ枠線を付けて示す
    private void UpdateIconSelection()
    {
        if (_iconPanel == null) return;
        foreach (var child in _iconPanel.Children)
        {
            if (child is not WpfButton b) continue;
            bool selected = (b.Tag as string) == ViewModel.Icon;
            b.BorderThickness = new Thickness(selected ? 2 : 1);
            b.BorderBrush     = selected ? WpfBrushes.CornflowerBlue : WpfBrushes.LightGray;
        }
    }

    // ─── フォントピッカー ────────────────────────────────────────

    private Popup BuildFontPopup()
    {
        var listBox = new WpfListBox { Width = 180, MaxHeight = 260, BorderThickness = new Thickness(0) };
        foreach (var font in FontList)
        {
            listBox.Items.Add(new ListBoxItem
            {
                Content = font,
                FontFamily = new WpfFontFamily(font),
                FontSize = 13,
                Tag = font,
            });
        }
        listBox.SelectionChanged += (_, _) =>
        {
            if (listBox.SelectedItem is ListBoxItem item && item.Tag is string font)
            {
                ViewModel.FontFamily = font;
                if (_fontPopup != null) _fontPopup.IsOpen = false;
                RequestSave();
            }
        };
        return new Popup
        {
            Child = new Border
            {
                Background = WpfBrushes.White, BorderBrush = WpfBrushes.LightGray,
                BorderThickness = new Thickness(1), Child = listBox,
            },
            Placement = PlacementMode.Bottom, StaysOpen = false,
        };
    }
}
