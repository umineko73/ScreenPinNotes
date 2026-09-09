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
    [InlineData("Light")]
    [InlineData("Dark")]
    public void SelectedIcon_HasItsOwnRenderedImageBeforeOpeningDropdown(string theme)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var settings = new AppSettings { Theme = theme };
        settings.NoteDefaults.Icon = "🦊";
        var window = new SettingsWindow(settings, app);
        try
        {
            window.Show();
            window.UpdateLayout();
            var picker = Descendants<ComboBox>(window).Single(c =>
                c.SelectedItem is ComboBoxItem { Tag: "🦊" });
            var presenter = (ContentPresenter)picker.Template.FindName("SelectionContent", picker);
            var image = Assert.Single(Descendants<Image>(presenter));
            Assert.NotNull(image.Source);
            Assert.True(image.ActualWidth > 0);
            Assert.True(image.ActualHeight > 0);
            picker.IsDropDownOpen = true;
            window.UpdateLayout();
            picker.IsDropDownOpen = false;
            window.UpdateLayout();
            Assert.NotNull(Assert.Single(Descendants<Image>(presenter)).Source);
        }
        finally { window.Close(); }
    }

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
            Assert.Equal(7, pickers.Length);
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

    // 帯を出さない設定のときは太さの欄を触れないようにする。効かない欄が残ると迷う。
    // 書き込み側はここで走らせない。チェックを実際に切り替えると Save() が
    // App.Current.Settings を本物のデータフォルダへ書き出してしまうため。
    [WpfTheory]
    [InlineData(true, 6.0)]
    [InlineData(false, 3.0)]
    public void TitleBarSpineEditor_ReflectsTheCurrentSettings(bool showSpine, double width)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var previousLanguage = app.Settings.Language;
        app.Settings.Language = "ja";
        var settings = new AppSettings { Language = "ja", ShowTitleBarHiddenSpine = showSpine };
        settings.Layout.TitleBarHiddenSpineWidth = width;
        var window = new SettingsWindow(settings, app);
        try
        {
            window.Show();
            window.UpdateLayout();

            var toggle = Descendants<CheckBox>(window).Single(box =>
                box.Content is TextBlock { Text: "左端に帯を出す" });
            Assert.Equal(showSpine, toggle.IsChecked);

            // 1px を選べるのは帯の太さの欄だけ（本文の文字サイズは 8 から）。
            var picker = Descendants<ComboBox>(window).Single(combo =>
                combo.Items.Cast<object>().Any(item => item is ComboBoxItem { Tag: 1.0 }));
            Assert.Equal(width, Assert.IsType<ComboBoxItem>(picker.SelectedItem).Tag);
            Assert.Equal(showSpine, picker.IsEnabled);
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
