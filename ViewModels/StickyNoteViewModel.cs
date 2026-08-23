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
    public static readonly Dictionary<string, (string Bg, string Header)> ColorPresets = new()
    {
        ["yellow"] = ("#FFFDE7", "#F9A825"),
        ["blue"]   = ("#DBEAFE", "#1D4ED8"),
        ["green"]  = ("#DCFCE7", "#15803D"),
        ["pink"]   = ("#FCE7F3", "#BE185D"),
        ["purple"] = ("#EDE9FE", "#6D28D9"),
        ["gray"]   = ("#F3F4F6", "#4B5563"),
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
            OnPropertyChanged(nameof(ControlsVisibility));
            OnPropertyChanged(nameof(TitleVisibility));
        }
    }

    public string FoldIcon => IsFolded ? "▼" : "▲";

    public Visibility ControlsVisibility => IsFolded ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TitleVisibility    => IsFolded ? Visibility.Visible   : Visibility.Collapsed;

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
