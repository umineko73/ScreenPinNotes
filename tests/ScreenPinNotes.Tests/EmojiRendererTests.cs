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

    // 不透明度で重みを付けた平均色。背景の透明部分に薄められないようにする。
    private static (double A, double R, double G, double B) AveragePixel(BitmapImage image)
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
