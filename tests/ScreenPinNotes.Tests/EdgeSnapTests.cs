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

using System.Drawing;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

/// <summary>畳んだ位置の影をドラッグしたときの吸着。</summary>
public class EdgeSnapTests
{
    private static readonly Rectangle WorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void SnapsToWorkAreaEdges()
    {
        var p = EdgeSnap.Snap(new Rectangle(6, 1040 - 30 - 5, 200, 30), WorkArea, [], 10);
        Assert.Equal(new Point(0, 1010), p);
    }

    [Fact]
    public void LeavesOnePixelGapNextToANoteAndAlignsSameEdges()
    {
        var note = Rectangle.FromLTRB(500, 300, 800, 600);
        // 右隣へ寄せると 1px 空け、上辺は揃える。
        var p = EdgeSnap.Snap(new Rectangle(805, 296, 200, 30), WorkArea, [note], 10);
        Assert.Equal(new Point(801, 300), p);
        // 真下へ寄せると 1px 空け、左辺は揃える。
        p = EdgeSnap.Snap(new Rectangle(503, 608, 200, 30), WorkArea, [note], 10);
        Assert.Equal(new Point(500, 601), p);
    }

    [Fact]
    public void DoesNotSnapBeyondTheDistance()
    {
        var note = Rectangle.FromLTRB(500, 300, 800, 600);
        var moving = new Rectangle(830, 350, 200, 30);
        Assert.Equal(moving.Location, EdgeSnap.Snap(moving, WorkArea, [note], 10));
    }
}
