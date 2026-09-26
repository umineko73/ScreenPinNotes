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

using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ScreenPinNotes.Services;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

public class ReminderDialogTests
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModalEditorStaysAbovePinnedNoteDuringLayerRefresh(bool pinnedOwner)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var windowsField = typeof(App).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var windows = (List<StickyNoteWindow>)windowsField.GetValue(app)!;
        var previous = windows.ToList();
        var tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString());
        var note = new ScreenPinNotes.Models.StickyNote { IsTopmost = true, LayerOrder = 7 };
        var pinned = new StickyNoteWindow(new ScreenPinNotes.ViewModels.StickyNoteViewModel(note, app.Settings), new ScreenPinNotes.Services.StorageService(tempRoot));
        var owner = new Window { Width = 200, Height = 100, Topmost = pinnedOwner };
        try
        {
            windows.Clear();
            windows.Add(pinned);
            pinned.Show();
            owner.Show();
            Exception? failure = null;
            owner.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
            {
                var dialog = owner.OwnedWindows.OfType<ReminderDialog>().Single();
                try
                {
                    failure = Record.Exception(() =>
                    {
                        Assert.True(dialog.Topmost);
                        app.ApplyLayerOrder();
                        var target = new System.Windows.Interop.WindowInteropHelper(dialog).Handle;
                        var found = false;
                        for (var h = GetWindow(new System.Windows.Interop.WindowInteropHelper(pinned).Handle, 3); h != IntPtr.Zero; h = GetWindow(h, 3))
                            if (h == target) { found = true; break; }
                        Assert.True(found, "The reminder editor should remain above the pinned note.");
                    });
                }
                finally { dialog.Close(); }
            }));
            ReminderDialog.ShowFor(owner, null);
            Assert.Null(failure);
            app.ApplyLayerOrder();
            Assert.True(pinned.Topmost);
            Assert.True(note.IsTopmost);
            Assert.Equal(7, note.LayerOrder);
            Assert.Equal(pinnedOwner, owner.Topmost);
        }
        finally
        {
            owner.Close();
            windows.Clear();
            pinned.Close();
            windows.AddRange(previous);
            if (System.IO.Directory.Exists(tempRoot)) System.IO.Directory.Delete(tempRoot, true);
        }
    }

    [WpfTheory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(60)]
    public void AddTime_UsesEditedDateTimeAndAccumulatesAcrossMidnight(int minutes)
    {
        WpfApplicationFixture.Ensure();
        var dialog = new ReminderDialog(new DateTime(2030, 1, 1));
        try
        {
            var date = Field<DatePicker>(dialog, "_dateBox");
            var time = Field<TextBox>(dialog, "_timeBox");
            var start = new DateTime(2032, 12, 31, 23, 58, 0);
            date.SelectedDate = start.Date;
            time.Text = "23:58";
            for (int i = 1; i <= 2; i++)
            {
                Invoke(dialog, "AddToSelectedTime", TimeSpan.FromMinutes(minutes));
                var expected = start.AddMinutes(minutes * i);
                Assert.Equal(expected.Date, date.SelectedDate);
                Assert.Equal(expected.ToString("HH:mm"), time.Text);
            }
        }
        finally { dialog.Close(); }
    }

    [WpfFact]
    public void InvalidTimeIsPreservedAndResetRecoversToCurrentTime()
    {
        WpfApplicationFixture.Ensure();
        var dialog = new ReminderDialog(new DateTime(2030, 1, 1));
        try
        {
            var time = Field<TextBox>(dialog, "_timeBox");
            time.Text = "invalid";
            Invoke(dialog, "AddToSelectedTime", TimeSpan.FromMinutes(5));
            Assert.Equal("invalid", time.Text);
            Assert.NotEmpty(Field<TextBlock>(dialog, "_errorText").Text);
            var before = DateTime.Now;
            Invoke(dialog, "ResetToNow");
            var after = DateTime.Now;
            var actual = Field<DatePicker>(dialog, "_dateBox").SelectedDate!.Value.Add(TimeSpan.Parse(time.Text));
            Assert.InRange(actual, before.AddTicks(-(before.Ticks % TimeSpan.TicksPerMinute)), after);
            Assert.Empty(Field<TextBlock>(dialog, "_errorText").Text);
        }
        finally { dialog.Close(); }
    }

    private static T Field<T>(ReminderDialog dialog, string name) =>
        (T)typeof(ReminderDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;

    private static void Invoke(ReminderDialog dialog, string name, params object[] arguments) =>
        typeof(ReminderDialog).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, arguments);

    [WpfTheory]
    [InlineData("23:59", "23:00")]
    [InlineData("9:5", "09:00")]
    [InlineData("00:00", "00:00")]
    [InlineData("invalid", "invalid")]
    public void ZeroMinutes_PreservesDateAndHour(string input, string expected)
    {
        WpfApplicationFixture.Ensure();
        var date = DateTime.Now.AddDays(2).Date;
        var dialog = new ReminderDialog(date);
        try
        {
            var time = (TextBox)typeof(ReminderDialog).GetField("_timeBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            time.Text = input;
            typeof(ReminderDialog).GetMethod("SetZeroMinutes", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null);
            Assert.Equal(expected, time.Text);
            var picker = (DatePicker)typeof(ReminderDialog).GetField("_dateBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            Assert.Equal(date, picker.SelectedDate);
        }
        finally { dialog.Close(); }
    }

    [WpfFact]
    public void WeekChoices_EveryWeekExcludesSpecificWeeksAndResultKeepsSelection()
    {
        WpfApplicationFixture.Ensure();
        var dialog = new ReminderDialog(DateTime.Now.AddDays(1));
        RunModal(dialog, () =>
        {
            var everyWeek = Field<CheckBox>(dialog, "_everyWeek");
            var weeks = Field<WrapPanel>(dialog, "_weeks").Children.OfType<CheckBox>().Where(b => b.Tag is int).ToList();
            Assert.Equal(5, weeks.Count);
            Assert.True(everyWeek.IsChecked);

            weeks[0].IsChecked = true;
            weeks[2].IsChecked = true;
            Assert.False(everyWeek.IsChecked);
            weeks[0].IsChecked = false;
            weeks[2].IsChecked = false;
            Assert.True(everyWeek.IsChecked);
            everyWeek.IsChecked = false;
            Assert.True(everyWeek.IsChecked);

            weeks[1].IsChecked = true;
            weeks[3].IsChecked = true;
            everyWeek.IsChecked = true;
            Assert.All(weeks, b => Assert.False(b.IsChecked));

            weeks[1].IsChecked = true;
            weeks[3].IsChecked = true;
            Field<ComboBox>(dialog, "_repeat").SelectedIndex = 2;
            Invoke(dialog, "Accept");
        });
        Assert.True(dialog.Result.Accepted);
        Assert.Equal([2, 4], dialog.Result.Settings!.MonthWeeks);
    }

    [WpfFact]
    public void MonthDay_LastDayChoiceIsSavedAndRestored()
    {
        WpfApplicationFixture.Ensure();
        var dialog = new ReminderDialog(DateTime.Now.AddDays(1));
        RunModal(dialog, () =>
        {
            var monthDay = Field<ComboBox>(dialog, "_monthDay");
            Assert.Equal(32, monthDay.Items.Count);
            monthDay.SelectedIndex = 31;
            Field<ComboBox>(dialog, "_repeat").SelectedIndex = 3;
            Invoke(dialog, "Accept");
        });
        var settings = dialog.Result.Settings!;
        Assert.True(settings.MonthLastDay);
        Assert.Equal(31, settings.MonthDay);

        var ctor = typeof(ReminderDialog).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, [typeof(DateTime?), typeof(ScreenPinNotes.Models.ReminderSettings)])!;
        var reopened = (ReminderDialog)ctor.Invoke([settings.NextAt, settings]);
        try { Assert.Equal(31, Field<ComboBox>(reopened, "_monthDay").SelectedIndex); }
        finally { reopened.Close(); }
    }

    // 音だけでも通知方法として足りる。新しいリマインダーは音を鳴らす側で開く。
    [WpfFact]
    public void SoundAloneIsAValidMethodAndIsRestored()
    {
        WpfApplicationFixture.Ensure();
        var dialog = new ReminderDialog(DateTime.Now.AddDays(1));
        RunModal(dialog, () =>
        {
            Assert.True(Field<CheckBox>(dialog, "_sound").IsChecked);
            // 何秒続くかは設定画面の値を出す。
            var seconds = App.Current.Settings.ReminderAlertSeconds;
            Assert.Equal(string.Format(LocalizationService.T("ReminderPlaySound"), seconds), Field<CheckBox>(dialog, "_sound").Content);
            Assert.Equal(string.Format(LocalizationService.T("ReminderFlashNote"), seconds), Field<CheckBox>(dialog, "_flash").Content);
            Field<CheckBox>(dialog, "_windows").IsChecked = false;
            Field<CheckBox>(dialog, "_alert").IsChecked = false;
            Field<CheckBox>(dialog, "_flash").IsChecked = false;
            Invoke(dialog, "Accept");
        });
        Assert.True(dialog.Result.Accepted);
        var settings = dialog.Result.Settings!;
        Assert.Equal(true, settings.PlaySound);

        settings.PlaySound = false;
        settings.FlashNote = true;
        var ctor = typeof(ReminderDialog).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, [typeof(DateTime?), typeof(ScreenPinNotes.Models.ReminderSettings)])!;
        var reopened = (ReminderDialog)ctor.Invoke([settings.NextAt, settings]);
        try { Assert.False(Field<CheckBox>(reopened, "_sound").IsChecked); }
        finally { reopened.Close(); }
    }

    // Accept は DialogResult を設定するので、モーダル表示中に操作する。
    private static void RunModal(ReminderDialog dialog, Action action)
    {
        Exception? failure = null;
        dialog.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
        {
            failure = Record.Exception(action);
            if (dialog.IsVisible) dialog.Close();
        }));
        dialog.ShowDialog();
        Assert.Null(failure);
    }

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
            var datePicker = Field<DatePicker>("_dateBox");
            Assert.Equal(DateTime.Now.AddDays(1).Date, datePicker.SelectedDate);
            datePicker.IsDropDownOpen = true;
            dialog.UpdateLayout();
            Assert.True(datePicker.IsDropDownOpen);
            datePicker.SelectedDate = DateTime.Now.AddDays(2).Date;
            datePicker.IsDropDownOpen = false;
            Assert.Equal(mode == 2 ? Visibility.Visible : Visibility.Collapsed, Field<WrapPanel>("_days").Visibility);
            Assert.Equal(mode == 2 ? Visibility.Visible : Visibility.Collapsed, Field<WrapPanel>("_weeks").Visibility);
            Assert.Equal(mode == 3 ? Visibility.Visible : Visibility.Collapsed, Field<ComboBox>("_monthDay").Visibility);
            Field<CheckBox>("_windows").IsChecked = false;
            Field<CheckBox>("_alert").IsChecked = false;
            Field<CheckBox>("_flash").IsChecked = false;
            Field<CheckBox>("_sound").IsChecked = false;
            typeof(ReminderDialog).GetMethod("Accept", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null);
            Assert.False(string.IsNullOrEmpty(Field<TextBlock>("_errorText").Text));
            Assert.False(dialog.Result.Accepted);
            SavePreview(dialog, theme);
        }
        finally { dialog.Close(); app.Settings.Theme = original; }
    }

    // 開いた瞬間に ON / OFF が読めること。日時の欄には初期値が入るので、
    // 欄の中身だけでは設定済みかどうか分からない。
    [WpfTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void CurrentSettingBannerTellsWhetherTheReminderIsOn(bool hasReminder)
    {
        WpfApplicationFixture.Ensure();
        var language = App.Current.Settings.Language;
        var current = hasReminder
            ? new ScreenPinNotes.Models.ReminderSettings
            {
                NextAt = new DateTime(2030, 5, 1, 9, 30, 0),
                Recurrence = "None",
                WindowsNotification = true,
                ShowAlert = false,
                FlashNote = true,
            }
            : null;
        var dialog = (ReminderDialog)Activator.CreateInstance(
            typeof(ReminderDialog),
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [current?.NextAt, current],
            null)!;
        try
        {
            var texts = Texts((DependencyObject)dialog.Content).ToList();

            Assert.Contains(LocalizationService.T("ReminderStatusCurrent", language), texts);
            Assert.Contains(ReminderDialog.StatusHeading(current, language), texts);
            Assert.Contains(ReminderDialog.StatusDetail(current, language), texts);
            SavePreview(dialog, hasReminder ? "status-on" : "status-off");
        }
        finally { dialog.Close(); }
    }

    /// <summary>
    /// SCREENPINNOTES_REMINDER_PREVIEW にフォルダーを指定して実行すると、
    /// 設定画面の見た目を PNG に書き出す。配色や余白は目で見ないと分からない。
    /// </summary>
    private static void SavePreview(ReminderDialog dialog, string name)
    {
        if (Environment.GetEnvironmentVariable("SCREENPINNOTES_REMINDER_PREVIEW") is not { Length: > 0 } folder)
            return;

        System.IO.Directory.CreateDirectory(folder);
        var root = (FrameworkElement)dialog.Content;
        dialog.UpdateLayout();
        root.Measure(new Size(460, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(System.IO.Path.Combine(folder, name + ".png"));
        encoder.Save(stream);
    }

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        if (root is TextBlock text) yield return text.Text;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Texts(child))
                yield return found;
    }
}
