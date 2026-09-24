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
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

/// <summary>
/// 開いた位置と畳んだ位置を分けた付箋を開いているあいだ、畳んだときの置き場所に出す影。
/// </summary>
public class FoldedPositionGhostTests
{
    [WpfFact]
    public void GhostAppearsWhereASeparatedNoteFoldsTo()
    {
        using var scope = new NoteScope(separated: true);
        var ghost = scope.GhostLocation();
        Assert.NotNull(ghost);
        Assert.Equal(Math.Round(420 * scope.Scale), ghost.Value.X);
        Assert.Equal(Math.Round(360 * scope.Scale), ghost.Value.Y);
    }

    [WpfFact]
    public void LinkedNotesHaveNoGhost()
    {
        using var scope = new NoteScope(separated: false);
        Assert.Null(scope.GhostLocation());
    }

    [WpfFact]
    public void GhostGoesAwayOnceTheNoteHasFolded()
    {
        using var scope = new NoteScope(separated: true);
        Assert.NotNull(scope.GhostLocation());

        scope.Call("ToggleFold", (object?)null);
        scope.Call("CompleteFoldAnimation");

        Assert.Null(scope.GhostLocation());
        // 開き直せばまた出る。
        scope.Call("ToggleFold", (object?)null);
        scope.Call("CompleteFoldAnimation");
        Assert.NotNull(scope.GhostLocation());
    }

    [WpfFact]
    public void GhostFollowsTheNoteBeingHiddenAndTheSetting()
    {
        using var scope = new NoteScope(separated: true);
        Assert.NotNull(scope.GhostLocation());

        scope.Window.Hide();
        Assert.Null(scope.GhostLocation());
        scope.Window.Show();
        Assert.NotNull(scope.GhostLocation());

        App.Current.Settings.ShowFoldedPositionGhost = false;
        scope.Window.RefreshSettings();
        Assert.Null(scope.GhostLocation());
    }

    [WpfFact]
    public void DraggingTheGhostMovesOnlyTheFoldedPosition()
    {
        using var scope = new NoteScope(separated: true);
        var x = (int)Math.Round(500 * scope.Scale);
        var y = (int)Math.Round(380 * scope.Scale);

        scope.Window.CommitFoldedGhostPosition(x, y);

        Assert.Equal(500, scope.Note.FoldedX!.Value, 3);
        Assert.Equal(380, scope.Note.FoldedY!.Value, 3);
        // 開いた位置は窓から読み戻して記録し直すので、物理ピクセルに丸まった値になる。
        DevicePixelAssert.Near((140, 130), (scope.Note.X, scope.Note.Y), scope.Window);
        Assert.True(scope.Note.IsPositionSeparated);
        var ghost = scope.GhostLocation();
        Assert.Equal((x, y), (ghost!.Value.X, ghost.Value.Y));
    }

    [WpfFact]
    public void ClickingTheGhostFoldsTheNoteThere()
    {
        using var scope = new NoteScope(separated: true);

        scope.Window.FoldFromGhost();
        scope.Call("CompleteFoldAnimation");

        Assert.True(scope.Note.IsFolded);
        Assert.Equal((420d, 360d), (scope.Note.FoldedX!.Value, scope.Note.FoldedY!.Value));
        Assert.Null(scope.GhostLocation());
    }

    [WpfFact]
    public void AligningFromTheGhostRemovesIt()
    {
        using var scope = new NoteScope(separated: true);

        scope.Window.ResetPositionSeparationFromGhost();

        Assert.False(scope.Note.IsPositionSeparated);
        Assert.Null(scope.GhostLocation());
    }

