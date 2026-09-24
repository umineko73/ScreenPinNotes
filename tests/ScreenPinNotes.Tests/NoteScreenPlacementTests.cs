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
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

/// <summary>
/// モニタ構成が保存時と違うときの付箋の出しかた。実機のモニタを相手にするので、
/// 判定は「この環境で掴める位置にあるか」と「本来の位置を書き換えていないか」で行う。
/// </summary>
public class NoteScreenPlacementTests
{
    [WpfFact]
    public void RescuedNoteStaysReachableAcrossFoldAndEditTransitions()
    {
        WpfApplicationFixture.Ensure();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { X = 20000, Y = 12000, FoldedX = -20000, FoldedY = 12000,
            PositionLayout = "disconnected-layout", PositionScale = 1.5, IsPositionSeparated = true };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), new StorageService(temp.Path));
        try
        {
            window.Show(); window.UpdateLayout();
            for (var i = 0; i < 2; i++)
            {
                typeof(StickyNoteWindow).GetMethod("ToggleFold", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object?[] { null });
                Invoke(window, "CompleteFoldAnimation");
                window.UpdateLayout();
                Assert.True(MonitorLayout.IsReachable(PhysicalRect(window), MonitorLayout.Current()));
            }
            Invoke(window, "EnterEditMode");
            Invoke(window, "EnterViewMode");
            window.UpdateLayout();
            Assert.True(MonitorLayout.IsReachable(PhysicalRect(window), MonitorLayout.Current()));
            Assert.Equal((20000d, 12000d, -20000d, 12000d), (note.X, note.Y, note.FoldedX!.Value, note.FoldedY!.Value));
            Assert.Equal("disconnected-layout", note.PositionLayout);
        }
        finally { window.Close(); }
    }
    [WpfFact]
    public void NoteSavedOnAMonitorThatIsGoneIsShownWhereItCanBeGrabbed()
    {
        WpfApplicationFixture.Ensure();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        // 接続の切れたモニタ（または縮んだ解像度）の外側に残った位置。
        var note = new StickyNote { X = 20000, Y = 12000, Width = 260, Height = 220,
            PositionLayout = "disconnected-layout", PositionScale = 1 };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), storage);
        try
        {
            window.Show();
            window.UpdateLayout();

            var monitors = MonitorLayout.Current();
            Assert.True(MonitorLayout.IsReachable(PhysicalRect(window), monitors));
            // 寄せた位置は「今だけの置き場所」。本来の位置は触らない。
            Assert.Equal((20000d, 12000d), (note.X, note.Y));
            Assert.Equal("disconnected-layout", note.PositionLayout);

            // 何度見直しても同じ場所に落ち着く。
            var once = PhysicalRect(window);
            Reconcile(window);
            Assert.Equal(once, PhysicalRect(window));

            window.Hide();
            window.Show();
            window.UpdateLayout();
            Assert.True(MonitorLayout.IsReachable(PhysicalRect(window), monitors));
            Assert.Equal((20000d, 12000d), (note.X, note.Y));
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void NoteGoesBackHomeWhenItsLayoutIsBack()
    {
        WpfApplicationFixture.Ensure();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var monitors = MonitorLayout.Current();
        var signature = MonitorLayout.Signature(monitors);
        var scale = MonitorLayout.PrimaryScale(monitors);
        var note = new StickyNote { X = 140, Y = 130, Width = 260, Height = 220,
            PositionLayout = signature, PositionScale = scale };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            var home = PhysicalRect(window);
            Assert.Equal(Math.Round(140 * scale), home.X);
            Assert.Equal(Math.Round(130 * scale), home.Y);

            // モニタ構成が変わり、保存時の構成ではなくなった状態。
            note.PositionLayout = "another-layout";
            // 取り外しのときに OS がウィンドウを動かすのと同じことをする。
            window.Left += 60;
            window.Top += 40;
            Assert.Equal((140d, 130d), (note.X, note.Y)); // 本来の位置は動かない
            Assert.Equal("another-layout", note.PositionLayout);

            // 構成が元に戻れば、見直しで元の位置へ帰る。
            note.PositionLayout = signature;
            Reconcile(window);
            Assert.Equal(home, PhysicalRect(window));
            Assert.Equal((140d, 130d), (note.X, note.Y));
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void NoteReturnsToWhereItWasPlacedInTheCurrentLayout()
    {
        WpfApplicationFixture.Ensure();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var monitors = MonitorLayout.Current();
        var signature = MonitorLayout.Signature(monitors);
        var scale = MonitorLayout.PrimaryScale(monitors);
        // 別の構成（ドッキング先など）がホームで、今の構成で置いた位置も覚えている付箋。
        var note = new StickyNote { X = 900, Y = 800, Width = 260, Height = 220,
            PositionLayout = "docked", PositionScale = 2,
            OtherLayoutPositions = [new LayoutPosition { Layout = signature, X = 150, Y = 160, Scale = scale }] };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), storage);
        try
        {
            window.Show();
            window.UpdateLayout();

            var placed = PhysicalRect(window);
            Assert.Equal(Math.Round(150 * scale), placed.X);
            Assert.Equal(Math.Round(160 * scale), placed.Y);
            Assert.Equal((150d, 160d, signature), (note.X, note.Y, note.PositionLayout));
            var docked = Assert.Single(note.OtherLayoutPositions);
            Assert.Equal(("docked", 900d, 800d, 2d), (docked.Layout, docked.X, docked.Y, docked.Scale));
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void MovesAreSavedUnderTheHomeLayoutAndDroppedUnderAnother()
    {
        WpfApplicationFixture.Ensure();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var signature = MonitorLayout.Signature(MonitorLayout.Current());
        var note = new StickyNote { X = 140, Y = 130, Width = 260, Height = 220 };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), storage);
        try
        {
            window.Show();
            window.UpdateLayout();

            // 構成の記録が無い付箋（更新前に保存されたもの）は、今の構成を引き受ける。
            window.Left = 180;
            window.Top = 170;
            StoreCurrentPosition(window);
            DevicePixelAssert.Near((180, 170), (note.X, note.Y), window);
            Assert.Equal(signature, note.PositionLayout);
            Assert.True(note.PositionScale > 0);

            // 別の構成では、動かしても本来の位置は残る。
            note.PositionLayout = "other-layout";
            window.Left = 240;
            window.Top = 230;
            StoreCurrentPosition(window);
            DevicePixelAssert.Near((180, 170), (note.X, note.Y), window);
            Assert.Equal("other-layout", note.PositionLayout);

            // ただし自分でドラッグして置き直したときは、その構成を引き受ける。
            // 解像度を変えたまま使い続けても、並べ直した位置を覚える。
            Invoke(window, "AdoptCurrentLayoutAsHome");
            StoreCurrentPosition(window);
            DevicePixelAssert.Near((240, 230), (note.X, note.Y), window);
            Assert.Equal(signature, note.PositionLayout);
            // 元の構成での位置は捨てずに残る。その構成に戻れば帰れるように。
            var kept = Assert.Single(note.OtherLayoutPositions);
            Assert.Equal("other-layout", kept.Layout);
            DevicePixelAssert.Near((180, 170), (kept.X, kept.Y), window);
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void NoteOnAMonitorWithAnotherScaleComesBackToTheSamePixels()
    {
        WpfApplicationFixture.Ensure();
        var monitors = MonitorLayout.Current();
        var primaryScale = MonitorLayout.PrimaryScale(monitors);
        var other = monitors.FirstOrDefault(m => Math.Abs(m.Scale - primaryScale) > 0.01);
        // 拡大率が混在していない環境では確かめられない（合成した構成の計算は MonitorLayoutTests）。
        if (other.WorkArea.IsEmpty || other.Scale <= 0) return;

        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        // そのモニタの中の、四辺から余裕のある位置（物理px）。
        var physicalX = other.WorkArea.X + 200;
        var physicalY = other.WorkArea.Y + 150;
        var note = new StickyNote
        {
            // 保存値はそのモニタの拡大率を基準にした論理px。
            X = physicalX / other.Scale, Y = physicalY / other.Scale, Width = 260, Height = 220,
            PositionScale = other.Scale, PositionLayout = MonitorLayout.Signature(monitors),
        };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), storage);
        try
        {
            window.Show();
            window.UpdateLayout();

            // 拡大率の違うモニタでも、保存したピクセルに出る。
            var rect = PhysicalRect(window);
            Assert.InRange(rect.X, physicalX - 1, physicalX + 1);
            Assert.InRange(rect.Y, physicalY - 1, physicalY + 1);

            // 同じ場所のまま保存し直しても、位置と基準は変わらない。
            StoreCurrentPosition(window);
            Assert.Equal(other.Scale, note.PositionScale);
            Assert.InRange(note.X, physicalX / other.Scale - 1, physicalX / other.Scale + 1);
            Assert.InRange(note.Y, physicalY / other.Scale - 1, physicalY / other.Scale + 1);
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void NoteOnAMonitorWithAnotherScaleDoesNotDriftAcrossShows()
    {
        WpfApplicationFixture.Ensure();
        var monitors = MonitorLayout.Current();
        var primaryScale = MonitorLayout.PrimaryScale(monitors);
        var other = monitors.FirstOrDefault(m => Math.Abs(m.Scale - primaryScale) > 0.01);
        if (other.WorkArea.IsEmpty || other.Scale <= 0) return;

        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            X = (other.WorkArea.X + 300) / other.Scale, Y = (other.WorkArea.Y + 260) / other.Scale,
            Width = 260, Height = 220,
            PositionScale = other.Scale, PositionLayout = MonitorLayout.Signature(monitors),
        };
        var saved = (note.X, note.Y);
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            var first = PhysicalRect(window);

            // 拡大率の違うモニタへ入ると WPF は DPI 変更に合わせて座標を読み替える。
            // 表示し直すたびにその読み替えぶんが積もると、付箋が少しずつ動いていく。
            for (var cycle = 0; cycle < 3; cycle++)
            {
                window.Hide();
                window.Show();
                window.UpdateLayout();
                Assert.Equal(first, PhysicalRect(window));
                Assert.Equal(saved, (note.X, note.Y));
            }
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void NoteWithoutARecordedScaleIsLeftWhereWpfPutsIt()
    {
        WpfApplicationFixture.Ensure();
        var monitors = MonitorLayout.Current();
        var other = monitors.FirstOrDefault(m =>
            Math.Abs(m.Scale - MonitorLayout.PrimaryScale(monitors)) > 0.01);
        if (other.WorkArea.IsEmpty || other.Scale <= 0) return;

        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        // この機能より前に保存された付箋。保存値がどのモニタの基準かは分からない。
        var note = new StickyNote { X = (other.WorkArea.X + 300) / other.Scale,
            Y = (other.WorkArea.Y + 260) / other.Scale, Width = 260, Height = 220,
            PositionScale = 0, PositionLayout = "" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            // 勝手に読み替えない。WPF が決めた場所（＝更新前と同じ場所）のまま。
            Assert.Equal(note.X, window.Left);
            Assert.Equal(note.Y, window.Top);
            Assert.True(MonitorLayout.IsReachable(PhysicalRect(window), monitors));
        }
        finally { window.Close(); }
    }

    private static Rect PhysicalRect(Window window)
    {
        Assert.True(GetWindowRect(new WindowInteropHelper(window).Handle, out var r));
        return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    private static void Reconcile(StickyNoteWindow window) => Invoke(window, "ReconcileScreenPlacement");

    private static void StoreCurrentPosition(StickyNoteWindow window)
        => Invoke(window, "StoreCurrentPositionInModel");

    private static void Invoke(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(target, []);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    private sealed class TempDataDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
