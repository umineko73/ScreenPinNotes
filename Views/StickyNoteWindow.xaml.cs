using System.Diagnostics;
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
using WpfBrushes     = System.Windows.Media.Brushes;
using WpfButton      = System.Windows.Controls.Button;
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
    private const double TitleBarHeight   = 28;
    private const double UnfoldedMinWidth = 280;
    private const double ResizeBorder     = 5;
    private const double SnapDistance   = 10;

    private static readonly string[] FontList =
    [
        "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo",
        "BIZ UDGothic", "BIZ UDPGothic", "BIZ UDMincho",
        "MS Gothic", "MS Mincho", "Segoe UI", "Consolas",
    ];

    private double     _unfoldedHeight;
    private Popup?     _colorPopup;
    private Popup?     _fontPopup;
    private bool       _isDragging;
    private double     _dragOffsetX, _dragOffsetY;
    private bool       _suppressTextChange;
    private bool       _isEditMode;
    private bool       _suppressViewMode;
    private WrapPanel? _colorPanel;
    private Hyperlink? _contextMenuLink;
    private MenuItem   _openLinkItem  = new();
    private MenuItem   _convertLinkItem = new();

    public StickyNoteViewModel ViewModel => (StickyNoteViewModel)DataContext;

    public StickyNoteWindow(StickyNoteViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        Left    = vm.Model.X;
        Top     = vm.Model.Y;
        Width   = vm.Model.Width;
        Height  = vm.Model.Height;
        Topmost = vm.IsTopmost;
        _unfoldedHeight = vm.Model.Height;

        _colorPopup = BuildColorPopup();
        _fontPopup  = BuildFontPopup();
        ContentBox.ContextMenu = BuildContentContextMenu();
        System.Windows.DataObject.AddPastingHandler(ContentBox, OnPaste);

        // ポップアップは別HWNDのため開くとウィンドウが非アクティブになる。
        // 開いている間はビューモードへの移行を抑止する。
        foreach (var popup in new[] { _colorPopup, _fontPopup })
        {
            popup.Opened += (_, _) => _suppressViewMode = true;
            popup.Closed += (_, _) =>
            {
                _suppressViewMode = false;
                if (_isEditMode) Dispatcher.BeginInvoke(() => ContentBox.Focus());
            };
        }

        // アプリ切り替え時もビューモードへ
        Deactivated += (_, _) => EnterViewMode();

        Loaded += (_, _) =>
        {
            LoadContent(vm.Content);
            if (vm.IsFolded)
            {
                ContentBox.Visibility = Visibility.Collapsed;
                Height = TitleBarHeight;
                SetResizeEnabled(false);
            }
            // 新規（空）付箋はすぐ編集モードで開始
            if (string.IsNullOrEmpty(vm.Content))
                Dispatcher.BeginInvoke(EnterEditMode);
        };
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
        if (_isEditMode) return;
        _isEditMode = true;
        ContentBox.IsReadOnly = false;
        ContentBox.Cursor = WpfCursors.IBeam;
        ContentBox.BorderThickness = new Thickness(2);
        ContentBox.BorderBrush = WpfBrushes.CornflowerBlue;
        ContentBox.ToolTip = null;
        UpdateControlsVisibility();
        ContentBox.Focus();
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
        ContentBox.ToolTip = "クリックして編集";
        UpdateControlsVisibility();
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
            chrome.ResizeBorderThickness = new Thickness(enabled ? ResizeBorder : 0);
        }

        if (enabled)
        {
            MinWidth  = UnfoldedMinWidth;
            MaxWidth  = double.PositiveInfinity;
            MinHeight = TitleBarHeight;
            MaxHeight = double.PositiveInfinity;
        }
        else
        {
            MinWidth  = MaxWidth  = Width;
            MinHeight = MaxHeight = TitleBarHeight;
        }
    }

    // 折りたたみ状態と編集モードの両方を考慮してステータスバーの表示を更新
    private void UpdateControlsVisibility()
    {
        StatusBar.Visibility = (_isEditMode && !ViewModel.IsFolded)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // View モード: クリックでリンクを直接開く / 非リンクならEdit モードへ
    // Edit モード: Ctrl+クリックでリンクを開く
    private void ContentBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var target = GetHyperlinkAt(e.GetPosition(ContentBox));

        if (!_isEditMode)
        {
            if (target != null)
            {
                OpenTarget(target);
                e.Handled = true;
                return;
            }
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
        // ステータスバー内への移動は編集モードを維持する。
        // ここで Focus() を呼び戻すとボタンのマウスキャプチャを奪い Click が発火しなくなる。
        if (_suppressViewMode) return;
        if (IsDescendantOf(e.NewFocus as DependencyObject, StatusBar)) return;
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

    // ─── 貼り付け（リンク検出付き） ──────────────────────────────

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(WpfDataFormats.UnicodeText)) return;
        e.CancelCommand();

        var clipboard = ((string)e.DataObject.GetData(WpfDataFormats.UnicodeText))
            .Replace("\r\n", "\n").Replace("\r", "\n");

        var beforeText = RangeToPlain(ContentBox.Document.ContentStart, ContentBox.Selection.Start);
        var afterText  = RangeToPlain(ContentBox.Selection.End, ContentBox.Document.ContentEnd);
        var newText    = beforeText + clipboard + afterText;
        var caretOff   = beforeText.Length + clipboard.Length;

        LoadContent(newText);
        RestoreCaretAt(caretOff);

        ViewModel.Content = newText;
        RequestSave();
    }

    private string RangeToPlain(TextPointer from, TextPointer to)
        => new TextRange(from, to).Text.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n');

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
        return cm;
    }

    private void ContentBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        _contextMenuLink = GetHyperlinkAtCaret();
        _openLinkItem.IsEnabled = _contextMenuLink != null;

        var sel = ContentBox.Selection.IsEmpty ? "" : ContentBox.Selection.Text.Trim();
        _convertLinkItem.IsEnabled = LinkDetector.IsLink(sel);
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
        var before   = RangeToPlain(ContentBox.Document.ContentStart, ContentBox.Selection.Start);
        var after    = RangeToPlain(ContentBox.Selection.End, ContentBox.Document.ContentEnd);
        var newText  = before + sel + after;    // sel は URL なので LoadContent でリンク検出される
        var caretOff = before.Length + sel.Length;

        LoadContent(newText);
        RestoreCaretAt(caretOff);

        ViewModel.Content = newText;
        RequestSave();
    }

    // ─── ドラッグ & スナップ ─────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        EnterViewMode(); // タイトルバークリックで編集モードを終了

        if (e.ClickCount == 2) { ToggleFold(); return; }

        _isDragging = true;
        var (dpiX, dpiY) = GetDpi();
        var cur = System.Windows.Forms.Cursor.Position;
        _dragOffsetX = cur.X / dpiX - Left;
        _dragOffsetY = cur.Y / dpiY - Top;
        ((UIElement)sender).CaptureMouse();
    }

    private void TitleBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging) return;
        var (dpiX, dpiY) = GetDpi();
        var cur = System.Windows.Forms.Cursor.Position;
        Left = cur.X / dpiX - _dragOffsetX;
        Top  = cur.Y / dpiY - _dragOffsetY;
        SnapToAll();
    }

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
        ViewModel.Model.X = Left;
        ViewModel.Model.Y = Top;
        RequestSave();
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

    // ─── 折りたたみ ──────────────────────────────────────────────

    private void Fold_Click(object sender, RoutedEventArgs e) => ToggleFold();

    private void ToggleFold()
    {
        if (ViewModel.IsFolded)
        {
            ContentBox.Visibility = Visibility.Visible;
            ViewModel.IsFolded = false;
            SetResizeEnabled(true);
            AnimateHeight(TitleBarHeight, _unfoldedHeight);
        }
        else
        {
            if (_isEditMode) EnterViewMode(); // 折りたたみ時は閲覧モードに戻す
            _unfoldedHeight = Height;
            // アニメーション中の SizeChanged で Model.Height が
            // 途中の値に上書きされないよう先にフラグを立てる
            ViewModel.IsFolded = true;
            AnimateHeight(Height, TitleBarHeight, () =>
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

    private void AddNote_Click(object sender, RoutedEventArgs e) => App.Current.AddNewNote();

    private void Pin_Changed(object sender, RoutedEventArgs e)
    {
        Topmost = ViewModel.IsTopmost;
        RequestSave();
    }

    private void FontSmaller_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.FontSize > 8) { ViewModel.FontSize -= 1; RequestSave(); }
    }

    private void FontLarger_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.FontSize < 48) { ViewModel.FontSize += 1; RequestSave(); }
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
        if (!ViewModel.IsFolded)
        {
            ViewModel.Model.Width  = Width;
            ViewModel.Model.Height = Height;
            RequestSave();
        }
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (_isDragging) return;
        ViewModel.Model.X = Left;
        ViewModel.Model.Y = Top;
        RequestSave();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) { }

    // ─── 自動保存（デバウンス） ──────────────────────────────────

    private System.Threading.Timer? _saveTimer;
    private void RequestSave()
    {
        _saveTimer?.Dispose();
        _saveTimer = new System.Threading.Timer(_ =>
        {
            Dispatcher.Invoke(() => App.Current.SaveAll());
        }, null, 800, System.Threading.Timeout.Infinite);
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
