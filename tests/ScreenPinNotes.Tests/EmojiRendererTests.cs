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

using System.Windows.Media.Imaging;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class EmojiRendererTests
{
    [Fact]
    public void Render_EmptyIcon_IsNothing()
    {
        Assert.Null(EmojiRenderer.Render(""));
        Assert.Null(EmojiRenderer.Render(null));
    }

    // モノクロ表示は彩度だけを落とす。形と濃淡は残るので絵文字どうしの
    // 区別は付いたままになる。
    [Fact]
    public void Render_Monochrome_LeavesNoColourWhileTheColourVersionKeepsIt()
    {
        const string RedCircle = "🔴";

        var colour = AveragePixel(EmojiRenderer.Render(RedCircle, monochrome: false)!);
        var mono = AveragePixel(EmojiRenderer.Render(RedCircle, monochrome: true)!);

        Assert.True(colour.A > 0, "the emoji did not render");
        Assert.True(colour.R - colour.B > 30, $"expected a red emoji, got {colour}");
        Assert.InRange(Math.Abs(mono.R - mono.G), 0, 2);
        Assert.InRange(Math.Abs(mono.G - mono.B), 0, 2);
        // 透明部分は透明のまま。輪郭が四角い板にならないことの確認。
        Assert.Equal(colour.A, mono.A);
    }

    // 同じ絵文字でも色ありと色なしは別画像。キャッシュが取り違えないこと。
    [Fact]
    public void Render_CachesColourAndMonochromeSeparately()
    {
        var colour = EmojiRenderer.Render("🦊", monochrome: false);
        var mono = EmojiRenderer.Render("🦊", monochrome: true);

        Assert.NotSame(colour, mono);
        Assert.Same(colour, EmojiRenderer.Render("🦊", monochrome: false));
        Assert.Same(mono, EmojiRenderer.Render("🦊", monochrome: true));
    }

    // パレットの絵文字は PNG が焼いてあること。焼き忘れると色が出ず白黒になるので、
    // アイコンを足したら tools/EmojiAssets を流し直す必要がある。
    [Fact]
    public void BakedGlyphs_AllHaveAnImage()
    {
        var missing = EmojiRenderer.BakedGlyphs.Where(glyph => !EmojiRenderer.HasBakedImage(glyph)).ToArray();

        Assert.True(missing.Length == 0,
            $"PNG が無い絵文字: {string.Join(" ", missing)} — dotnet run --project tools/EmojiAssets で焼き直す");
    }

    // settings.json の IconPalette に手で足した絵文字など、焼いていないものは
    // WPF のフォント描画に落とす。色は出ないが形は出る。
    [WpfFact]
    public void Render_GlyphWithoutAnImage_FallsBackToTheFont()
    {
        const string NotInPalette = "🥝";
        Assert.False(EmojiRenderer.HasBakedImage(NotInPalette), "この絵文字はパレットに入っていない前提");

        var rendered = Assert.IsAssignableFrom<BitmapSource>(EmojiRenderer.Render(NotInPalette));
        var average = AveragePixel(rendered);

        Assert.True(average.A > 0, "the emoji did not render");
        Assert.InRange(Math.Abs(average.R - average.G), 0, 2);
        Assert.InRange(Math.Abs(average.G - average.B), 0, 2);
    }

    // 不透明度で重みを付けた平均色。背景の透明部分に薄められないようにする。
    private static (double A, double R, double G, double B) AveragePixel(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        double a = 0, r = 0, g = 0, b = 0, weight = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            double alpha = pixels[i + 3];
            a += alpha;
            if (alpha == 0) continue;
            weight += alpha;
            b += pixels[i] * alpha;
            g += pixels[i + 1] * alpha;
            r += pixels[i + 2] * alpha;
        }
        var count = pixels.Length / 4;
        if (weight == 0) return (0, 0, 0, 0);
        return (Math.Round(a / count, 2), r / weight, g / weight, b / weight);
    }
}
