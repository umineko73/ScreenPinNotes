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

using System.Drawing;

namespace ScreenPinNotes.Services;

/// <summary>
/// ドラッグ中の矩形を、作業領域の端や他の付箋の辺へ吸着させる（物理ピクセル）。
/// 付箋本体の吸着（StickyNoteWindow.SnapToAll）と同じ規則で、接する辺には
/// 1ピクセルの隙間を残し、同じ辺どうしはそろえる。
/// </summary>
public static class EdgeSnap
{
    public static Point Snap(Rectangle moving, Rectangle workArea, IEnumerable<Rectangle> others, int distance)
    {
        int? bestLeft = null, bestTop = null;
        int minX = distance, minY = distance;
        int w = moving.Width, h = moving.Height;

        Try(moving.Left, workArea.Left, workArea.Left, ref bestLeft, ref minX);
        Try(moving.Right, workArea.Right, workArea.Right - w, ref bestLeft, ref minX);
        Try(moving.Top, workArea.Top, workArea.Top, ref bestTop, ref minY);
        Try(moving.Bottom, workArea.Bottom, workArea.Bottom - h, ref bestTop, ref minY);

        foreach (var o in others)
        {
            Try(moving.Left, o.Left, o.Left, ref bestLeft, ref minX);
            Try(moving.Left, o.Right + 1, o.Right + 1, ref bestLeft, ref minX);
            Try(moving.Right, o.Left - 1, o.Left - 1 - w, ref bestLeft, ref minX);
            Try(moving.Right, o.Right, o.Right - w, ref bestLeft, ref minX);

            Try(moving.Top, o.Top, o.Top, ref bestTop, ref minY);
            Try(moving.Top, o.Bottom + 1, o.Bottom + 1, ref bestTop, ref minY);
            Try(moving.Bottom, o.Top - 1, o.Top - 1 - h, ref bestTop, ref minY);
            Try(moving.Bottom, o.Bottom, o.Bottom - h, ref bestTop, ref minY);
        }

        return new Point(bestLeft ?? moving.Left, bestTop ?? moving.Top);
    }

    private static void Try(int edge, int target, int snapTo, ref int? best, ref int bestDistance)
    {
        var d = Math.Abs(edge - target);
        if (d < bestDistance) { bestDistance = d; best = snapTo; }
    }
}
