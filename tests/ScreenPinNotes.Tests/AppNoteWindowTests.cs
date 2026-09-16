// ScreenPinNotes - a desktop sticky notes app for Windows 11
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
    [WpfFact]
    public void ContinuousEditing_AutosavesAndToolbarUndoRedoTargetsTheFocusedEditor()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var tempRoot = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        var windowsField = typeof(App).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var storageField = typeof(App).GetField("_storage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var windows = (List<StickyNoteWindow>)windowsField.GetValue(app)!;
        var previous = windows.ToList();
        var previousStorage = storageField.GetValue(app);
        var previousDelay = app.Settings.Timings.SaveDebounceMs;
        var storage = new StorageService(tempRoot);
        var note = new StickyNote { Content = "original", Title = "title" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, app.Settings), storage);
        try
        {
            windows.Clear();
            windows.Add(window);
            storageField.SetValue(app, storage);
            app.Settings.Timings.SaveDebounceMs = 800;
            window.Show();
            window.Activate();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            typeof(StickyNoteWindow).GetMethod("EnterEditMode", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            window.FlushPendingSave();
            var body = (System.Windows.Controls.TextBox)window.FindName("BodyEditBox");
            var title = (System.Windows.Controls.TextBox)window.FindName("TitleEditBox");
            var undo = (System.Windows.Controls.Button)window.FindName("UndoButton");
            var redo = (System.Windows.Controls.Button)window.FindName("RedoButton");
            var until = Environment.TickCount64 + 5500;
            while (Environment.TickCount64 < until)
            {
                body.Select(body.Text.Length, 0);
                body.SelectedText = "x";
                Thread.Sleep(100);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            Assert.StartsWith("originalx", Assert.Single(storage.Load()).Content);
            Assert.Equal(System.Windows.Visibility.Visible, body.Visibility);
            foreach (var editor in new[] { body, title })
            {
                editor.Focus();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.Same(editor, undo.CommandTarget);
                Assert.Same(editor, redo.CommandTarget);
                editor.IsUndoEnabled = false;
                editor.IsUndoEnabled = true;
                var before = editor.Text;
                editor.Select(editor.Text.Length, 0);
                editor.SelectedText = " added";
                window.FlushPendingSave();
                Assert.True(System.Windows.Input.ApplicationCommands.Undo.CanExecute(null, undo.CommandTarget));
                System.Windows.Input.ApplicationCommands.Undo.Execute(null, undo.CommandTarget);
                Assert.Equal(before, editor.Text);
                Assert.True(System.Windows.Input.ApplicationCommands.Redo.CanExecute(null, redo.CommandTarget));
                System.Windows.Input.ApplicationCommands.Redo.Execute(null, redo.CommandTarget);
                Assert.Equal(before + " added", editor.Text);
            }
            Thread.Sleep(1100);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var saved = Assert.Single(storage.Load());
            Assert.Equal(body.Text, saved.Content);
            Assert.Equal(title.Text, saved.Title);
        }
        finally
        {
            windows.Clear();
            window.Close();
            windows.AddRange(previous);
            storageField.SetValue(app, previousStorage);
            app.Settings.Timings.SaveDebounceMs = previousDelay;
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [WpfFact]
    public void DuplicateNote_OpensASavedCopyWithItsOwnImages()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var tempRoot = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        var windowsField = typeof(App).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var storageField = typeof(App).GetField("_storage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var windows = (List<StickyNoteWindow>)windowsField.GetValue(app)!;
        var previous = windows.ToList();
        var previousStorage = storageField.GetValue(app);
        var storage = new StorageService(tempRoot);
        var note = new StickyNote
        {
            Content = "body\n![](assets/sub/pic.png)", Title = "title", X = 300, Y = 200,
            Reminder = new ReminderSettings { NextAt = DateTime.Now.AddDays(1) },
        };
        var assets = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(Path.Combine(assets, "sub"));
        File.WriteAllText(Path.Combine(assets, "sub", "pic.png"), "png");
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, app.Settings), storage);
        StickyNoteWindow? duplicate = null;
        try
        {
            windows.Clear();
            windows.Add(window);
            storageField.SetValue(app, storage);
            window.Show();

            duplicate = app.DuplicateNote(window);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            var copy = duplicate.ViewModel.Model;
            Assert.Equal(2, windows.Count);
            Assert.True(duplicate.IsVisible);
            Assert.NotEqual(note.Id, copy.Id);
            Assert.Equal(("body\n![](assets/sub/pic.png)", "title"), (copy.Content, copy.Title));
            Assert.True(copy.X > note.X && copy.Y > note.Y);
            Assert.True(copy.LayerOrder < note.LayerOrder);
            Assert.Null(copy.Reminder);
            var copiedImage = Path.Combine(storage.GetNoteAssetsDirectoryPath(copy.Id), "sub", "pic.png");
            Assert.Equal("png", File.ReadAllText(copiedImage));
            // 写した画像は複製のもの。元の画像はそのまま残る。
            Assert.True(File.Exists(Path.Combine(assets, "sub", "pic.png")));
            var saved = storage.Load();
            Assert.Equal(2, saved.Count);
            Assert.Contains(saved, n => n.Id == copy.Id && n.Content == copy.Content);
            Assert.NotNull(saved.Single(n => n.Id == note.Id).Reminder);
        }
        finally
        {
            windows.Clear();
            duplicate?.Close();
            window.Close();
            windows.AddRange(previous);
            storageField.SetValue(app, previousStorage);
            // 表示した画像のファイルは、閉じた直後もまだ掴まれていることがある。
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); }
            catch (IOException) { }
        }
    }

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
