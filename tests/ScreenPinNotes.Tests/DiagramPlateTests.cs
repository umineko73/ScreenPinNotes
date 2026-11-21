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
using ScreenPinNotes.Services;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace ScreenPinNotes.Tests;

/// <summary>暗い付箋で draw.io の図の後ろに敷く台紙。</summary>
public sealed class DiagramPlateTests
{
    // 余白は図の短い辺の 4%。小さな図でも 8px、大きな図でも 32px に収める。
    [Theory]
    [InlineData(100, 100, 8)]
    [InlineData(400, 300, 12)]
    [InlineData(1600, 800, 32)]
    [InlineData(4000, 3000, 32)]
    public void PaddingFor_ScalesWithTheShortSideWithinLimits(int width, int height, int expected)
        => Assert.Equal(expected, DiagramPlate.PaddingFor(width, height));

    // 透明で書き出した図は縁が透けている。背景を付けて書き出した図は透けていない。
    [WpfFact]
    public void HasTransparentEdge_TellsTransparentExportsFromOpaqueOnes()
    {
        Assert.True(DiagramPlate.HasTransparentEdge(Solid(40, 30, 0, 0, 0, 0)));
        Assert.False(DiagramPlate.HasTransparentEdge(Solid(40, 30, 255, 255, 255, 255)));
        // 真ん中にだけ描いてあり、縁が透けている図。
        Assert.True(DiagramPlate.HasTransparentEdge(WithOpaqueCenter(40, 30)));
    }

    // 台紙の分だけ大きくなり、図は余白の内側にそのまま載る。余白には台紙の色が敷かれる。
    [WpfFact]
    public void Compose_PutsTheDiagramOnALightPlateWithPadding()
    {
        var source = Solid(200, 100, 0, 0, 0, 255);   // 黒一色の図
        var pad = DiagramPlate.PaddingFor(200, 100);

        var plated = DiagramPlate.Compose(source);

        Assert.Equal(200 + pad * 2, plated.PixelWidth);
        Assert.Equal(100 + pad * 2, plated.PixelHeight);
        Assert.Equal(Colors.Black, PixelAt(plated, pad + 100, pad + 50));
        Assert.Equal(DiagramPlate.PlateColor, PixelAt(plated, pad / 2, plated.PixelHeight / 2));
        // 角は丸めてあるので、いちばん端の点は透けている。
        Assert.Equal(0, PixelAt(plated, 0, 0).A);
    }

    private static BitmapSource Solid(int width, int height, byte b, byte g, byte r, byte a)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b; pixels[i + 1] = g; pixels[i + 2] = r; pixels[i + 3] = a;
        }
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }

    private static BitmapSource WithOpaqueCenter(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = height / 4; y < height * 3 / 4; y++)
            for (var x = width / 4; x < width * 3 / 4; x++)
                pixels[(y * width + x) * 4 + 3] = 255;
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }

    private static Color PixelAt(BitmapSource bitmap, int x, int y)
    {
        var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var p = new byte[4];
        bgra.CopyPixels(new Int32Rect(x, y, 1, 1), p, 4, 0);
        return Color.FromArgb(p[3], p[2], p[1], p[0]);
    }
}
