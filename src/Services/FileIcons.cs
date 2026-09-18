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
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenPinNotes.Services;

/// <summary>
/// 付箋に置いたファイルの、エクスプローラーと同じアイコンを返す。
/// サムネイル（PDFの1ページ目など）は作らない。アイコンだけなら
/// <see cref="SHGetFileInfoW"/> で速く、ファイルが無くても拡張子から引ける。
/// </summary>
/// <remarks>
/// 付箋は出しっぱなしのアプリなので、取り込んだアイコンは必ず
/// DestroyIcon で返し、取り出した ImageSource は Freeze して使い回す。
/// </remarks>
public static class FileIcons
{
    // 中身ごとにアイコンが違うもの。拡張子でまとめず、ファイルごとに覚える。
    private static readonly HashSet<string> PerFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".ico", ".cur", ".url", ".msc", ".cpl", ".scr",
    };

    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    /// <summary>
    /// <paramref name="path"/> のアイコン。ファイルが無いときも拡張子から引く
    /// （リンク先が消えた付箋でも、何のファイルだったかは見せられる）。
    /// 引けなければ null。
    /// </summary>
    public static ImageSource? Get(string path, bool isFolder = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var extension = Path.GetExtension(path);
        // 拡張子ごとに覚えるものは、ファイルが消えても同じアイコンが出せる。
        var key = isFolder ? "\\folder"
            : extension.Length == 0 || PerFileExtensions.Contains(extension)
                ? Path.GetFullPath(path)
                : extension;

        lock (Gate)
            if (Cache.TryGetValue(key, out var cached))
                return cached;

        var icon = Load(path, isFolder, usesFileAttributes: key == extension || !File.Exists(path));
        lock (Gate)
            Cache[key] = icon;
        return icon;
    }

    /// <summary>覚えているアイコンを捨てる（見た目の設定が変わったときなど）。</summary>
    public static void Clear()
    {
        lock (Gate)
            Cache.Clear();
    }

    private static ImageSource? Load(string path, bool isFolder, bool usesFileAttributes)
    {
        var info = default(ShFileInfo);
        var flags = ShgfiIcon | ShgfiLargeIcon;
        var attributes = 0u;
        if (usesFileAttributes)
        {
            // 実物を見に行かせない。ネットワーク上のファイルでも待たされず、
            // 消えたファイルの拡張子からでもアイコンが引ける。
            flags |= ShgfiUseFileAttributes;
            attributes = isFolder ? FileAttributeDirectory : FileAttributeNormal;
        }

        var handle = IntPtr.Zero;
        try
        {
            if (SHGetFileInfoW(path, attributes, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags) == IntPtr.Zero)
                return null;

            handle = info.hIcon;
            if (handle == IntPtr.Zero)
                return null;

            var source = Imaging.CreateBitmapSourceFromHIcon(
                handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException)
        {
            ErrorReporter.ReportNonFatal("Read a file icon", ex);
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero)
                DestroyIcon(handle);
        }
    }

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileAttributeDirectory = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string path, uint fileAttributes,
        ref ShFileInfo fileInfo, uint fileInfoSize, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
