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

public class NoteLayersTests
{
    [Theory]
    [InlineData(LayerMove.Top, "B,D,A,C,E")]
    [InlineData(LayerMove.Up, "B,A,D,C,E")]
    [InlineData(LayerMove.Down, "A,C,B,E,D")]
    [InlineData(LayerMove.Bottom, "A,C,E,B,D")]
    public void MovesMultipleNotesPreservingRelativeOrder(LayerMove move, string expected)
    {
        var notes = "ABCDE".Select((id, index) => new StickyNote { Id = id.ToString(), LayerOrder = index }).ToList();
        NoteLayers.Move(notes, new HashSet<string> { "B", "D" }, move);
        Assert.Equal(expected, string.Join(",", NoteLayers.Ordered(notes).Select(n => n.Id)));
    }

    [Fact]
    public void ContiguousSelectionMovesAsBlockAndStopsAtBoundary()
    {
        var notes = "ABCD".Select((id, index) => new StickyNote { Id = id.ToString(), LayerOrder = index }).ToList();
        var selected = new HashSet<string> { "B", "C" };
        NoteLayers.Move(notes, selected, LayerMove.Up);
        NoteLayers.Move(notes, selected, LayerMove.Up);
        Assert.Equal("BCAD", string.Concat(NoteLayers.Ordered(notes).Select(n => n.Id)));
    }

    [Fact]
    public void HiddenNotesKeepTheirLayerAndPinnedNotesStayAboveNormalNotes()
    {
        var pinned = new StickyNote { Id = "P", IsTopmost = true };
        var hidden = new StickyNote { Id = "H", IsHidden = true, LayerOrder = 1 };
        var normal = new StickyNote { Id = "N", LayerOrder = 0 };
        var notes = new[] { normal, pinned, hidden };
        NoteLayers.Move(notes, new HashSet<string> { "H" }, LayerMove.Top);
        Assert.Equal("PHN", string.Concat(NoteLayers.Ordered(notes).Select(n => n.Id)));
        Assert.True(pinned.IsTopmost);
        Assert.True(hidden.IsHidden);
    }
}
