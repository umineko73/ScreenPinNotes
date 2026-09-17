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

using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

/// <summary>
/// クリックした付箋が前に残るかどうか。重なり順は設定として持っているので、
/// 並べ直しはクリックのたびに走る。そこで今触っている付箋まで奥へ送り返すと、
/// 一瞬表に出てすぐ裏へ戻ってしまう。
/// </summary>
public class NoteLayerRaiseTests
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    private const uint GwHwndPrev = 3;

    // テストのプロセスは前面に来られないので Activate() は効かない。
    // 付箋側が活性化やクリックを受け取ったときと同じ経路を直接たどる。
    private static void Activate(StickyNoteWindow window)
        => typeof(StickyNoteWindow)
            .GetMethod("Window_Activated",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(window, new object?[] { window, EventArgs.Empty });

    private static void Click(StickyNoteWindow window)
        => typeof(StickyNoteWindow)
            .GetMethod("Window_PreviewMouseDown",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(window, new object?[]
            {
                window,
                new System.Windows.Input.MouseButtonEventArgs(
                    System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left),
            });

    [WpfTheory]
    [InlineData(LayerMove.Top, false)]
    [InlineData(LayerMove.Up, false)]
    [InlineData(LayerMove.Down, false)]
    [InlineData(LayerMove.Bottom, false)]
    [InlineData(LayerMove.Top, true)]
    [InlineData(LayerMove.Up, true)]
    [InlineData(LayerMove.Down, true)]
    [InlineData(LayerMove.Bottom, true)]
    public void ExplicitLayerMoveOverridesClickAndPersists(LayerMove move, bool pinned)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var field = typeof(App).GetField("_windows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var windows = (List<StickyNoteWindow>)field.GetValue(app)!;
        var previous = windows.ToList();
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        var storage = new StorageService(root);
        var notes = Enumerable.Range(0, 3).Select(i => new StickyNoteWindow(
            new StickyNoteViewModel(new StickyNote { LayerOrder = i, IsTopmost = pinned }, app.Settings), storage)).ToArray();
        try
        {
            windows.Clear();
            windows.AddRange(notes);
            foreach (var window in notes) window.Show();
            Click(notes[0]);
            app.ApplyLayerOrder();
            var selected = move is LayerMove.Top or LayerMove.Up ? notes[2] : notes[0];
            app.MoveNoteLayers(new HashSet<string> { selected.ViewModel.Model.Id }, move);
            // 再適用でもクリックの一時的な前面化が戻らない。
            app.ApplyLayerOrder();
            var ordered = NoteLayers.Ordered(notes.Select(w => w.ViewModel.Model));
            for (var i = 0; i < ordered.Count - 1; i++)
            {
                var upper = notes.Single(w => w.ViewModel.Model.Id == ordered[i].Id);
                var lower = notes.Single(w => w.ViewModel.Model.Id == ordered[i + 1].Id);
                var upperHandle = new System.Windows.Interop.WindowInteropHelper(upper).Handle;
                var lowerHandle = new System.Windows.Interop.WindowInteropHelper(lower).Handle;
                var found = false;
                for (var h = GetWindow(lowerHandle, GwHwndPrev); h != IntPtr.Zero; h = GetWindow(h, GwHwndPrev))
                    if (h == upperHandle) { found = true; break; }
                Assert.True(found, "Native window order must match the explicit layer order.");
            }
            Assert.Equal(ordered.Select(n => n.Id), NoteLayers.Ordered(storage.Load()).Select(n => n.Id));
        }
        finally
        {
            windows.Clear();
            foreach (var window in notes) window.Close();
            windows.AddRange(previous);
            app.ForgetLastActiveNote();
            if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
        }
    }

    // 付箋を足したときなどの並べ直しで、他のアプリの後ろにある付箋まで前へ出さない。
    // トレイのメニューから操作するとアプリが前面になるので、一番上へ置き直す並べ方だと
    // 全付箋が他のアプリを越えてしまっていた。「すべて表示」では前へ出す。
    [WpfFact]
    public void AutomaticReorderKeepsNotesBehindOtherWindowsButShowAllRaisesThem()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var field = typeof(App).GetField("_windows",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var windows = (List<StickyNoteWindow>)field.GetValue(app)!;
        var previous = windows.ToList();
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        var storage = new StorageService(root);
        var front = new StickyNoteWindow(new StickyNoteViewModel(new StickyNote { LayerOrder = 0 }, app.Settings), storage);
        var back = new StickyNoteWindow(new StickyNoteViewModel(new StickyNote { LayerOrder = 1 }, app.Settings), storage);
        // 他のアプリの窓の代わり。付箋ではないので並べ直しの対象にならない。
        var other = new System.Windows.Window { Width = 200, Height = 200, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            windows.Clear();
            windows.AddRange([front, back]);
            back.Show();
            front.Show();
            other.Show();
            app.ForgetLastActiveNote();
            var frontHandle = new System.Windows.Interop.WindowInteropHelper(front).Handle;
            var backHandle = new System.Windows.Interop.WindowInteropHelper(back).Handle;
            var otherHandle = new System.Windows.Interop.WindowInteropHelper(other).Handle;
            bool IsAbove(IntPtr upper, IntPtr lower)
            {
                for (var h = GetWindow(lower, GwHwndPrev); h != IntPtr.Zero; h = GetWindow(h, GwHwndPrev))
                    if (h == upper) return true;
                return false;
            }

            // 付箋を他の窓の後ろへ送り、さらに付箋の順番を崩しておく
            // （後から最背面へ送ったほうが下になるので、front が一番下）。
            back.ChangeZOrder(false);
            front.ChangeZOrder(false);
            Assert.True(IsAbove(otherHandle, frontHandle) && IsAbove(otherHandle, backHandle), "setup");
            Assert.True(IsAbove(backHandle, frontHandle), "setup: notes out of order");

            app.ApplyLayerOrder();
            Assert.True(IsAbove(frontHandle, backHandle), "notes are reordered among themselves");
            Assert.True(IsAbove(otherHandle, frontHandle), "the other window stays in front");
            Assert.True(IsAbove(otherHandle, backHandle), "the other window stays in front of the back note");

            app.ShowAllNotes();
            Assert.True(IsAbove(frontHandle, otherHandle), "show all brings notes forward");
            Assert.True(IsAbove(backHandle, otherHandle), "show all brings every note forward");
            Assert.True(IsAbove(frontHandle, backHandle), "show all keeps the layer order");
        }
        finally
        {
            windows.Clear();
            other.Close();
            front.Close();
            back.Close();
            windows.AddRange(previous);
            app.ForgetLastActiveNote();
            if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
        }
    }

    [WpfFact]
    public void ClickedNoteStaysInFrontUntilAnotherNoteIsClicked()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var field = typeof(App).GetField("_windows",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var windows = (List<StickyNoteWindow>)field.GetValue(app)!;
        var previous = windows.ToList();
        var tempRoot = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString());
        var storage = new StorageService(tempRoot);
        var front = new StickyNoteWindow(
            new StickyNoteViewModel(new StickyNote { Title = "front", LayerOrder = 0 }, app.Settings), storage);
        var back = new StickyNoteWindow(
            new StickyNoteViewModel(new StickyNote { Title = "back", LayerOrder = 1 }, app.Settings), storage);
        try
        {
            windows.Clear();
            windows.AddRange([front, back]);
            front.Show();
            back.Show();
            // Show() でも活性化は起きるので、「まだ何も触っていない」状態に戻す。
            app.ForgetLastActiveNote();

            var frontHandle = new System.Windows.Interop.WindowInteropHelper(front).Handle;
            var backHandle = new System.Windows.Interop.WindowInteropHelper(back).Handle;
            bool IsAbove(IntPtr upper, IntPtr lower)
            {
                for (var h = GetWindow(lower, GwHwndPrev); h != IntPtr.Zero; h = GetWindow(h, GwHwndPrev))
                    if (h == upper) return true;
                return false;
            }

            // 何も触っていなければ、設定した重なり順のまま。
            app.ApplyLayerOrder();
            Assert.True(IsAbove(frontHandle, backHandle), "initial layer order");

            // 奥の付箋を触ると前に出て、そのまま残る。
            Activate(back);
            app.ApplyLayerOrder();
            Assert.True(IsAbove(backHandle, frontHandle), "clicked note raised");

            // 触っていない間に別の用事で並べ直しが走っても、奥へ戻されない。
            app.ApplyLayerOrder();
            Assert.True(IsAbove(backHandle, frontHandle), "stays raised on a later re-apply");

            // 別の付箋を触れば、そちらが前に出る。
            Activate(front);
            app.ApplyLayerOrder();
            Assert.True(IsAbove(frontHandle, backHandle), "another click takes over");

            // すでに入力先になっている付箋を押しても前に出る。活性化だけを
            // 見ていると、起動直後に最後に開いた付箋がこの状態で沈んだままになる。
            // （front を触った直後なので back は活性化しておらず、押すだけ）
            Click(back);
            app.ApplyLayerOrder();
            Assert.True(IsAbove(backHandle, frontHandle), "clicking an already-focused note raises it");

            // 重なり順そのものは書き換えない。設定として持っているもの。
            Assert.Equal(0, front.ViewModel.Model.LayerOrder);
            Assert.Equal(1, back.ViewModel.Model.LayerOrder);
        }
        finally
        {
            windows.Clear();
            front.Close();
            back.Close();
            windows.AddRange(previous);
            app.ForgetLastActiveNote();
        }
    }
}
