using System.IO;
using System.Reflection;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

// タスクバーの × や Alt+F4 は、アプリを経由せずウィンドウを直接閉じる。
// 閉じたウィンドウは Show() できないので、App の一覧に残したままだと
// 「一覧には出るが表示できない抜け殻」になり、全表示で例外になる。
public class AppNoteWindowTests
{
    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DueReminderFlashesOnlyWhenEnabled(bool flash)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var tempRoot = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        var windowsField = typeof(App).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var storageField = typeof(App).GetField("_storage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var windows = (List<StickyNoteWindow>)windowsField.GetValue(app)!;
        var previous = windows.ToList();
        var previousStorage = storageField.GetValue(app);
        var storage = new StorageService(tempRoot);
        var due = DateTime.Now.AddMinutes(-1);
        var note = new StickyNote { IsHidden = true, Reminder = new ReminderSettings { NextAt = due, FlashNote = flash, ShowAlert = false, WindowsNotification = true } };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, app.Settings), storage);
        try
        {
            windows.Clear();
            windows.Add(window);
            storageField.SetValue(app, storage);
            typeof(App).GetMethod("TriggerReminder", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [window, due]);
            Assert.Equal(flash, window.IsVisible);
            Assert.Equal(!flash, note.IsHidden);
            Assert.Equal(flash, ((System.Windows.Controls.Border)window.FindName("ReminderFlashBorder")).HasAnimatedProperties);
            Assert.Null(note.Reminder.NextAt);
            Assert.NotNull(note.Reminder.LastTriggeredAt);
            Assert.Equal(flash, Assert.Single(storage.Load()).Reminder!.FlashNote);
        }
        finally
        {
            windows.Clear();
            window.Close();
            windows.AddRange(previous);
            storageField.SetValue(app, previousStorage);
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewNoteStartsWithBodyReadyForTyping(bool fromTemplate)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var tempRoot = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        var windowsField = typeof(App).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var storageField = typeof(App).GetField("_storage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var windows = (List<StickyNoteWindow>)windowsField.GetValue(app)!;
        var previousWindows = windows.ToList();
        var previousStorage = storageField.GetValue(app);
        try
        {
            windows.Clear();
            storageField.SetValue(app, new StorageService(tempRoot));
            app.AddNewNote(fromTemplate ? new StickyNote { IsReadOnly = true, IsFolded = true, IsTitleBarHidden = true } : null);
            var window = Assert.Single(windows);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var editor = Assert.IsType<System.Windows.Controls.TextBox>(window.FindName("BodyEditBox"));
            Assert.Equal(System.Windows.Visibility.Visible, editor.Visibility);
            Assert.False(editor.IsReadOnly);
            Assert.True(editor.IsKeyboardFocused);
            Assert.Empty(editor.Text);
            editor.SelectedText = "すぐ入力";
            Assert.Equal("すぐ入力", window.ViewModel.Content);
        }
        finally
        {
            var created = windows.ToList();
            windows.Clear();
            foreach (var window in created) window.Close();
            windows.AddRange(previousWindows);
            storageField.SetValue(app, previousStorage);
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [WpfFact]
    public void ClosingANoteWindowDirectly_HidesTheNoteAndKeepsShowAllWorking()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var tempRoot = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var windowsField = typeof(App).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var storageField = typeof(App).GetField("_storage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var windows = (List<StickyNoteWindow>)windowsField.GetValue(app)!;
        var previousStorage = storageField.GetValue(app);
        var previousWindows = windows.ToList();

        var note = new StickyNote { Title = "Closed by the taskbar" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, app.Settings), new StorageService(tempRoot));
        try
        {
            storageField.SetValue(app, new StorageService(tempRoot));
            windows.Clear();
            windows.Add(window);
            window.Show();

            window.Close();

            // 付箋は消さず「非表示」にして、あとから戻せる状態にする。
            Assert.True(note.IsHidden);
            Assert.Contains(window, windows);
            Assert.False(window.IsVisible);

            // 抜け殻が残っていると、ここで InvalidOperationException になっていた。
            app.ShowAllNotes();
            Assert.False(window.IsVisible);   // 非表示にした付箋は全表示の対象外

            app.ShowNote(note.Id);
            Assert.True(window.IsVisible);
            Assert.False(note.IsHidden);
        }
        finally
        {
            windows.Remove(window);
            window.Close();
            windows.Clear();
            windows.AddRange(previousWindows);
            storageField.SetValue(app, previousStorage);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    // 削除や保存先の切り替えでは、アプリ側が先に一覧から外してから閉じる。
    // その経路まで「非表示」に読み替えてしまうと、付箋が閉じられなくなる。
    [WpfFact]
    public void ClosingANoteWindowTheAppAlreadyDropped_ActuallyCloses()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var tempRoot = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var note = new StickyNote();
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, app.Settings), new StorageService(tempRoot));
        try
        {
            window.Show();

            window.Close();   // App の一覧に入っていないので、そのまま閉じる

            Assert.False(note.IsHidden);
            Assert.Throws<InvalidOperationException>(() => window.Show());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
