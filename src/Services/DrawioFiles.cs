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

using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace ScreenPinNotes.Services;

/// <summary>
/// draw.io で編集できるファイルかを見分け、draw.io 本体の居場所を探す。
/// </summary>
/// <remarks>
/// draw.io は図の XML を PNG の中に忍ばせて保存できる（「編集可能な PNG」）。
/// 見た目はただの画像なので付箋にそのまま貼れて、draw.io で開けばまた編集でき、
/// 保存すると同じ PNG が描き直される。付箋にとっては画像のまま図面でもある、
/// という状態なので、拡張子ではなく中身を見て判断する。
/// </remarks>
public static class DrawioFiles
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>draw.io が図として開ける、名前で分かるもの。</summary>
    private static readonly string[] DiagramSuffixes =
        [".drawio", ".dio", ".drawio.xml", ".drawio.png", ".drawio.svg"];

    /// <summary>PNG に忍ばせた図の目印。draw.io はこの名前で XML を書き込む。</summary>
    private static readonly string[] DiagramKeywords = ["mxfile", "mxGraphModel"];

    /// <summary>
    /// 見出しの読み飛ばしを打ち切る長さ。目印は画像データより前に来るので、
    /// 大きな PNG でも先頭のいくつかの塊を見れば足りる。
    /// </summary>
    private const int MaxChunksProbed = 64;

    /// <summary>draw.io で開けるファイルか。PNG は中身に図が入っているかを見る。</summary>
    public static bool IsDiagram(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var name = Path.GetFileName(path);
        foreach (var suffix in DiagramSuffixes)
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;

        return name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && PngHasDiagram(path);
    }

    /// <summary>PNG の中に draw.io の図が入っているか。</summary>
    public static bool PngHasDiagram(string path)
    {
        try
        {
            // 書き手（draw.io）が開いたままでも読めるよう、共有して開く。
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var header = new byte[8];
            if (stream.Read(header, 0, 8) != 8 || !header.AsSpan().SequenceEqual(PngSignature))
                return false;

            var chunkHeader = new byte[8];
            for (var chunk = 0; chunk < MaxChunksProbed; chunk++)
            {
                if (stream.Read(chunkHeader, 0, 8) != 8)
                    return false;

                var length = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader.AsSpan(0, 4));
                var type = Encoding.ASCII.GetString(chunkHeader, 4, 4);
                // 画像データまで来たら、目印はもう無い。
                if (type == "IDAT" || type == "IEND")
                    return false;

                if (type is "tEXt" or "zTXt" or "iTXt" && HasDiagramKeyword(stream, length))
                    return true;

                // 塊の中身と、そのうしろの CRC 4バイトを飛ばす。
                stream.Seek(length + 4L, SeekOrigin.Current);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            // 読めないファイルは「図ではない」として黙って扱う。メニューに
            // 項目が出ないだけで、画像としての表示は今までどおり。
        }

        return false;
    }

    /// <summary>
    /// 文字の塊の名前だけを読む。名前は最初の 0 バイトまでで、
    /// draw.io のものは十数バイトしかない。読んだぶんは呼び出し側で数え直さず、
    /// 塊の先頭へ戻してから飛ばす。
    /// </summary>
    private static bool HasDiagramKeyword(FileStream stream, uint length)
    {
        var start = stream.Position;
        try
        {
            var probe = new byte[(int)Math.Min(length, 32u)];
            var read = stream.Read(probe, 0, probe.Length);
            var end = Array.IndexOf(probe, (byte)0, 0, read);
            if (end < 0)
                return false;

            var keyword = Encoding.ASCII.GetString(probe, 0, end);
            foreach (var candidate in DiagramKeywords)
                if (string.Equals(keyword, candidate, StringComparison.Ordinal))
                    return true;

            return false;
        }
        finally
        {
            stream.Seek(start, SeekOrigin.Begin);
        }
    }

    /// <summary>
    /// draw.io 本体の場所。設定に書かれていればそれを、無ければ既定の場所を探す。
    /// 見つからなければ null。
    /// </summary>
    public static string? FindExecutable(string? configuredPath)
    {
        var configured = (configuredPath ?? "").Trim();
        if (configured.Length > 0)
            return File.Exists(configured) ? configured : null;

        foreach (var candidate in DefaultInstallPaths())
        {
            try
            {
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // 読めない場所は次の候補へ。
            }
        }

        return null;
    }

    private static IEnumerable<string> DefaultInstallPaths()
    {
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                 })
        {
            if (!string.IsNullOrEmpty(folder))
                yield return Path.Combine(folder, "draw.io", "draw.io.exe");
        }
    }
}
