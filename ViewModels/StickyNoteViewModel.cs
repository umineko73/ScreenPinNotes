using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ScreenStickyNotes.Models;
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
    public StickyNote Model => _model;

    public StickyNoteViewModel(StickyNote model)
    {
        _model = model;
        UpdateBrushes();
    }

    public string Content
    {
        get => _model.Content;
        set { _model.Content = value; _model.UpdatedAt = DateTime.Now; OnPropertyChanged(); OnPropertyChanged(nameof(FirstLine)); }
    }

    public string FirstLine
    {
        get
        {
            var line = _model.Content.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
            return line.Length > 0 ? line : "（メモなし）";
        }
    }

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
        }
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

    private void UpdateBrushes()
    {
        if (!ColorPresets.TryGetValue(_model.ColorKey, out var preset))
            preset = ColorPresets["yellow"];

        BackgroundBrush = new WpfSolidBrush(
            (WpfColor)WpfColorConverter.ConvertFromString(preset.Bg));
        HeaderBrush = new WpfSolidBrush(
            (WpfColor)WpfColorConverter.ConvertFromString(preset.Header));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
