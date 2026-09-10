using ScreenPinNotes.Models;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class NoteGeometryStateTests
{
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
}
