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

using System.Collections.Concurrent;
using SkiaSharp;
using WpfBitmapImage = System.Windows.Media.Imaging.BitmapImage;

namespace ScreenPinNotes.Services;

/// <summary>
/// 絵文字を画像にする。WPF は Segoe UI Emoji のカラーフォントを直接描けないため、
/// SkiaSharp で一度 PNG に落としてから Image として表示する。
/// 付箋のタイトルバーと設定画面の両方から使う。
/// </summary>
public static class EmojiRenderer
{
    // 同じ絵文字を何度も描き直さない。付箋の数だけ同じアイコンが並ぶことがある。
    // 色ありと色なしは別物なので、キーに混ぜて取り違えないようにする。
    private static readonly ConcurrentDictionary<string, WpfBitmapImage> Cache = new();

    /// <summary>絵文字の画像。空文字なら null（アイコンなし）。</summary>
    /// <param name="monochrome">色を抜いて明度だけで描くかどうか。</param>
    public static WpfBitmapImage? Render(string? icon, bool monochrome = false)
        => string.IsNullOrEmpty(icon)
            ? null
            : Cache.GetOrAdd((monochrome ? "m:" : "c:") + icon, RenderCore);

    private static WpfBitmapImage RenderCore(string cacheKey)
    {
        var monochrome = cacheKey[0] == 'm';
        var icon = cacheKey[2..];
        const int pixelSize = 64;
        using var bitmap = new SKBitmap(pixelSize, pixelSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var typeface = SKTypeface.FromFamilyName("Segoe UI Emoji");
        using var font = new SKFont(typeface, 52) { Subpixel = true };
        using var paint = new SKPaint { IsAntialias = true };
        // 明度は保ったまま彩度だけ落とす。同じ形のまま線画寄りの見た目になり、
        // 絵文字どうしの区別（明るい/暗い、細い/太い）は残る。
        if (monochrome)
            paint.ColorFilter = SKColorFilter.CreateColorMatrix(GrayscaleMatrix);

        font.MeasureText(icon, out SKRect bounds, paint);
        var x = (pixelSize - bounds.Width) / 2 - bounds.Left;
        var y = (pixelSize - bounds.Height) / 2 - bounds.Top;
        canvas.DrawText(icon, x, y, font, paint);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();
        var result = new WpfBitmapImage();
        result.BeginInit();
        result.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        result.StreamSource = stream;
        result.EndInit();
        result.Freeze();
        return result;
    }

    // 輝度の重み（Rec.601）。アルファはそのまま通し、絵文字の輪郭を保つ。
    private static readonly float[] GrayscaleMatrix =
    [
        0.299f, 0.587f, 0.114f, 0, 0,
        0.299f, 0.587f, 0.114f, 0, 0,
        0.299f, 0.587f, 0.114f, 0, 0,
        0,      0,      0,      1, 0,
    ];
}