    [WpfFact]
    public void GhostStaysOutOfTheTaskbarAndAltTabWithoutTakingFocus()
    {
        using var scope = new NoteScope(separated: true);
        var foreground = GetForegroundWindow();
        Assert.NotNull(scope.GhostLocation());
        var ghost = (Window)typeof(StickyNoteWindow)
            .GetField("_foldedGhost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scope.Window)!;
        var hwnd = new WindowInteropHelper(ghost).Handle;

        // ShowInTaskbar=false の WPF ウィンドウは見えない親の下に作られ、Alt+Tab の一覧に出ない。
        var owner = GetWindow(hwnd, GW_OWNER);
        Assert.NotEqual(IntPtr.Zero, owner);
        Assert.False(IsWindowVisible(owner));
        Assert.Equal(0, GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_APPWINDOW);
        // 出しただけで前面を奪わない。
        Assert.Equal(foreground, GetForegroundWindow());
        Assert.NotEqual(hwnd, GetForegroundWindow());
    }

    private const int GW_OWNER = 4;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_APPWINDOW = 0x00040000;

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [Theory]
    [InlineData("en", "SettingsShowFoldedPositionGhost", "Show where a note with separate positions collapses to while it is expanded")]
    [InlineData("ja", "SettingsShowFoldedPositionGhost", "位置を分けた付箋を開いているとき、折りたたみ時の位置に影を表示")]
    [InlineData("en", "FoldedGhostTooltip", "The note returns here when collapsed\nClick: collapse it here\nDrag: move the collapsed position")]
    [InlineData("ja", "FoldedGhostTooltip", "折りたたむとこの位置に戻ります\nクリック: ここに折りたたむ\nドラッグ: 折りたたみ時の位置を動かす")]
    public void GhostTextIsLocalized(string culture, string key, string expected)
        => Assert.Equal(expected, LocalizationService.T(key, culture));

    [Fact]
    public void FoldedPhysicalPositionIsStoredAgainstThePositionScale()
    {
        var note = new StickyNote { X = 10, Y = 20, FoldedX = 10, FoldedY = 20,
            IsPositionSeparated = true, PositionLayout = "home", PositionScale = 1.5 };
        var state = new NoteGeometryState(note, () => new(true, "home", 1.5));

        state.StoreFoldedPhysicalPosition(600, 450);

        Assert.Equal((400d, 300d), (note.FoldedX!.Value, note.FoldedY!.Value));
        Assert.Equal((10d, 20d), (note.X, note.Y));

        // 別の構成のあいだは書き戻さない（一時的に寄せている位置かもしれない）。
        var elsewhere = new NoteGeometryState(note, () => new(false, "other", 1));
        elsewhere.StoreFoldedPhysicalPosition(0, 0);
        Assert.Equal((400d, 300d), (note.FoldedX!.Value, note.FoldedY!.Value));
    }

    private sealed class NoteScope : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        private readonly bool _previousSetting;
        private readonly bool _previousAnimation;

        public StickyNote Note { get; }
        public StickyNoteWindow Window { get; }
        public double Scale { get; }

        public NoteScope(bool separated)
        {
            WpfApplicationFixture.Ensure();
            var settings = App.Current.Settings;
            _previousSetting = settings.ShowFoldedPositionGhost;
            _previousAnimation = settings.EnableFoldAnimation;
            settings.ShowFoldedPositionGhost = true;
            settings.EnableFoldAnimation = false;

            var monitors = MonitorLayout.Current();
            Scale = MonitorLayout.PrimaryScale(monitors);
            Note = new StickyNote
            {
                X = 140, Y = 130, Width = 260, Height = 220, Content = "memo",
                FoldedX = separated ? 420 : 140, FoldedY = separated ? 360 : 130,
                IsPositionSeparated = separated,
                PositionLayout = MonitorLayout.Signature(monitors), PositionScale = Scale,
            };
            Window = new StickyNoteWindow(new StickyNoteViewModel(Note, settings), new StorageService(_root));
            Window.Show();
            Window.UpdateLayout();
        }

        public System.Drawing.Point? GhostLocation()
        {
            // 影の更新は Dispatcher にまとめて積まれるので、それが済むまで待つ。
            Window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            return Window.FoldedGhostPhysicalLocation;
        }

        public void Call(string name, params object?[] args)
            => typeof(StickyNoteWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window, args);

        public void Dispose()
        {
            Window.Close();
            App.Current.Settings.ShowFoldedPositionGhost = _previousSetting;
            App.Current.Settings.EnableFoldAnimation = _previousAnimation;
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
