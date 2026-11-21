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

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace ScreenPinNotes.Services;

/// <summary>
/// 暗い付箋に貼った draw.io の図の後ろに敷く、明るい台紙。
/// </summary>
/// <remarks>
/// draw.io の図は白い紙の上で描く前提の色（黒い線と文字）でできている。背景を透明にして
/// 書き出した PNG を暗い付箋にそのまま置くと、線と文字が地に沈んで読めない。そこで、
/// 図のまわりに少し余白を取った角の丸い台紙を描き、その上に図を重ねた画像を作る。
/// 図の色は変えない。背景の付いた（透明なところのない）PNG はそのまま使う。
/// </remarks>
public static class DiagramPlate
{
    /// <summary>台紙の色。真っ白だと暗い付箋の中でまぶしいので、ごく薄い灰色にする。</summary>
    public static readonly Color PlateColor = Color.FromRgb(0xF5, 0xF6, 0xF8);

    private const int MinPadding = 8;
    private const int MaxPadding = 32;

    /// <summary>台紙の余白（ピクセル）。図の短い辺の 4% を、8〜32 に収める。</summary>
    public static int PaddingFor(int pixelWidth, int pixelHeight)
        => Math.Clamp((int)Math.Round(Math.Min(pixelWidth, pixelHeight) * 0.04), MinPadding, MaxPadding);

    /// <summary>
    /// 縁に透けているところがあるか。四隅と各辺の中ほどを見る。draw.io は図の外側を
    /// 塗らないので、透明で書き出した図なら縁は透けている。
    /// </summary>
    public static bool HasTransparentEdge(BitmapSource source)
    {
        if (source.PixelWidth <= 0 || source.PixelHeight <= 0) return false;
        var bgra = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int right = source.PixelWidth - 1, bottom = source.PixelHeight - 1;
        int midX = right / 2, midY = bottom / 2;
        var pixel = new byte[4];
        foreach (var (x, y) in new[] { (0, 0), (right, 0), (0, bottom), (right, bottom), (midX, 0), (midX, bottom), (0, midY), (right, midY) })
        {
            bgra.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            if (pixel[3] < 250) return true;
        }
        return false;
    }

    /// <summary>台紙に図を載せた画像。大きさは図に余白を足したもの（ピクセル単位で同じ比率）。</summary>
    public static BitmapSource Compose(BitmapSource source)
    {
        int w = source.PixelWidth, h = source.PixelHeight;
        var pad = PaddingFor(w, h);
        int width = w + pad * 2, height = h + pad * 2;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var plate = new SolidColorBrush(PlateColor);
            plate.Freeze();
            dc.DrawRoundedRectangle(plate, null, new Rect(0, 0, width, height), pad, pad);
            // 96 DPI で描くので 1 DIP = 1 ピクセル。元の DPI に関係なくピクセルの大きさで置く。
            dc.DrawImage(source, new Rect(pad, pad, w, h));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
