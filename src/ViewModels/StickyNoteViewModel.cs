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

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ScreenStickyNotes.Models;
using ScreenStickyNotes.Services;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfSolidBrush = System.Windows.Media.SolidColorBrush;

namespace ScreenStickyNotes.ViewModels;

public class StickyNoteViewModel : INotifyPropertyChanged
{
    // 背景（淡色）とヘッダー（濃色）の組。暖色→寒色→無彩色の順に並べる。
    // 既存ノートの互換のため yellow/blue/green/pink/purple/gray のキーは変更しない。
    public static readonly Dictionary<string, (string Bg, string Header)> ColorPresets = new()
    {
        // 暖色
        ["yellow"]  = ("#FFFDE7", "#F9A825"),
        ["amber"]   = ("#FEF3C7", "#B45309"),
        ["orange"]  = ("#FFEDD5", "#C2410C"),
        ["red"]     = ("#FEE2E2", "#B91C1C"),
        ["rose"]    = ("#FFE4E6", "#BE123C"),
        ["pink"]    = ("#FCE7F3", "#BE185D"),
        // 紫〜青
        ["fuchsia"] = ("#FAE8FF", "#A21CAF"),
        ["purple"]  = ("#EDE9FE", "#6D28D9"),
        ["violet"]  = ("#DDD6FE", "#5B21B6"),
        ["indigo"]  = ("#E0E7FF", "#4338CA"),
        ["blue"]    = ("#DBEAFE", "#1D4ED8"),
        ["sky"]     = ("#E0F2FE", "#0369A1"),
        // 寒色〜緑
        ["cyan"]    = ("#CFFAFE", "#0E7490"),
        ["teal"]    = ("#CCFBF1", "#0F766E"),
        ["emerald"] = ("#D1FAE5", "#047857"),
        ["green"]   = ("#DCFCE7", "#15803D"),
        ["lime"]    = ("#ECFCCB", "#4D7C0F"),
        ["olive"]   = ("#F7F7DC", "#827717"),
        // 無彩色・その他
        ["brown"]   = ("#EFEBE9", "#6D4C41"),
        ["stone"]   = ("#F5F5F4", "#57534E"),
        ["gray"]    = ("#F3F4F6", "#4B5563"),
        ["slate"]   = ("#F1F5F9", "#334155"),
        ["white"]   = ("#FFFFFF", "#9CA3AF"),
        ["dark"]    = ("#E5E7EB", "#111827"),
    };

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
        string.IsNullOrWhiteSpace(_model.Title) ? FirstLine : _model.Title!;

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
        _model.IsReadOnly ? Visibility.Visible : Visibility.Collapsed;

    public bool IsFolded
    {
        get => _model.IsFolded;
        set
        {
            _model.IsFolded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FoldIcon));
        }
    }

    public string FoldIcon => IsFolded ? "▼" : "▲";

    public string FontFamily
    {
        get => _model.FontFamily;
        set { _model.FontFamily = value; OnPropertyChanged(); }
    }

    public double FontSize
    {
        get => _model.FontSize;
        set { _model.FontSize = value; OnPropertyChanged(); }
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
        }
    }

    public double TitleIconSize =>
        Math.Clamp(Math.Ceiling(_model.TitleFontSize * 1.35), 17, 34);

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

    private WpfBrush _textForeground = WpfBrushes.Black;
    public WpfBrush TextForeground
    {
        get => _textForeground;
        private set { _textForeground = value; OnPropertyChanged(); }
    }

    public void RefreshSettings()
    {
        UpdateBrushes();
        OnPropertyChanged(nameof(FirstLine));
        OnPropertyChanged(nameof(DisplayTitle));
    }

    private void UpdateBrushes()
    {
        if (!ColorPresets.TryGetValue(_model.ColorKey, out var preset))
            preset = ColorPresets["yellow"];

        var bg = (WpfColor)WpfColorConverter.ConvertFromString(preset.Bg);
        var header = (WpfColor)WpfColorConverter.ConvertFromString(preset.Header);

        if (IsDarkTheme())
        {
            var darkBase = WpfColor.FromRgb(17, 24, 39);
            var darkPanel = Blend(header, darkBase, 0.86);
            var darkHeader = Blend(header, WpfColor.FromRgb(0, 0, 0), 0.25);

            BackgroundBrush = new WpfSolidBrush(WithOpacity(darkPanel));
            HeaderBrush = new WpfSolidBrush(WithOpacity(darkHeader));
            TitleBarBrush = new WpfSolidBrush(WithOpacity(Blend(darkHeader, darkPanel, 0.45)));
            TitleBarForeground = new WpfSolidBrush(WpfColor.FromRgb(249, 250, 251));
            TextForeground = new WpfSolidBrush(WpfColor.FromRgb(229, 231, 235));
            return;
        }

        BackgroundBrush = new WpfSolidBrush(WithOpacity(bg));
        HeaderBrush = new WpfSolidBrush(WithOpacity(header));
        TitleBarBrush = new WpfSolidBrush(WithOpacity(Blend(header, bg, 0.90)));
        TitleBarForeground = new WpfSolidBrush(Blend(header, WpfColor.FromRgb(0, 0, 0), 0.45));
        TextForeground = new WpfSolidBrush(WpfColor.FromRgb(17, 24, 39));
    }

    private WpfColor WithOpacity(WpfColor color)
        => WpfColor.FromArgb(
            (byte)Math.Round(255 * GetEffectiveOpacity()),
            color.R,
            color.G,
            color.B);

    private double GetEffectiveOpacity()
    {
        if (_forceOpaque) return 1.0;
        var boost = _isHovered ? Math.Clamp(_settings.HoverOpacityBoostPercent, 0, 90) : 0;
        return Math.Min(100, OpacityPercent + boost) / 100.0;
    }

    private static WpfColor Blend(WpfColor from, WpfColor to, double t)
    {
        byte Lerp(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
        return WpfColor.FromRgb(Lerp(from.R, to.R), Lerp(from.G, to.G), Lerp(from.B, to.B));
    }

    private bool IsDarkTheme()
        => string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
