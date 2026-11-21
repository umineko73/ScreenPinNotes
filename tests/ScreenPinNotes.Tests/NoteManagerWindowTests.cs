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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

public class NoteManagerWindowTests
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [WpfFact]
    public void LayerOrderMatchesWindowsWhileTitleSortChangesOnlyList()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var field = typeof(App).GetField("_windows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var windows = (List<StickyNoteWindow>)field.GetValue(app)!;
        var previous = windows.ToList();
        var tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString());
        var storage = new ScreenPinNotes.Services.StorageService(tempRoot);
        var front = new StickyNoteWindow(new ScreenPinNotes.ViewModels.StickyNoteViewModel(new ScreenPinNotes.Models.StickyNote { Title = "Z", LayerOrder = 0 }, app.Settings), storage);
        var back = new StickyNoteWindow(new ScreenPinNotes.ViewModels.StickyNoteViewModel(new ScreenPinNotes.Models.StickyNote { Title = "A", LayerOrder = 1 }, app.Settings), storage);
        NoteManagerWindow? manager = null;
        try
        {
            windows.Clear();
            windows.AddRange([front, back]);
            front.Show();
            back.Show();
            // Show() でも活性化は起きる。起動時と同じく、まだ何も触っていない
            // 状態にしてから重なり順を確かめる。
            app.ForgetLastActiveNote();
            manager = new NoteManagerWindow();
            manager.Show();
            manager.UpdateLayout();
            app.ApplyLayerOrder();
            var frontHandle = new System.Windows.Interop.WindowInteropHelper(front).Handle;
            var backHandle = new System.Windows.Interop.WindowInteropHelper(back).Handle;
            bool FrontIsAboveBack()
            {
                for (var h = GetWindow(backHandle, 3); h != IntPtr.Zero; h = GetWindow(h, 3))
                    if (h == frontHandle) return true;
                return false;
            }
            Assert.True(FrontIsAboveBack());
            var root = (FrameworkElement)manager.Content;
            var list = Descendants(root).OfType<ListView>().Single();
            string FirstTitle() => (string)list.Items[0].GetType().GetProperty("Title")!.GetValue(list.Items[0])!;
            Assert.Equal("Z", FirstTitle());
            var headers = ((GridView)list.View).Columns.Select(c => (GridViewColumnHeader)c.Header).ToList();
            var titleHeader = headers.Single(h => (string)h.Tag == "Title");
            list.SelectedItem = list.Items[0];
            var selected = list.SelectedItem;
            titleHeader.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal("A", FirstTitle());
            Assert.Same(selected, list.SelectedItem);
            Assert.EndsWith("▲", titleHeader.Content.ToString());
            Assert.True(FrontIsAboveBack());
            titleHeader.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal("Z", FirstTitle());
            Assert.EndsWith("▼", titleHeader.Content.ToString());
            headers.Single(h => (string)h.Tag == "Layer").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal("Z", FirstTitle());
            Assert.DoesNotContain("▼", titleHeader.Content.ToString());
        }
        finally
        {
            manager?.Close();
            windows.Clear();
            front.Close();
            back.Close();
            windows.AddRange(previous);
            if (System.IO.Directory.Exists(tempRoot)) System.IO.Directory.Delete(tempRoot, true);
        }
    }

    [WpfTheory]
    [InlineData(920)]
    [InlineData(720)]
    public void ButtonsFitWindow(double width)
    {
        WpfApplicationFixture.Ensure();
        var window = new NoteManagerWindow { Width = width };
        try
        {
            window.Show();
            window.UpdateLayout();
            var root = (FrameworkElement)window.Content;
            foreach (var button in Descendants(root).OfType<Button>())
            {
                var bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
                Assert.True(bounds.Right <= root.ActualWidth + 1);
                Assert.True(bounds.Bottom <= root.ActualHeight + 1);
            }
            Assert.Equal(SelectionMode.Extended, Descendants(root).OfType<ListView>().Single().SelectionMode);
            var search = Descendants(root).OfType<ComboBox>().Single(c => c.IsEditable);
            var editor = (TextBox)search.Template.FindName("PART_EditableTextBox", search);
            Assert.True(editor.IsVisible);
            editor.Text = "test search";
            Assert.Equal("test search", search.Text);
            search.Text = "";
        }
        finally { window.Close(); }
    }

    // 行の先頭に付箋の色とアイコン、状態はアイコンで出す。説明文の段落や「更新」ボタンは置かず、
    // 付箋が変わると一覧も自動で追いつく。
    [WpfFact]
    public void Rows_ShowColorIconAndStatusMarks_AndRefreshOnTheirOwn()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var field = typeof(App).GetField("_windows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var windows = (List<StickyNoteWindow>)field.GetValue(app)!;
        var previous = windows.ToList();
        var tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString());
        var storage = new ScreenPinNotes.Services.StorageService(tempRoot);
        var model = new ScreenPinNotes.Models.StickyNote { Title = "pinned", ColorKey = "blue", Icon = "📌", IsTopmost = true, IsReadOnly = true, IsHidden = true };
        var note = new StickyNoteWindow(new ScreenPinNotes.ViewModels.StickyNoteViewModel(model, app.Settings), storage);
        NoteManagerWindow? manager = null;
        try
        {
            windows.Clear();
            windows.Add(note);
            manager = new NoteManagerWindow();
            manager.Show();
            manager.UpdateLayout();
            var root = (FrameworkElement)manager.Content;
            var list = Descendants(root).OfType<ListView>().Single();
            var row = list.Items[0];
            object? Prop(string name) => row.GetType().GetProperty(name)!.GetValue(row);
            Assert.NotNull(Prop("Icon"));
            Assert.Equal(System.Windows.Media.Color.FromRgb(0xDB, 0xEA, 0xFE), ((System.Windows.Media.SolidColorBrush)Prop("Background")!).Color);
            var glyphs = (string)Prop("StatusGlyphs")!;
            Assert.Contains(ScreenPinNotes.Services.UiIcons.Hide, glyphs);
            Assert.Contains(ScreenPinNotes.Services.UiIcons.Pin, glyphs);
            Assert.Contains(ScreenPinNotes.Services.UiIcons.Lock, glyphs);
            Assert.DoesNotContain(Descendants(root).OfType<Button>(),
                b => Equals(b.Content, ScreenPinNotes.Services.LocalizationService.T("NoteManagerRefresh")));
            Assert.DoesNotContain(Descendants(root).OfType<TextBlock>(),
                t => t.Text == ScreenPinNotes.Services.LocalizationService.T("LayerSortHint"));

            model.Title = "renamed";
            model.UpdatedAt = DateTime.Now.AddMinutes(1);
            typeof(NoteManagerWindow).GetMethod("RefreshIfChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(manager, null);
            Assert.Equal("renamed", list.Items[0].GetType().GetProperty("Title")!.GetValue(list.Items[0]));
        }
        finally
        {
            manager?.Close();
            windows.Clear();
            note.Close();
            windows.AddRange(previous);
            if (System.IO.Directory.Exists(tempRoot)) System.IO.Directory.Delete(tempRoot, true);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
