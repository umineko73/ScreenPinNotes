using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

public class SettingsWindowTests
{
    [WpfTheory]
    [InlineData("Light", "ja", 720)]
    [InlineData("Dark", "ja", 720)]
    [InlineData("Light", "en", 540)]
    [InlineData("Dark", "en", 1000)]
    public void SettingsControls_AlignAndRemainUsable(string theme, string language, double width)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var previousLanguage = app.Settings.Language;
        app.Settings.Language = language;
        var settings = new AppSettings { Theme = theme, Language = language };
        var window = new SettingsWindow(settings, app) { Width = width, Height = 1000 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var pickers = Descendants<ComboBox>(window).ToArray();
            Assert.Equal(6, pickers.Length);
            var left = pickers[0].TranslatePoint(new Point(), window).X;
            foreach (var picker in pickers)
            {
                Assert.Equal(left, picker.TranslatePoint(new Point(), window).X, 1);
                Assert.InRange(picker.ActualWidth, 80, picker.MaxWidth);
            }
            foreach (var check in Descendants<CheckBox>(window))
                Assert.Equal(left, check.TranslatePoint(new Point(), window).X, 1);

            foreach (var key in new[]
                     {
                         "SettingsTitleBar", "SettingsTheme", "SettingsStartup", "SettingsTaskbar",
                         "SettingsTrayClick", "SettingsFolding", "SettingsBackup",
                     })
                Assert.NotEqual(key, LocalizationService.T(key, language));

            // Render the real WPF controls for optional visual review, without changing saved settings.
            if (Environment.GetEnvironmentVariable("SCREENPINNOTES_SETTINGS_PREVIEW") is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
                var visual = (FrameworkElement)window.Content;
                var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(folder, $"settings-{theme}-{language}-{width}.png"));
                encoder.Save(stream);
            }

            Assert.IsType<System.Windows.Controls.Primitives.Popup>(pickers[0].Template.FindName("PART_Popup", pickers[0]));
            settings.Theme = theme == "Dark" ? "Light" : "Dark";
            typeof(SettingsWindow).GetMethod("ApplyTheme", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Assert.Equal(settings.Theme == "Dark" ? Color.FromRgb(32, 32, 32) : Colors.White,
                ((SolidColorBrush)window.Background).Color);
        }
        finally
        {
            window.Close();
            app.Settings.Language = previousLanguage;
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
