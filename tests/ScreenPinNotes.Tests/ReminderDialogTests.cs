using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

public class ReminderDialogTests
{
    [WpfTheory]
    [InlineData("Light", 2)]
    [InlineData("Dark", 3)]
    public void RecurrenceControls_ShowOnlyRelevantOptions(string theme, int mode)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var original = app.Settings.Theme;
        app.Settings.Theme = theme;
        var dialog = new ReminderDialog(DateTime.Now.AddDays(1));
        try
        {
            T Field<T>(string name) => (T)typeof(ReminderDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            Field<ComboBox>("_repeat").SelectedIndex = mode;
            dialog.Show();
            dialog.UpdateLayout();
            Assert.Equal(mode == 2 ? Visibility.Visible : Visibility.Collapsed, Field<WrapPanel>("_days").Visibility);
            Assert.Equal(mode == 3 ? Visibility.Visible : Visibility.Collapsed, Field<ComboBox>("_monthDay").Visibility);
            Field<CheckBox>("_windows").IsChecked = false;
            Field<CheckBox>("_alert").IsChecked = false;
            typeof(ReminderDialog).GetMethod("Accept", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null);
            Assert.False(string.IsNullOrEmpty(Field<TextBlock>("_errorText").Text));
            Assert.False(dialog.Result.Accepted);
            if (Environment.GetEnvironmentVariable("SCREENPINNOTES_REMINDER_PREVIEW") is { Length: > 0 } folder)
            {
                System.IO.Directory.CreateDirectory(folder);
                var root = (FrameworkElement)dialog.Content;
                dialog.UpdateLayout();
                root.Measure(new Size(460, double.PositiveInfinity));
                root.Arrange(new Rect(root.DesiredSize));
                root.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var stream = System.IO.File.Create(System.IO.Path.Combine(folder, theme + ".png"));
                encoder.Save(stream);
            }
        }
        finally { dialog.Close(); app.Settings.Theme = original; }
    }
}
