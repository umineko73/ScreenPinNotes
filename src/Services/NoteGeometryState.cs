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

namespace ScreenPinNotes.Services;

/// <summary>
/// 位置を保存してよいかと、保存するときに添える基準（モニタ構成と拡大率）。
/// 付箋を一時的に別モニタへ寄せているあいだは CanStore が false で、
/// 本来の位置を上書きしない。
/// </summary>
public readonly record struct NotePositionContext(bool CanStore, string Layout, double Scale);

/// <summary>閲覧・折りたたみ・編集の保存領域を管理する。座標は論理ピクセル。</summary>
/// <param name="positionContext">
/// 位置を書き戻すたびに参照する基準。null なら常に書き戻し、基準も記録しない。
/// </param>
public sealed class NoteGeometryState(StickyNote note, Func<NotePositionContext>? positionContext = null)
{
    public double ExpandedHeight => note.Height;

    /// <summary>
    /// 位置を書き戻してよいか。モニタ構成が保存時と違うなら、今の位置は
    /// 「今だけの置き場所」なので記録しない。書き戻すときは基準も一緒に更新する
    /// （位置と基準がずれると、次回の復元先が分からなくなる）。
    /// </summary>
    private bool BeginStorePosition()
    {
        if (positionContext is null) return true;
        var context = positionContext();
        if (!context.CanStore) return false;
        StampPositionContext(note, context);
        return true;
    }

    public static void StampPositionContext(StickyNote note, NotePositionContext context)
    {
        // Both presentation modes share the scale. Rebase the untouched mode too,
        // otherwise storing one mode on another-DPI monitor moves the other's home.
        if (note.PositionScale > 0 && context.Scale > 0 && note.PositionScale != context.Scale)
        {
            var ratio = note.PositionScale / context.Scale;
            note.X *= ratio;
            note.Y *= ratio;
            note.FoldedX *= ratio;
            note.FoldedY *= ratio;
        }
        note.PositionLayout = context.Layout;
        note.PositionScale = context.Scale;
    }

    public (double Width, double Height) GetSize(bool editing)
    {
        static double Valid(double? size, double fallback) =>
            size is > 0 && double.IsFinite(size.Value) ? size.Value : fallback;
        return editing
            ? (Math.Max(note.Width, Valid(note.EditWidth, note.Width)),
               Math.Max(note.Height, Valid(note.EditHeight, note.Height)))
            : (note.Width, note.Height);
    }

    public void StoreSize(double width, double height, bool editing, double dpiX = 1, double dpiY = 1)
    {
        if (note.IsFolded)
            note.FoldedWidth = PreserveLogicalValue(note.FoldedWidth ?? note.Width, width, dpiX);
        else if (editing)
            (note.EditWidth, note.EditHeight) =
                (PreserveLogicalValue(note.EditWidth ?? note.Width, width, dpiX),
                 PreserveLogicalValue(note.EditHeight ?? note.Height, height, dpiY));
        else
            (note.Width, note.Height) = (PreserveLogicalValue(note.Width, width, dpiX),
                PreserveLogicalValue(note.Height, height, dpiY));
    }

    public void StorePosition(double x, double y)
    {
        if (!BeginStorePosition()) return;
        if (note.IsFolded || !note.IsPositionSeparated)
            (note.FoldedX, note.FoldedY) = (x, y);
        if (!note.IsFolded || !note.IsPositionSeparated)
            (note.X, note.Y) = (x, y);
    }

    // 同じ物理ピクセルに表示される場合だけ、保存済みの論理値を維持する。
    // 半ピクセル以内という距離判定では、境界の両側にある別々のピクセルを
    // 同一視してしまい、ユーザーの1ピクセルのリサイズまで捨ててしまう。
    public static double PreserveLogicalValue(double saved, double displayed, double dpiScale) =>
        Math.Round(saved * dpiScale, MidpointRounding.AwayFromZero) ==
        Math.Round(displayed * dpiScale, MidpointRounding.AwayFromZero) ? saved : displayed;

    public void CaptureExpanded(double x, double y, double width, double height, double dpiX, double dpiY)
    {
        if (BeginStorePosition())
        {
            note.X = PreserveLogicalValue(note.X, x, dpiX);
            note.Y = PreserveLogicalValue(note.Y, y, dpiY);
        }
        note.Width = PreserveLogicalValue(note.Width, width, dpiX);
        note.Height = PreserveLogicalValue(note.Height, height, dpiY);
    }

    public void CaptureFolded(double x, double y, double width, double dpiX, double dpiY)
    {
        if (BeginStorePosition())
        {
            note.FoldedX = PreserveLogicalValue(note.FoldedX ?? note.X, x, dpiX);
            note.FoldedY = PreserveLogicalValue(note.FoldedY ?? note.Y, y, dpiY);
        }
        note.FoldedWidth = PreserveLogicalValue(note.FoldedWidth ?? note.Width, width, dpiX);
    }
}
