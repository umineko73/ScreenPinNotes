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

using ScreenPinNotes.Models;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class NoteGeometryStateTests
{
    [Fact]
    public void SavingAtAnotherDpiPreservesOtherModesPhysicalPosition()
    {
        var note = new StickyNote { X = 100, Y = 200, FoldedX = 1800, FoldedY = 300,
            IsPositionSeparated = true, PositionScale = 1.5, PositionLayout = "home" };
        var state = new NoteGeometryState(note, () => new(true, "home", 1));
        state.StorePosition(120, 130);
        Assert.Equal((2700d, 450d), (note.FoldedX!.Value * note.PositionScale, note.FoldedY!.Value * note.PositionScale));
        Assert.Equal((120d, 130d), (note.X, note.Y));
    }
    [Theory]
    [InlineData(false, false, -1)]
    [InlineData(false, false, 1)]
    [InlineData(true, false, -1)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, -1)]
    [InlineData(false, true, 1)]
    public void OnePixelResizeAtRoundingBoundaryIsSaved(bool folded, bool editing, int delta)
    {
        const double dpi = 1.25;
        var note = new StickyNote { Width = 230, Height = 250, FoldedWidth = 230,
            EditWidth = 230, EditHeight = 250, IsFolded = folded };
        var state = new NoteGeometryState(note);
        const double initialPixels = 288;
        state.StoreSize(initialPixels / dpi, 250, editing, dpi, dpi);
        var resizedWidth = (initialPixels + delta) / dpi;
        state.StoreSize(resizedWidth, 250, editing, dpi, dpi);

        var savedWidth = folded ? note.FoldedWidth : editing ? note.EditWidth : note.Width;
        Assert.Equal(resizedWidth, savedWidth);
        // 保存後に状態管理を作り直しても、次の表示イベントで元へ戻らない。
        new NoteGeometryState(note).StoreSize(resizedWidth, 250, editing, dpi, dpi);
        Assert.Equal(resizedWidth, folded ? note.FoldedWidth : editing ? note.EditWidth : note.Width);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void RepeatedDisplayRoundingDoesNotChangeSavedBounds(double dpi)
    {
        var note = new StickyNote { X = 90, Y = 91, Width = 230, Height = 251,
            FoldedX = 93, FoldedY = 94, FoldedWidth = 231 };
        var state = new NoteGeometryState(note);
        double Display(double value) => Math.Round(value * dpi, MidpointRounding.AwayFromZero) / dpi;
        for (var cycle = 0; cycle < 20; cycle++)
        {
            state.CaptureExpanded(Display(note.X), Display(note.Y), Display(note.Width), Display(note.Height), dpi, dpi);
            state.StoreSize(Display(note.Width), Display(note.Height), false, dpi, dpi);
            note.IsFolded = true;
            state.CaptureFolded(Display(note.FoldedX!.Value), Display(note.FoldedY!.Value), Display(note.FoldedWidth!.Value), dpi, dpi);
            state.StoreSize(Display(note.FoldedWidth.Value), 28, false, dpi, dpi);
            note.IsFolded = false;
        }
        Assert.Equal((90d, 91d, 230d, 251d), (note.X, note.Y, note.Width, note.Height));
        Assert.Equal((93d, 94d, 231d), (note.FoldedX!.Value, note.FoldedY!.Value, note.FoldedWidth!.Value));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MoveOnlySynchronizesPositionsWhenTheyAreNotSeparated(bool folded, bool separated)
    {
        var note = new StickyNote { X = 90, Y = 91, FoldedX = 92, FoldedY = 93,
            IsFolded = folded, IsPositionSeparated = separated };
        new NoteGeometryState(note).StorePosition(200.8, 300.8);
        Assert.Equal(folded && separated ? 90 : 200.8, note.X);
        Assert.Equal(folded && separated ? 91 : 300.8, note.Y);
        Assert.Equal(!folded && separated ? 92 : 200.8, note.FoldedX);
        Assert.Equal(!folded && separated ? 93 : 300.8, note.FoldedY);
    }

    [Fact]
    public void EditResizeKeepsExpandedSizeAndRemembersRealPixelChanges()
    {
        var note = new StickyNote { Width = 300, Height = 250, EditWidth = 320, EditHeight = 280 };
        var state = new NoteGeometryState(note);
        state.StoreSize(320.8, 280.8, true, 1.25, 1.25);
        Assert.Equal((320.8, 280.8), state.GetSize(true));
        Assert.Equal((300d, 250d), state.GetSize(false));
        Assert.Equal(250, state.ExpandedHeight);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(-1d, 0d)]
    [InlineData(double.NaN, double.PositiveInfinity)]
    [InlineData(100d, 100d)]
    public void EditingNeverShrinksBelowExpandedSize(double? width, double? height)
    {
        var note = new StickyNote { Width = 300, Height = 250, EditWidth = width, EditHeight = height };
        Assert.Equal((300d, 250d), new NoteGeometryState(note).GetSize(true));
    }

    // ─── モニタ構成が違うあいだの位置 ──────────────────────────

    private static NotePositionContext Home(bool canStore)
        => new(canStore, "1920,0,2560x1400@1.5", 1.5);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PositionIsStoredOnlyUnderTheLayoutItBelongsTo(bool canStore)
    {
        var note = new StickyNote { X = 90, Y = 91, FoldedX = 90, FoldedY = 91,
            PositionLayout = "old", PositionScale = 1 };
        var state = new NoteGeometryState(note, () => Home(canStore));

        state.StorePosition(500, 600);

        Assert.Equal(canStore ? 500 : 90, note.X);
        Assert.Equal(canStore ? 600 : 91, note.Y);
        Assert.Equal(canStore ? 500 : 90, note.FoldedX);
        // 書き戻したときだけ基準も更新する。位置と基準がずれると復元先が分からなくなる。
        Assert.Equal(canStore ? "1920,0,2560x1400@1.5" : "old", note.PositionLayout);
        Assert.Equal(canStore ? 1.5 : 1, note.PositionScale);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CapturingKeepsSizesButGuardsThePosition(bool canStore)
    {
        var note = new StickyNote { X = 90, Y = 91, Width = 230, Height = 251,
            FoldedX = 93, FoldedY = 94, FoldedWidth = 231 };
        var state = new NoteGeometryState(note, () => Home(canStore));

        state.CaptureExpanded(500, 600, 240, 260, 1, 1);
        note.IsFolded = true;
        state.CaptureFolded(700, 800, 241, 1, 1);

        Assert.Equal(canStore ? (500d, 600d) : (90d, 91d), (note.X, note.Y));
        Assert.Equal(canStore ? (700d, 800d) : (93d, 94d), (note.FoldedX!.Value, note.FoldedY!.Value));
        // 大きさは構成に関係なく記録する（どのモニタでも同じ見た目にしたい）。
        Assert.Equal((240d, 260d, 241d), (note.Width, note.Height, note.FoldedWidth!.Value));
    }

    [Fact]
    public void NotesWithoutARecordedLayoutAreLeftToTheCaller()
    {
        // 基準を渡さない使い方（従来どおり）では、いつでも書き戻す。
        var note = new StickyNote { X = 90, Y = 91, PositionLayout = "old", PositionScale = 1 };
        new NoteGeometryState(note).StorePosition(500, 600);
        Assert.Equal((500d, 600d, "old", 1d), (note.X, note.Y, note.PositionLayout, note.PositionScale));
    }

    // ─── 構成ごとのホーム ──────────────────────────────────────

    [Fact]
    public void MovingANoteInAnotherLayoutKeepsTheOriginalLayoutsHome()
    {
        // ドッキング中に置いた位置。
        var note = new StickyNote { X = 100, Y = 110, FoldedX = 120, FoldedY = 130,
            PositionLayout = "docked", PositionScale = 1.5 };
        var state = new NoteGeometryState(note, () => new(note.PositionLayout == "laptop", "laptop", 1));

        // ノートPC単体で自分で動かした。
        NoteGeometryState.AdoptLayout(note, "laptop");
        state.StorePosition(10, 20);
        Assert.Equal((10d, 20d, "laptop", 1d), (note.X, note.Y, note.PositionLayout, note.PositionScale));

        // ドッキングし直すと、ドッキング中の位置へ帰る。
        Assert.True(NoteGeometryState.RestoreLayoutHome(note, "docked"));
        Assert.Equal((100d, 110d, 120d, 130d), (note.X, note.Y, note.FoldedX!.Value, note.FoldedY!.Value));
        Assert.Equal(("docked", 1.5), (note.PositionLayout, note.PositionScale));

        // もう一度外せば、ノートPCで置いた位置へ。
        Assert.True(NoteGeometryState.RestoreLayoutHome(note, "laptop"));
        Assert.Equal((10d, 20d, "laptop"), (note.X, note.Y, note.PositionLayout));
        Assert.Equal(["docked"], note.OtherLayoutPositions.Select(p => p.Layout));
    }

    [Fact]
    public void AnUnknownLayoutLeavesTheHomeUntouched()
    {
        var note = new StickyNote { X = 100, Y = 110, PositionLayout = "docked", PositionScale = 1 };

        Assert.False(NoteGeometryState.RestoreLayoutHome(note, "projector"));
        Assert.False(NoteGeometryState.RestoreLayoutHome(note, "docked"));

        Assert.Equal((100d, 110d, "docked"), (note.X, note.Y, note.PositionLayout));
        Assert.Empty(note.OtherLayoutPositions);
    }

    [Fact]
    public void OnlyTheMostRecentLayoutsAreRemembered()
    {
        var note = new StickyNote { PositionLayout = "layout0", PositionScale = 1 };
        for (var i = 1; i <= NoteGeometryState.MaxRememberedLayouts + 3; i++)
        {
            NoteGeometryState.AdoptLayout(note, $"layout{i}");
            note.X = i;
        }

        Assert.Equal(NoteGeometryState.MaxRememberedLayouts, note.OtherLayoutPositions.Count);
        Assert.Equal($"layout{NoteGeometryState.MaxRememberedLayouts + 2}", note.OtherLayoutPositions[0].Layout);
        Assert.DoesNotContain(note.OtherLayoutPositions, p => p.Layout == note.PositionLayout);
    }

    [Fact]
    public void AdoptingFromAnUnrecordedLayoutStashesNothing()
    {
        var note = new StickyNote { X = 5, Y = 6 };
        NoteGeometryState.AdoptLayout(note, "laptop");
        Assert.Equal("laptop", note.PositionLayout);
        Assert.Empty(note.OtherLayoutPositions);
    }
}
