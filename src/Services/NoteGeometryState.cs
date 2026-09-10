using ScreenPinNotes.Models;

namespace ScreenPinNotes.Services;

/// <summary>閲覧・折りたたみ・編集の保存領域を管理する。座標は論理ピクセル。</summary>
public sealed class NoteGeometryState(StickyNote note)
{
    public double ExpandedHeight => note.Height;

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
        note.X = PreserveLogicalValue(note.X, x, dpiX);
        note.Y = PreserveLogicalValue(note.Y, y, dpiY);
        note.Width = PreserveLogicalValue(note.Width, width, dpiX);
        note.Height = PreserveLogicalValue(note.Height, height, dpiY);
    }

    public void CaptureFolded(double x, double y, double width, double dpiX, double dpiY)
    {
        note.FoldedX = PreserveLogicalValue(note.FoldedX ?? note.X, x, dpiX);
        note.FoldedY = PreserveLogicalValue(note.FoldedY ?? note.Y, y, dpiY);
        note.FoldedWidth = PreserveLogicalValue(note.FoldedWidth ?? note.Width, width, dpiX);
    }
}
