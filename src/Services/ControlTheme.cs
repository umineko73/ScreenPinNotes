using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SystemColors = System.Windows.SystemColors;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ScreenPinNotes.Services;

/// <summary>Window-scoped palette and templates, independent of the Windows theme.</summary>
public static class ControlTheme
{
    private static readonly DependencyProperty InstalledProperty = DependencyProperty.RegisterAttached(
        "Installed", typeof(bool), typeof(ControlTheme), new PropertyMetadata(false));

    public static void Apply(Window window, bool dark, bool dialog = false)
    {
        if (!(bool)window.GetValue(InstalledProperty))
        {
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/ScreenPinNotes;component/Resources/" +
                    (dialog ? "SettingsStyles.xaml" : "ControlStyles.xaml")),
            });
            window.SetValue(InstalledProperty, true);
            window.SourceInitialized += (_, _) => ApplyCaption(window);
        }
        System.Windows.Media.SolidColorBrush Brush(string value)
        {
            var brush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        }
        var surface = Brush(dark ? "#303030" : "#FFFFFF");
        var text = Brush(dark ? "#EEEEEE" : "#242424");
        var hover = Brush(dark ? "#444444" : "#EAF0F8");
        window.Resources["SettingsBackground"] = Brush(dark ? "#202020" : "#FFFFFF");
        window.Resources["SettingsSurface"] = surface;
        window.Resources["SettingsText"] = text;
        window.Resources["SettingsBorder"] = Brush(dark ? "#555555" : "#CCCCCC");
        window.Resources["SettingsHover"] = hover;
        window.Resources["ControlThumb"] = Brush(dark ? "#929292" : "#777777");
        // Default list selection and the ScrollViewer corner also use system resources.
        foreach (var key in new[] { SystemColors.ControlBrushKey, SystemColors.WindowBrushKey, SystemColors.ScrollBarBrushKey })
            window.Resources[key] = surface;
        foreach (var key in new[] { SystemColors.ControlTextBrushKey, SystemColors.WindowTextBrushKey })
            window.Resources[key] = text;
        window.Resources[SystemColors.HighlightBrushKey] = Brush(dark ? "#405F80" : "#C9DEF5");
        window.Resources[SystemColors.HighlightTextBrushKey] = text;
        window.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = hover;
        window.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = text;
        if (dialog)
        {
            window.SetResourceReference(Window.BackgroundProperty, "SettingsBackground");
            window.SetResourceReference(Window.ForegroundProperty, "SettingsText");
        }
        ApplyCaption(window);
    }

    private static void ApplyCaption(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || window.WindowStyle == WindowStyle.None) return;
        if (window.Resources["SettingsBackground"] is not SolidColorBrush background ||
            window.Resources["SettingsText"] is not SolidColorBrush foreground) return;
        // Explicit caption/text COLORREF values work even when Windows is in light mode.
        // https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
        int ColorRef(System.Windows.Media.Color color) => color.R | color.G << 8 | color.B << 16;
        var caption = ColorRef(background.Color);
        var text = ColorRef(foreground.Color);
        DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
