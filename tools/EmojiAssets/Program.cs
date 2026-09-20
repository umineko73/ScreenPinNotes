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
using System.IO;
using System.Text.RegularExpressions;
using ScreenPinNotes.Services;
using SkiaSharp;

namespace ScreenPinNotes.Tools.EmojiAssets;

/// <summary>
/// アイコン用の絵文字を PNG に焼いて src\Resources\Emoji\ へ書き出す。
/// WPF は Segoe UI Emoji のカラーフォントを描けないので、アプリ本体は
/// この PNG を埋め込みリソースとして読む（Services\EmojiRenderer.cs）。
/// SkiaSharp を使うのはここだけ。リポジトリのルートから
///   dotnet run --project tools/EmojiAssets
/// で実行し、出てきた PNG をコミットする。
/// </summary>
internal static partial class Program
{
    private static int Main(string[] args)
    {
        var outputDirectory = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("src", "Resources", "Emoji"));
        Directory.CreateDirectory(outputDirectory);

        using var typeface = SKTypeface.FromFamilyName("Segoe UI Emoji");
        if (typeface == null || !typeface.FamilyName.Contains("Emoji", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Segoe UI Emoji が見つかりません。Windows 上で実行してください。");
            return 1;
        }

        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var glyph in EmojiRenderer.BakedGlyphs)
        {
            var name = EmojiRenderer.AssetKey(glyph) + ".png";
            File.WriteAllBytes(Path.Combine(outputDirectory, name), Render(glyph, typeface));
            written.Add(name);
        }

        // パレットから外した絵文字の PNG は置いていかない。消すのは自分が付ける
        // 名前（コードポイントを並べたもの）の PNG だけ。出力先を打ち間違えても
        // 無関係な画像を巻き込まないため。
        var removed = 0;
        foreach (var stale in Directory.EnumerateFiles(outputDirectory, "*.png")
                     .Where(path => !written.Contains(Path.GetFileName(path)) && IsAssetName(Path.GetFileName(path)))
                     .ToArray())
        {
            File.Delete(stale);
            removed++;
        }

        Console.WriteLine($"{written.Count} 個を書き出しました（削除 {removed} 個）: {outputDirectory}");
        return 0;
    }

    private static bool IsAssetName(string fileName)
        => AssetName().IsMatch(fileName);

    [GeneratedRegex(@"^[0-9a-f]{4,6}(-[0-9a-f]{4,6})*\.png$")]
    private static partial Regex AssetName();

    private static byte[] Render(string glyph, SKTypeface typeface)
    {
        var pixelSize = EmojiRenderer.PixelSize;
        using var bitmap = new SKBitmap(pixelSize, pixelSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var font = new SKFont(typeface, (float)EmojiRenderer.GlyphSize) { Subpixel = true };
        using var paint = new SKPaint { IsAntialias = true };
        font.MeasureText(glyph, out SKRect bounds, paint);
        var x = (pixelSize - bounds.Width) / 2 - bounds.Left;
        var y = (pixelSize - bounds.Height) / 2 - bounds.Top;
        canvas.DrawText(glyph, x, y, font, paint);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
