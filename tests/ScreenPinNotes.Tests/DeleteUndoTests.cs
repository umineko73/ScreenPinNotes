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
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

/// <summary>
/// 削除した付箋はすぐには消さず、ごみ箱（保存ルートの trash）に移し、
/// 「元に戻す」で位置・中身・画像ごと戻せる。
/// </summary>
public class DeleteUndoTests
{
    [Fact]
    public void DeleteNote_MovesTheFolderToTrashAndRestoreBringsItBack()
    {
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote { Title = "keep me", Content = "body", X = 321, Y = 123 };
        storage.SaveNote(note);
        var assets = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(assets, "a.png"), "png");

        var trashed = storage.DeleteNote(note.Id);

        Assert.NotNull(trashed);
        Assert.Empty(storage.Load());
        Assert.StartsWith(Path.GetFullPath(storage.TrashRoot), trashed);
        Assert.True(File.Exists(Path.Combine(trashed!, "assets", "a.png")));

        Assert.True(storage.RestoreNote(trashed, note.Id));
        var restored = Assert.Single(storage.Load());
        Assert.Equal("keep me", restored.Title);
        Assert.Equal("body", restored.Content);
        Assert.Equal(321, restored.X);
        Assert.True(File.Exists(Path.Combine(assets, "a.png")));
        Assert.False(Directory.Exists(trashed));
    }

    [Fact]
    public void RestoreNote_RefusesPathsOutsideTheTrashAndExistingNotes()
    {
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote { Content = "x" };
        storage.SaveNote(note);
        var outside = Path.Combine(temp.Path, "elsewhere");
        Directory.CreateDirectory(outside);

        Assert.False(storage.RestoreNote(outside, "other"));
        var trashed = storage.DeleteNote(note.Id)!;
        storage.SaveNote(note);   // 同じ ID の付箋がもうある
        Assert.False(storage.RestoreNote(trashed, note.Id));
        Assert.True(Directory.Exists(trashed));
    }

    [Fact]
    public void PurgeTrash_RemovesOnlyEntriesOlderThanTheRetention()
    {
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var now = new DateTime(2026, 11, 21, 12, 0, 0);
        var old = new StickyNote { Content = "old" };
        var recent = new StickyNote { Content = "recent" };
        storage.SaveNote(old);
        storage.SaveNote(recent);
        var oldPath = storage.DeleteNote(old.Id, now - StorageService.TrashRetention - TimeSpan.FromHours(1))!;
        var recentPath = storage.DeleteNote(recent.Id, now - TimeSpan.FromDays(1))!;

        storage.PurgeTrash(now);

        Assert.False(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(recentPath));
    }

    // 付箋を消すと画面の下に「元に戻す」が出て、押すと同じ付箋が開き直る。
    [WpfFact]
    public void RemovingANote_OffersUndoThatReopensIt()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        using var temp = new TempDataDirectory();
        var windowsField = typeof(App).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var storageField = typeof(App).GetField("_storage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var windows = (List<StickyNoteWindow>)windowsField.GetValue(app)!;
        var previous = windows.ToList();
        var previousStorage = storageField.GetValue(app);
        var storage = new StorageService(temp.Path);
        var note = new StickyNote { Title = "undo me", Content = "body" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, app.Settings), storage);
        try
        {
            windows.Clear();
            windows.Add(window);
            storageField.SetValue(app, storage);
            window.Show();
            storage.SaveNote(note);

            Assert.True(app.RemoveNoteFromManager(note.Id));
            Assert.Empty(windows);
            Assert.Empty(storage.Load());

            var toast = Assert.IsType<UndoToast>(UndoToast.Current);
            Assert.Contains("undo me", Descendants<TextBlock>(toast).First().Text);
            toast.ActionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var reopened = Assert.Single(windows);
            Assert.Equal(note.Id, reopened.ViewModel.Model.Id);
            Assert.Equal("body", Assert.Single(storage.Load()).Content);
            Assert.Null(UndoToast.Current);
        }
        finally
        {
            foreach (var w in windows.ToList()) w.Close();
            UndoToast.Current?.Close();
            windows.Clear();
            windows.AddRange(previous);
            storageField.SetValue(app, previousStorage);
        }
    }

    private sealed class TempDataDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
