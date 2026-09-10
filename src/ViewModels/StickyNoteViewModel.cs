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
        IsTitleBarHidden && _settings.ShowTitleBarHiddenSpine
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>左端の縦線の太さ。設定で変えられる。</summary>
    public double TitleSpineWidth => _settings.Layout.TitleBarHiddenSpineWidth;

    /// <summary>帯の左余白6pxと幅を避け、本文まで8pxの間隔を保つ。</summary>
    public Thickness NoteContentPadding => new(
        TitleSpineVisibility == Visibility.Visible ? 6 + TitleSpineWidth + 8 : 8,
        8, 8, 8);

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
        Math.Clamp(Math.Ceiling(_model.TitleFontSize * 1.5), 20, 38);

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
        OnPropertyChanged(nameof(NoteContentPadding));
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
