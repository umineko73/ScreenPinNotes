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
using System.Text;

namespace ScreenPinNotes.Services;

/// <summary>
/// 付箋にドロップ・貼り付けされた画像ファイルを、付箋の assets フォルダへコピーする。
/// 元のファイルは移動も変換もしない（ダウンロードや Obsidian の保管庫から入れても元の場所に残る）。
/// </summary>
public static class ImageAssetImport
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>付箋に表示できる拡張子を持ち、実在するファイルだけを重複なく返す。</summary>
    public static IReadOnlyList<string> GetImageFiles(IEnumerable<string>? paths)
    {
        if (paths == null)
            return [];

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path) &&
                           LinkDetector.IsRenderableImageTarget(path) &&
                           File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// <paramref name="sourcePath"/> を assets フォルダへコピーし、コピー先のファイル名を返す。
    /// 同名のファイルがあれば "名前-2.png" のように番号を付ける。すでにその assets フォルダにある
    /// ファイルはコピーせず、そのまま参照する。
    /// </summary>
    public static string CopyIntoAssets(string sourcePath, string assetsDirectory)
    {
        var source = Path.GetFullPath(sourcePath);
        var assets = Path.GetFullPath(assetsDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var sourceName = Path.GetFileName(source);
        var name = ToSafeFileName(sourceName);
        if (string.Equals(Path.GetDirectoryName(source), assets, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(name, sourceName, StringComparison.Ordinal))
        {
            return sourceName;
        }

        Directory.CreateDirectory(assets);
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var number = 1; ; number++)
        {
            var candidate = number == 1 ? name : $"{stem}-{number}{extension}";
            var destination = Path.Combine(assets, candidate);
            try
            {
                File.Copy(source, destination, overwrite: false);
                return candidate;
            }
            catch (IOException) when (File.Exists(destination))
            {
                // 同名のファイルがある。次の番号を試す。
            }
        }
    }

    /// <summary>
    /// Markdown の画像記法にそのまま書けるファイル名にする。文字・数字・"-"・"_"・"." 以外
    /// （空白や括弧など）は "-" にまとめ、日本語などの文字はそのまま残す。
    /// </summary>
    public static string ToSafeFileName(string fileName)
    {
        var extension = new string(Path.GetExtension(fileName).Where(char.IsLetterOrDigit).ToArray());
        var builder = new StringBuilder();
        foreach (var ch in Path.GetFileNameWithoutExtension(fileName))
        {
            var safe = char.IsLetterOrDigit(ch) || ch is '_' or '.' ? ch : '-';
            if (safe == '-' && builder.Length > 0 && builder[^1] == '-')
                continue;
            builder.Append(safe);
        }

        var stem = builder.ToString().Trim('-', '.');
        if (stem.Length == 0)
            stem = "image";
        else if (ReservedDeviceNames.Contains(stem))
            stem = "image-" + stem;

        return extension.Length == 0 ? stem : $"{stem}.{extension}";
    }
}
