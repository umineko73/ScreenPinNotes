// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3 of the License.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
    [WpfFact]
    public void CategoriesCanBeOpenedIndependently()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var window = new SettingsWindow(new AppSettings(), app);
        try
        {
            window.Show();
            window.UpdateLayout();
            var categories = Descendants<Expander>(window).ToArray();
            Assert.Equal(4, categories.Length);
            Assert.True(categories[0].IsExpanded);
            Assert.All(categories.Skip(1), category => Assert.False(category.IsExpanded));
            categories[1].IsExpanded = true;
            categories[0].IsExpanded = false;
            window.UpdateLayout();
            Assert.True(((FrameworkElement)categories[1].Content).IsVisible);
            Assert.False(((FrameworkElement)categories[0].Content).IsVisible);
        }
        finally { window.Close(); }
    }

    // 選択欄は今の設定を選んだ状態で開く。一覧に無い音（settings.json に書いたパスなど）も消さずに見せる。
    [WpfTheory]
    [InlineData(ReminderSound.DefaultSound)]
    [InlineData(@"C:\nowhere\custom.wav")]
    public void ReminderSoundPicker_ShowsTheCurrentSound(string sound)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var settings = new AppSettings { ReminderSound = sound };
        var window = new SettingsWindow(settings, app);
        try
        {
            window.Show();
            ExpandCategories(window);
            window.UpdateLayout();
            var picker = Descendants<ComboBox>(window).Single(c => c.Name == "ReminderSoundPicker");
            Assert.Equal(sound, ((ComboBoxItem)picker.SelectedItem).Tag);
            Assert.Equal(ReminderSound.DisplayName(sound), ((ComboBoxItem)picker.SelectedItem).Content);
            Assert.True(picker.ActualWidth > 0);
            Assert.Contains(Descendants<Button>(window),
                b => Equals(b.Content, LocalizationService.T("SettingsReminderSoundPreview")));
        }
        finally { window.Close(); }
    }

    [WpfTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void DefaultIcon_ShowsItsRenderedImageOnThePickerButton(string theme)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var settings = new AppSettings { Theme = theme };
        settings.NoteDefaults.Icon = "🦊";
        var window = new SettingsWindow(settings, app);
        try
        {
            window.Show();
            ExpandCategories(window);
            var image = Descendants<Image>(window).Single(i => i.Name == "DefaultIconImage");
            Assert.NotNull(image.Source);
            Assert.True(image.ActualWidth > 0);
            Assert.True(image.ActualHeight > 0);
        }
        finally { window.Close(); }
    }

    // 既定の色は付箋のパレットと同じ色見本で選ぶ。内部の名前（yellow など）は画面に出さない。
    [WpfFact]
    public void DefaultColor_IsChosenFromSwatchesWithoutInternalNames()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var settings = new AppSettings();
        settings.NoteDefaults.ColorKey = "blue";
        var window = new SettingsWindow(settings, app);
        try
        {
            window.Show();
            ExpandCategories(window);
            var swatches = Descendants<StackPanel>(window).Single(p => p.Name == "DefaultColorSwatches");
            var buttons = Descendants<Button>(swatches).ToArray();
            Assert.Equal(NoteAppearance.Presets.Count, buttons.Length);
            var selected = Assert.Single(buttons, b => ((TextBlock)b.Content).Visibility == Visibility.Visible);
            Assert.Equal("blue", selected.Tag);
            foreach (var text in Descendants<TextBlock>(window))
                Assert.DoesNotContain(text.Text, NoteAppearance.Presets.Keys);
        }
        finally { window.Close(); }
    }

    // テーマはライト・ダーク・システムの3択。システムは Windows のアプリ モードに従う。
    [WpfTheory]
    [InlineData(AppSettings.ThemeLight, false, false)]
    [InlineData(AppSettings.ThemeDark, false, true)]
    [InlineData(AppSettings.ThemeSystem, false, false)]
    [InlineData(AppSettings.ThemeSystem, true, true)]
    public void ThemePicker_OffersSystemAndFollowsWindows(string theme, bool windowsDark, bool expectedDark)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        SystemTheme.Override = () => windowsDark;
        var settings = new AppSettings { Theme = theme };
        var window = new SettingsWindow(settings, app);
        try
        {
            Assert.Equal(expectedDark, settings.IsDarkTheme);
            window.Show();
            ExpandCategories(window);
            var picker = Descendants<ComboBox>(window).Single(combo =>
                combo.Items.Cast<object>().Any(item => item is ComboBoxItem { Tag: AppSettings.ThemeSystem }));
            Assert.Equal(3, picker.Items.Count);
            Assert.Equal(theme, Assert.IsType<ComboBoxItem>(picker.SelectedItem).Tag);
            Assert.Equal(expectedDark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(0xF3, 0xF3, 0xF3),
                ((SolidColorBrush)window.Background).Color);
        }
        finally
        {
            window.Close();
            SystemTheme.Override = null;
        }
    }

    [Theory]
    [InlineData("system", AppSettings.ThemeSystem)]
    [InlineData("DARK", AppSettings.ThemeDark)]
    [InlineData("blue", AppSettings.ThemeLight)]
    public void Normalize_KeepsTheThreeThemes(string stored, string expected)
    {
        var settings = new AppSettings { Theme = stored };
        settings.Normalize();
        Assert.Equal(expected, settings.Theme);
    }

    // ショートカットは「適用」を押さずにその場で登録する。説明は1行に収まる短さ。
    [WpfFact]
    public void HotkeyCards_HaveNoApplyButtonAndShortHints()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var previousLanguage = app.Settings.Language;
        app.Settings.Language = "ja";
        var window = new SettingsWindow(new AppSettings { Language = "ja" }, app);
        try
        {
            window.Show();
            ExpandCategories(window);
            Assert.DoesNotContain(Descendants<Button>(window), b => Equals(b.Content, LocalizationService.T("HotkeyApply")));
            Assert.Contains(Descendants<Button>(window), b => Equals(b.ToolTip, LocalizationService.T("HotkeyDefault")));
            Assert.Contains(Descendants<Button>(window), b => Equals(b.ToolTip, LocalizationService.T("HotkeyDisable")));
            foreach (var key in new[] { "HotkeyHint", "ClipboardHotkeyHint" })
                foreach (var language in new[] { "ja", "en" })
                    Assert.InRange(LocalizationService.T(key, language).Length, 1, 100);
        }
        finally
        {
            window.Close();
            app.Settings.Language = previousLanguage;
        }
    }

    [WpfTheory]
    [InlineData("Light", "ja", 760)]
    [InlineData("Dark", "ja", 760)]
    [InlineData("Light", "en", 560)]
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
            ExpandCategories(window);
            window.UpdateLayout();
            var pickers = Descendants<ComboBox>(window).ToArray();
            Assert.Equal(12, pickers.Length);

            // 操作部品はカードの右端にそろう。欄の幅は2種類だけ。
            double Right(FrameworkElement e) => e.TranslatePoint(new Point(e.ActualWidth, 0), window).X;
            var right = Right(Descendants<CheckBox>(window).First());
            foreach (var picker in pickers)
            {
                Assert.Contains(picker.ActualWidth, new[] { SettingsWindow.WidePickerWidth, SettingsWindow.NarrowPickerWidth });
                // 試聴ボタンが右に付く音の欄だけは右端より手前
                if (picker.Name != "ReminderSoundPicker")
                    Assert.Equal(right, Right(picker), 1);
                // 選んでいる文字が欄に収まる（切れない）。
                if (picker.SelectedItem is ComboBoxItem { Content: string text })
                {
                    var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, new Typeface(window.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                        window.FontSize, Brushes.Black, 1.0);
                    Assert.True(formatted.Width <= picker.ActualWidth - 40, $"{text} is cut off");
                }
            }
            foreach (var check in Descendants<CheckBox>(window))
                Assert.Equal(right, Right(check), 1);

            foreach (var key in new[]
                     {
                         "SettingsTitleBar", "SettingsTheme", "SettingsStartup", "SettingsTaskbar",
                         "SettingsTrayClick", "SettingsFolding", "SettingsBackup",
                         "SettingsStartHidden", "SettingsClipboardNoteHotkey", "SettingsReminderSound",
                         "SettingsThemeLight", "SettingsThemeDark", "SettingsThemeSystem",
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
            Assert.Equal(settings.Theme == "Dark" ? Color.FromRgb(32, 32, 32) : Color.FromRgb(0xF3, 0xF3, 0xF3),
                ((SolidColorBrush)window.Background).Color);
        }
        finally
        {
            window.Close();
            app.Settings.Language = previousLanguage;
        }
    }

    [WpfTheory]
    [InlineData(0.0, AppSettings.NoteBorderNone, true)]
    [InlineData(12.0, AppSettings.NoteBorderNoteColor, false)]
    public void NoteFrameEditors_ReflectTheCurrentSettings(double radius, string border, bool monochrome)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var previousLanguage = app.Settings.Language;
        app.Settings.Language = "ja";
        var settings = new AppSettings { Language = "ja", NoteBorderColor = border, MonochromeIcons = monochrome };
        settings.Layout.NoteCornerRadius = radius;
        var window = new SettingsWindow(settings, app);
        try
        {
            window.Show();
            ExpandCategories(window);
            window.UpdateLayout();

            // 0 の次が 2 なのは角の丸みの欄だけ
            // （帯の太さは 1 から、端からの距離は 0 と 1 の両方を持つ）。
            var corner = Descendants<ComboBox>(window).Single(combo =>
                HasTag(combo, 0.0) && !HasTag(combo, 1.0));
            Assert.Equal(radius, Assert.IsType<ComboBoxItem>(corner.SelectedItem).Tag);

            var borderPicker = Descendants<ComboBox>(window).Single(combo =>
                combo.Items.Cast<object>().Any(item =>
                    item is ComboBoxItem { Tag: AppSettings.NoteBorderNoteColor }));
            Assert.Equal(border, Assert.IsType<ComboBoxItem>(borderPicker.SelectedItem).Tag);

            var icons = Descendants<CheckBox>(window).Single(box =>
                box.Content is TextBlock { Text: "モノクロで表示" });
            Assert.Equal(monochrome, icons.IsChecked);
        }
        finally
        {
            window.Close();
            app.Settings.Language = previousLanguage;
        }
    }

    // settings.json に直接書いた色は、選択済みの項目として残す。
    [WpfFact]
    public void NoteBorderEditor_KeepsACustomColourFromSettingsJson()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var settings = new AppSettings { NoteBorderColor = "#FF3366" };
        var window = new SettingsWindow(settings, app);
        try
        {
            window.Show();
            ExpandCategories(window);
            window.UpdateLayout();
            var picker = Descendants<ComboBox>(window).Single(combo =>
                combo.Items.Cast<object>().Any(item =>
                    item is ComboBoxItem { Tag: AppSettings.NoteBorderNoteColor }));

            var selected = Assert.IsType<ComboBoxItem>(picker.SelectedItem);
            Assert.Equal("#FF3366", selected.Tag);
            Assert.Equal("#FF3366", selected.Content);
        }
        finally { window.Close(); }
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
            ExpandCategories(window);
            window.UpdateLayout();

            var toggle = Descendants<CheckBox>(window).Single(box =>
                box.Content is TextBlock { Text: "左側に帯を出す" });
            Assert.Equal(showSpine, toggle.IsChecked);

            // 1px から始まるのは帯の太さの欄だけ。端からの距離は 0 も選べ、
            // 本文の文字サイズは 8 から始まる。
            var picker = Descendants<ComboBox>(window).Single(combo =>
                HasTag(combo, 1.0) && !HasTag(combo, 0.0));
            Assert.Equal(width, Assert.IsType<ComboBoxItem>(picker.SelectedItem).Tag);
            Assert.Equal(showSpine, picker.IsEnabled);
        }
        finally
        {
            window.Close();
            app.Settings.Language = previousLanguage;
        }
    }

    // 帯の位置の欄。端に付ける従来の置き方を選ぶと、端からの距離は効かなくなる。
    // ここでも書き込み側は走らせない（Save() が本物のデータフォルダへ書き出すため）。
    [WpfTheory]
    [InlineData(AppSettings.SpineStyleInset, true)]
    [InlineData(AppSettings.SpineStyleEdge, false)]
    public void TitleBarSpinePlacementEditor_ReflectsTheCurrentSettings(string style, bool insetEnabled)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var previousLanguage = app.Settings.Language;
        app.Settings.Language = "ja";
        var settings = new AppSettings { Language = "ja", TitleBarHiddenSpineStyle = style };
        settings.Layout.TitleBarHiddenSpineInset = 4;
        var window = new SettingsWindow(settings, app);
        try
        {
            window.Show();
            ExpandCategories(window);
            window.UpdateLayout();

            var placement = Descendants<ComboBox>(window).Single(combo =>
                combo.Items.Cast<object>().Any(item =>
                    item is ComboBoxItem entry && Equals(entry.Tag, AppSettings.SpineStyleEdge)));
            Assert.Equal(style, Assert.IsType<ComboBoxItem>(placement.SelectedItem).Tag);
            Assert.True(placement.IsEnabled);

            // 0 と 1 の両方を選べるのは端からの距離の欄だけ
            // （帯の太さは 1 から、角の丸みは 0 の次が 2）。
            var inset = Descendants<ComboBox>(window).Single(combo =>
                HasTag(combo, 0.0) && HasTag(combo, 1.0));
            Assert.Equal(4.0, Assert.IsType<ComboBoxItem>(inset.SelectedItem).Tag);
            Assert.Equal(insetEnabled, inset.IsEnabled);
        }
        finally
        {
            window.Close();
            app.Settings.Language = previousLanguage;
        }
    }

    // 画像だけの付箋での帯の扱い。帯そのものを出さない設定なら触れないようにする。
    [WpfTheory]
    [InlineData(AppSettings.ImageSpineGutter, true)]
    [InlineData(AppSettings.ImageSpineOverlay, true)]
    [InlineData(AppSettings.ImageSpineHidden, false)]
    public void TitleBarSpineOnImageEditor_ReflectsTheCurrentSettings(string style, bool showSpine)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var previousLanguage = app.Settings.Language;
        app.Settings.Language = "ja";
        var settings = new AppSettings
        {
            Language = "ja",
            ImageOnlySpineStyle = style,
            ShowTitleBarHiddenSpine = showSpine,
        };
        var window = new SettingsWindow(settings, app);
        try
        {
            window.Show();
            ExpandCategories(window);
            window.UpdateLayout();

            var picker = Descendants<ComboBox>(window).Single(combo =>
                combo.Items.Cast<object>().Any(item =>
                    item is ComboBoxItem entry && Equals(entry.Tag, AppSettings.ImageSpineOverlay)));
            Assert.Equal(style, Assert.IsType<ComboBoxItem>(picker.SelectedItem).Tag);
            Assert.Equal(showSpine, picker.IsEnabled);
        }
        finally
        {
            window.Close();
            app.Settings.Language = previousLanguage;
        }
    }

    private static bool HasTag(ComboBox combo, double tag) =>
        combo.Items.Cast<object>().Any(item => item is ComboBoxItem entry && Equals(entry.Tag, tag));

    // 書体の一覧は一度読むと使い回す。2回目に開いた設定でも、今の書体1つだけにならない。
    [WpfFact]
    public void DefaultFontPicker_ListsInstalledFontsEveryTimeItOpens()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        for (var round = 0; round < 2; round++)
        {
            var window = new SettingsWindow(new AppSettings(), app);
            try
            {
                window.Show();
                ExpandCategories(window);
                var picker = Descendants<ComboBox>(window).Single(c => c.Name == "DefaultFontPicker");
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (picker.Items.Count < 2 && DateTime.UtcNow < deadline)
                {
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background, () => frame.Continue = false);
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                    Thread.Sleep(20);
                }
                Assert.True(picker.Items.Count > 1, $"round {round}: {picker.Items.Count} fonts");
                Assert.NotNull(picker.SelectedItem);
            }
            finally { window.Close(); }
        }
    }

    private static void ExpandCategories(Window window)
    {
        window.UpdateLayout();
        foreach (var category in Descendants<Expander>(window)) category.IsExpanded = true;
        window.UpdateLayout();
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
