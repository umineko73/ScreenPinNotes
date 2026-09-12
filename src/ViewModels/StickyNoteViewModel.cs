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

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace ScreenPinNotes.ViewModels;

public class StickyNoteViewModel : INotifyPropertyChanged
{
    public static IReadOnlyDictionary<string, (string Bg, string Header)> ColorPresets => NoteAppearance.Presets;

    private readonly StickyNote _model;
    private readonly AppSettings _settings;
    private bool _forceOpaque;
    private bool _isHovered;
    public StickyNote Model => _model;

    public StickyNoteViewModel(StickyNote model, AppSettings settings)
    {
        _model = model;
        _settings = settings;
        UpdateBrushes();
    }

    public string Content
    {
        get => _model.Content;
        set
        {
            _model.Content = value;
            _model.UpdatedAt = DateTime.Now;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FirstLine));
            OnPropertyChanged(nameof(DisplayTitle));
            // 画像1枚だけかどうかで本文の余白と、設定によっては帯の有無も変わる。
            OnPropertyChanged(nameof(IsImageOnlyContent));
            OnPropertyChanged(nameof(NoteContentPadding));
            OnPropertyChanged(nameof(TitleSpineHandleWidth));
            OnPropertyChanged(nameof(TitleSpineVisibility));
        }
    }

    public string FirstLine
    {
        get
        {
            var line = _model.Content.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
            return line.Length > 0 ? line : LocalizationService.T("NoMemo");
        }
    }

    /// <summary>タイトルバーに直接入力する文字列。空なら本文の1行目にフォールバックする。</summary>
    public string Title
    {
        get => _model.Title ?? "";
        set
        {
            _model.Title = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    /// <summary>タイトルバーに実際に表示する文字列（Title が空なら FirstLine）。</summary>
    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(_model.Title) ? MarkdownRenderer.GetImageOnlyTarget(Content) ?? FirstLine : _model.Title!;

    public string ColorKey
    {
        get => _model.ColorKey;
        set { _model.ColorKey = value; UpdateBrushes(); OnPropertyChanged(); }
    }

    /// <summary>タイトルバーに表示する絵文字。空文字ならアイコンなし。</summary>
    public string Icon
    {
        get => _model.Icon;
        set
        {
            _model.Icon = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IconVisibility));
        }
    }

    public Visibility IconVisibility =>
        string.IsNullOrEmpty(_model.Icon) ? Visibility.Collapsed : Visibility.Visible;

    public string? TitleIconTooltip =>
        _model.IsExternalContent && !string.IsNullOrWhiteSpace(_model.ExternalContentPath)
            ? $"{ExternalFileLabel()}:\n{_model.ExternalContentPath}"
            : null;

    public string? TitleTooltip => TitleIconTooltip;

    public Visibility ReminderVisibility =>
        _model.HasReminder ? Visibility.Visible : Visibility.Collapsed;

    public string? ReminderTooltip =>
        _model.Reminder?.NextAt is DateTime nextAt
            ? $"{ReminderLabel()}:\n{FormatReminder(nextAt)}"
            : null;

    public bool IsTopmost
    {
        get => _model.IsTopmost;
        set { _model.IsTopmost = value; OnPropertyChanged(); }
    }

    public bool IsReadOnly
    {
        get => _model.IsReadOnly;
        set
        {
            _model.IsReadOnly = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EditLockVisibility));
        }
    }

    public Visibility EditLockVisibility =>
        _model.IsReadOnly || _model.IsExternalContent ? Visibility.Visible : Visibility.Collapsed;

    public bool IsPositionSeparated
    {
        get => _model.IsPositionSeparated;
        set
        {
            _model.IsPositionSeparated = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PositionSeparatedVisibility));
        }
    }

    public Visibility PositionSeparatedVisibility =>
        _model.IsPositionSeparated ? Visibility.Visible : Visibility.Collapsed;

    public bool IsExternalContent => _model.IsExternalContent;

    public void ClearExternalContentPath()
    {
        _model.ExternalContentPath = null;
        _model.ExternalImageWidthOverrides.Clear();
        OnPropertyChanged(nameof(IsExternalContent));
        OnPropertyChanged(nameof(EditLockVisibility));
        OnPropertyChanged(nameof(TitleIconTooltip));
        OnPropertyChanged(nameof(TitleTooltip));
    }

    public void SetReminder(DateTime? nextAt)
    {
        if (nextAt == null)
        {
            _model.Reminder = null;
        }
        else
        {
            _model.Reminder ??= new ReminderSettings();
            _model.Reminder.NextAt = nextAt;
            _model.Reminder.Recurrence = "None";
            _model.Reminder.LastTriggeredAt = null;
        }

        _model.UpdatedAt = DateTime.Now;
        RefreshReminder();
    }

    public void RefreshReminder()
    {
        OnPropertyChanged(nameof(ReminderVisibility));
        OnPropertyChanged(nameof(ReminderTooltip));
    }

    public bool IsFolded
    {
        get => _model.IsFolded;
        set
        {
            _model.IsFolded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FoldIcon));
            OnPropertyChanged(nameof(UsesTightImageLayout));
            OnPropertyChanged(nameof(NoteContentPadding));
            OnPropertyChanged(nameof(TitleSpineHandleWidth));
            OnPropertyChanged(nameof(ContentFontSize));
        }
    }

    public string FoldIcon => IsFolded ? "⮟" : "⮝";

    public bool IsTitleBarHidden
    {
        get => _model.IsTitleBarHidden;
        set
        {
            _model.IsTitleBarHidden = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TitleBarVisibility));
            OnPropertyChanged(nameof(TitleSpineVisibility));
            OnPropertyChanged(nameof(NoteContentPadding));
            OnPropertyChanged(nameof(TitleSpineHandleWidth));
            OnPropertyChanged(nameof(NoteEditorPadding));
            OnPropertyChanged(nameof(ContentFontSize));
        }
    }

    /// <summary>
    /// 常時表示のタイトルバー。隠す設定のときは場所ごと消し、
    /// 代わりに右上のホバーオーバーレイがボタンを引き受ける。
    /// </summary>
    public Visibility TitleBarVisibility =>
        IsTitleBarHidden ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// タイトルバーを隠している付箋の左端に出す縦線。畳むとタイトルバーを
    /// 出している付箋との違いがアイコンの左右だけになり、見分けが付かない。
    /// 色や不透明度に左右されない形の手掛かりとして、TitleBarVisibility の裏返しで出す。
    /// 目印が要らない人は設定で消せる。
    /// </summary>
    public Visibility TitleSpineVisibility =>
        IsTitleBarHidden && _settings.ShowTitleBarHiddenSpine && !HidesSpineOverImage
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>画像1枚だけの付箋で帯を出さない設定か。</summary>
    private bool HidesSpineOverImage =>
        IsImageOnlyContent && MatchesImageSpineStyle(AppSettings.ImageSpineHidden);

    /// <summary>画像1枚だけの付箋で、帯のぶんだけ画像を右へ寄せる設定か。</summary>
    private bool ReservesSpineGutterOverImage =>
        !IsImageOnlyContent || MatchesImageSpineStyle(AppSettings.ImageSpineGutter);

    private bool MatchesImageSpineStyle(string style) =>
        string.Equals(_settings.ImageOnlySpineStyle, style, StringComparison.OrdinalIgnoreCase);

    /// <summary>左端の縦線の太さ。設定で変えられる。</summary>
    public double TitleSpineWidth => _settings.Layout.TitleBarHiddenSpineWidth;

    /// <summary>
    /// 縦線の角の丸み。太さの半分にして両端を半円にする。端から離して置く
    /// ときだけ丸める。端に貼り付ける従来の置き方では、丸めても付箋の縁に
    /// 隠れて見えないうえ、以前の見た目をそのまま残しておきたい。
    /// </summary>
    public double TitleSpineCornerRadius =>
        UsesInsetSpine ? _settings.Layout.TitleBarHiddenSpineWidth / 2 : 0;

    /// <summary>付箋の外枠。XAML の RootBorder.BorderThickness と合わせてある。</summary>
    private const double RootBorderThickness = 1;

    /// <summary>
    /// 縦線の置き場所。従来（Edge）は左端に貼り付けるだけなので余白は要らない。
    /// Inset のときは設定ぶん右へずらし、上下も同じだけ空けて、四方に等しい
    /// 余白を持つ1本の線にする。角を大きく丸めていて、それだけでは
    /// RootBorder の角丸クリップに端が削られてしまうときは、削られる高さまで
    /// 上下を広げる（削られると端に行くほど細く見え、太さが一定にならない）。
    /// </summary>
    public Thickness TitleSpineMargin
    {
        get
        {
            if (!UsesInsetSpine) return default;

            var inset = _settings.Layout.TitleBarHiddenSpineInset;
            var vertical = Math.Max(
                inset,
                CornerClipDepth(_settings.Layout.NoteCornerRadius, RootBorderThickness + inset));
            return new Thickness(inset, vertical, 0, vertical);
        }
    }

    /// <summary>本文の手前まで、帯と左右の余白をまとめて持ち手にする。</summary>
    public double TitleSpineHandleWidth =>
        Math.Max(SpineExtent, UsesTightImageLayout ? NoteContentPadding.Left : NoteTextPadding.Left);

    private bool UsesInsetSpine =>
        !string.Equals(_settings.TitleBarHiddenSpineStyle, AppSettings.SpineStyleEdge,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 半径 <paramref name="radius"/> の角丸から左へ <paramref name="x"/> の位置で、
    /// 上端が何ピクセル削られるかを返す。円弧の中心は (radius, radius) にあるので、
    /// そこからの高さの差がそのまま削られる量になる。角の外（x >= radius）なら0。
    /// </summary>
    private static double CornerClipDepth(double radius, double x)
    {
        if (radius <= 0 || x >= radius) return 0;
        // クリップは RootBorder の内側なので、外枠の分を引いた位置で見る。
        var depth = radius - Math.Sqrt((radius * radius) - ((radius - x) * (radius - x)));
        return Math.Max(0, depth - RootBorderThickness);
    }

    /// <summary>帯が出ていないときの本文の余白。XAML の既定値と合わせてある。</summary>
    private const double DefaultContentPadding = 8;

    /// <summary>本文が画像1枚だけか。余白の詰め方が変わる。</summary>
    public bool IsImageOnlyContent => MarkdownRenderer.GetImageOnlyTarget(Content) != null;

    /// <summary>
    /// 画像を縁まで広げる表示か。畳んで1行になると画像ではなく画像パスの文字が
    /// 出るので、そのときは詰めない。詰めると文字が縁に貼り付き、1行ぶんの
    /// 高さも余白のぶんだけ低くなって、他の付箋と並びが揃わなくなる。
    /// </summary>
    public bool UsesTightImageLayout => IsImageOnlyContent && !IsFolded;

    /// <summary>帯の右端までの幅。帯を出していないときは0。</summary>
    private double SpineExtent =>
        TitleSpineVisibility != Visibility.Visible
            ? 0
            : SpineInset + _settings.Layout.TitleBarHiddenSpineWidth;

    private double SpineInset =>
        UsesInsetSpine ? _settings.Layout.TitleBarHiddenSpineInset : 0;

    /// <summary>
    /// 本文の余白。帯を出しているときは、帯とその手前の余白ぶんだけ左を広げ、
    /// 帯の右側にも余白と同じ間隔を残す。広げないと帯と文字が数ピクセルまで
    /// 近づき、目印ではなく本文の飾り罫のように見えてしまう。
    ///
    /// ただし本文が画像1枚だけのときは、余白を詰めて画像を目一杯見せる。
    /// 文字と違って画像は余白の中で読むものではなく、付箋を画像の額縁として
    /// 使うことのほうが多い。帯を出しているときだけ、帯とその左右の隙間ぶんを残す。
    /// </summary>
    public Thickness NoteContentPadding
    {
        get
        {
            if (UsesTightImageLayout)
            {
                // 帯の場所を空けるのは「並べる」設定のときだけ。重ねる・出さない
                // なら避けるものが無いので、四方とも縁まで詰める。
                var gutter = SpineExtent > 0 && ReservesSpineGutterOverImage
                    ? SpineExtent + SpineInset
                    : 0;
                return new Thickness(gutter, 0, 0, 0);
            }

            return NoteTextPadding;
        }
    }

    /// <summary>
    /// 本文が文字のときの余白。画像以外の本文、編集中、畳んだ1行表示で使う。
    /// </summary>
    public Thickness NoteTextPadding => new(
        SpineExtent + DefaultContentPadding,
        DefaultContentPadding, DefaultContentPadding, DefaultContentPadding);

    /// <summary>
    /// 編集欄の余白。編集中は画像も生の Markdown 文字列なので、本文が画像1枚でも
    /// 詰めない。詰めると文字が付箋の縁に貼り付いて読めなくなる。
    /// </summary>
    public Thickness NoteEditorPadding => NoteTextPadding;

    /// <summary>付箋の四隅の丸み。0 なら角のまま。</summary>
    public CornerRadius NoteCornerRadius => new(_settings.Layout.NoteCornerRadius);

    /// <summary>
    /// リマインダーの明滅枠は外枠の内側をなぞるので、そのぶん丸みを小さくする。
    /// 同じ値だと角で外枠からはみ出して見える。
    /// </summary>
    public CornerRadius NoteFlashCornerRadius =>
        new(Math.Max(0, _settings.Layout.NoteCornerRadius - 1));

    public string FontFamily
    {
        get => _model.FontFamily;
        set { _model.FontFamily = value; OnPropertyChanged(); }
    }

    public double FontSize
    {
        get => _model.FontSize;
        set
        {
            _model.FontSize = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ContentFontSize));
        }
    }

    /// <summary>タイトルバーに表示する文字のサイズ。</summary>
    public double TitleFontSize
    {
        get => _model.TitleFontSize;
        set
        {
            _model.TitleFontSize = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TitleBarHeight));
            OnPropertyChanged(nameof(TitleIconSize));
            OnPropertyChanged(nameof(ContentFontSize));
        }
    }

    /// <summary>
    /// 本文コントロールに実際に適用するフォントサイズ。
    /// タイトルバーを隠していて折りたたんだ状態では、本文の1行目が
    /// タイトルバーの代わりになるので、見た目をタイトル文字サイズに揃える。
    /// </summary>
    public double ContentFontSize =>
        IsFolded && IsTitleBarHidden ? TitleFontSize : FontSize;

    public double TitleIconSize =>
        Math.Clamp(Math.Ceiling(_model.TitleFontSize * 1.5), 20, 54);

    public int OpacityPercent
    {
        get => Math.Clamp(_model.OpacityPercent, 10, 100);
        set
        {
            _model.OpacityPercent = Math.Clamp(value, 10, 100);
            UpdateBrushes();
            OnPropertyChanged();
        }
    }

    public void SetForceOpaque(bool forceOpaque)
    {
        if (_forceOpaque == forceOpaque) return;
        _forceOpaque = forceOpaque;
        UpdateBrushes();
    }

    public void SetHovered(bool isHovered)
    {
        if (_isHovered == isHovered) return;
        _isHovered = isHovered;
        UpdateBrushes();
    }

    /// <summary>
    /// タイトルバーの高さ。文字を大きくしても切れないよう追従させる。
    /// 折りたたみ時のウィンドウ高さもこの値になる。
    /// </summary>
    public double TitleBarHeight =>
        Math.Max(28, Math.Ceiling(_model.TitleFontSize * 1.9));

    private WpfBrush _backgroundBrush = WpfBrushes.White;
    public WpfBrush BackgroundBrush
    {
        get => _backgroundBrush;
        private set { _backgroundBrush = value; OnPropertyChanged(); }
    }

    private WpfBrush _headerBrush = WpfBrushes.Orange;
    public WpfBrush HeaderBrush
    {
        get => _headerBrush;
        private set { _headerBrush = value; OnPropertyChanged(); }
    }

    // タイトルバー専用の色。HeaderBrush をそのまま帯として敷くと目立ちすぎるため、
    // 背景色へ寄せて弱めた色を使う。付箋の外枠（RootBorder）は引き続き
    // HeaderBrush そのままなので、色の手掛かり自体は失われない。
    private WpfBrush _titleBarBrush = WpfBrushes.Orange;
    public WpfBrush TitleBarBrush
    {
        get => _titleBarBrush;
        private set { _titleBarBrush = value; OnPropertyChanged(); }
    }

    // 明るくなったタイトルバーでも読めるよう、ヘッダー色を黒へ寄せた文字色
    private WpfBrush _titleBarForeground = WpfBrushes.Black;
    public WpfBrush TitleBarForeground
    {
        get => _titleBarForeground;
        private set { _titleBarForeground = value; OnPropertyChanged(); }
    }

    // 付箋の外枠。設定で「なし」「グレー」「付箋の色」または任意の色を選べる。
    private WpfBrush _noteBorderBrush = WpfBrushes.Transparent;
    public WpfBrush NoteBorderBrush
    {
        get => _noteBorderBrush;
        private set { _noteBorderBrush = value; OnPropertyChanged(); }
    }

    private WpfBrush _textForeground = WpfBrushes.Black;
    public WpfBrush TextForeground
    {
        get => _textForeground;
        private set { _textForeground = value; OnPropertyChanged(); }
    }

    public void RefreshSettings()
    {
        UpdateBrushes();
        OnPropertyChanged(nameof(TitleSpineVisibility));
        OnPropertyChanged(nameof(TitleSpineWidth));
        OnPropertyChanged(nameof(TitleSpineCornerRadius));
        OnPropertyChanged(nameof(TitleSpineMargin));
        OnPropertyChanged(nameof(TitleSpineHandleWidth));
        OnPropertyChanged(nameof(NoteContentPadding));
        OnPropertyChanged(nameof(NoteEditorPadding));
        OnPropertyChanged(nameof(NoteCornerRadius));
        OnPropertyChanged(nameof(NoteFlashCornerRadius));
        OnPropertyChanged(nameof(FirstLine));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(TitleIconTooltip));
        OnPropertyChanged(nameof(TitleTooltip));
    }

    private void UpdateBrushes()
    {
        var appearance = new NoteAppearance(_model, _settings, _forceOpaque, _isHovered);
        BackgroundBrush = appearance.BackgroundBrush;
        HeaderBrush = appearance.HeaderBrush;
        TitleBarBrush = appearance.TitleBarBrush;
        TitleBarForeground = appearance.TitleBarForeground;
        TextForeground = appearance.TextForeground;
        NoteBorderBrush = appearance.NoteBorderBrush;
    }

    public bool UsesDarkNoteColors => NoteAppearance.UsesDarkColors(_model, _settings);

    private string ExternalFileLabel()
        => LocalizationService.T("ExternalFile", _settings.Language);

    private string ReminderLabel()
        => LocalizationService.T("ReminderDialogTitle", _settings.Language);

    private string FormatReminder(DateTime nextAt)
    {
        return nextAt.ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.GetCultureInfo(_settings.Language));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
