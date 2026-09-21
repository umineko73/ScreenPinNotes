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
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenPinNotes.Models;
using WpfBrushes       = System.Windows.Media.Brushes;
using WpfFlowDirection = System.Windows.FlowDirection;
using WpfFontFamily    = System.Windows.Media.FontFamily;
using WpfPoint         = System.Windows.Point;

namespace ScreenPinNotes.Services;

/// <summary>
/// 絵文字を画像にする。WPF は Segoe UI Emoji のカラーフォントを直接描けないため、
/// 色付きの絵文字はあらかじめ PNG に焼いて exe へ埋め込んである
/// （tools/EmojiAssets が Resources\Emoji\ へ書き出す。焼き直し方は CLAUDE.md）。
/// 焼いていない絵文字（settings.json の IconPalette に手で足したものなど）は
/// WPF のフォント描画にそのまま流すので、形は出るが白黒になる。
/// 付箋のタイトルバーと設定画面の両方から使う。
/// </summary>
public static class EmojiRenderer
{
    /// <summary>焼いてある PNG の一辺。tools/EmojiAssets と揃える。</summary>
    public const int PixelSize = 64;

    /// <summary>フォントから描くときの字の大きさ。同上。</summary>
    public const double GlyphSize = 52;

    // 同じ絵文字を何度も描き直さない。付箋の数だけ同じアイコンが並ぶことがある。
    // 色ありと色なしは別物なので、キーに混ぜて取り違えないようにする。
    private static readonly ConcurrentDictionary<string, BitmapSource> Cache = new();

    // 埋め込みリソース名は「ScreenPinNotes.Resources.Emoji.<キー>.png」。
    // 実際に埋まっている名前から引くので、リソース名の綴りに依存しない。
    private static readonly Lazy<Dictionary<string, string>> Assets = new(FindAssets);

    /// <summary>PNG を焼いておく絵文字。tools/EmojiAssets はこの一覧を描く。</summary>
    public static IEnumerable<string> BakedGlyphs
        => AppSettings.IconGroups.SelectMany(group => group.Icons)
            .Where(icon => !string.IsNullOrEmpty(icon))
            .Distinct(StringComparer.Ordinal);

    /// <summary>絵文字の画像。空文字なら null（アイコンなし）。</summary>
    /// <param name="monochrome">色を抜いて明度だけで描くかどうか。</param>
    public static BitmapSource? Render(string? icon, bool monochrome = false)
        => string.IsNullOrEmpty(icon)
            ? null
            : Cache.GetOrAdd((monochrome ? "m:" : "c:") + icon, RenderCore);

    /// <summary>PNG が焼いてあるか。焼いていない絵文字は白黒になる。</summary>
    public static bool HasBakedImage(string icon)
        => !string.IsNullOrEmpty(icon) && Assets.Value.ContainsKey(AssetKey(icon));

    /// <summary>PNG のファイル名（拡張子なし）。コードポイントを並べたもの。</summary>
    public static string AssetKey(string icon)
    {
        var key = new StringBuilder();
        foreach (var rune in icon.EnumerateRunes())
        {
            if (key.Length > 0) key.Append('-');
            key.Append(rune.Value.ToString("x4", CultureInfo.InvariantCulture));
        }
        return key.ToString();
    }

    private static BitmapSource RenderCore(string cacheKey)
    {
        var monochrome = cacheKey[0] == 'm';
        var icon = cacheKey[2..];
        var baked = LoadBaked(icon);
        // 焼いていない絵文字はフォント描画へ。どのみち色は付かないので、
        // モノクロ指定でもそのまま返す。
        if (baked == null) return RenderWithFont(icon);
        return monochrome ? ToMonochrome(baked) : baked;
    }

    private static BitmapSource? LoadBaked(string icon)
    {
        if (!Assets.Value.TryGetValue(AssetKey(icon), out var resource)) return null;
        using var stream = typeof(EmojiRenderer).Assembly.GetManifestResourceStream(resource);
        if (stream == null) return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    // 明度は保ったまま彩度だけ落とす。同じ形のまま線画寄りの見た目になり、
    // 絵文字どうしの区別（明るい/暗い、細い/太い）は残る。
    private static BitmapSource ToMonochrome(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            // 輝度の重み（Rec.601）。アルファ（i+3）はそのまま通し、絵文字の輪郭を保つ。
            var luma = (byte)Math.Round(pixels[i + 2] * 0.299 + pixels[i + 1] * 0.587 + pixels[i] * 0.114);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = luma;
        }
        var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96,
            PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private static BitmapSource RenderWithFont(string icon)
    {
        var text = new FormattedText(icon, CultureInfo.InvariantCulture, WpfFlowDirection.LeftToRight,
            new Typeface(new WpfFontFamily("Segoe UI Emoji"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            GlyphSize, WpfBrushes.Black, pixelsPerDip: 1.0);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawText(text, new WpfPoint((PixelSize - text.Width) / 2, (PixelSize - text.Height) / 2));
        var target = new RenderTargetBitmap(PixelSize, PixelSize, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static Dictionary<string, string> FindAssets()
    {
        const string prefix = "ScreenPinNotes.Resources.Emoji.";
        const string suffix = ".png";
        return typeof(EmojiRenderer).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal)
                && name.EndsWith(suffix, StringComparison.Ordinal))
            .ToDictionary(name => name[prefix.Length..^suffix.Length], StringComparer.Ordinal);
    }
}
