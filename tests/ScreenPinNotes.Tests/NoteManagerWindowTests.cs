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
