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

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
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
    /// <paramref name="linkOverlay"/> のときは、エクスプローラーのショートカットと
    /// 同じ矢印をシェルに重ねてもらう。引けなければ null。
    /// </summary>
    public static ImageSource? Get(string path, bool isFolder = false, bool linkOverlay = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var extension = Path.GetExtension(path);
        // 拡張子だけで決まるものは、拡張子ごとに1つ覚える。ファイルが消えても
        // 同じアイコンを出せるし、同じ種類のファイルが並んでも1回で済む。
        var byExtension = !isFolder && extension.Length > 0 && !PerFileExtensions.Contains(extension);
        var key = byExtension ? extension : isFolder ? "\\folder" : Path.GetFullPath(path);
        // 矢印を重ねたものは別物として覚える。
        if (linkOverlay) key += "\\link";

        lock (Gate)
            if (Cache.TryGetValue(key, out var cached))
                return cached;

        var icon = Load(path, isFolder, linkOverlay,
            usesFileAttributes: byExtension || !File.Exists(path));
        lock (Gate)
            Cache[key] = icon;
        return icon;
    }

    /// <summary>覚えているアイコンを捨てる（見た目の設定が変わったときなど）。</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Cache.Clear();
            DisplayCache.Clear();
            _linkOverlay = null;
        }
    }

    // ─── 画面に出す大きさちょうどのアイコン ──────────────────────

    private static readonly Dictionary<string, ImageSource> DisplayCache = new(StringComparer.OrdinalIgnoreCase);
    private static BitmapSource? _linkOverlay;

    /// <summary>
    /// 画面に出す画素数ちょうどのアイコン。<see cref="Get"/> は32pxの絵を受け取って
    /// 縮めて使うので、細部（Word の「W」や PDF の文字）がにじむ。ここでは
    /// エクスプローラーと同じ <c>IShellItemImageFactory</c> に、必要な画素数を指定して
    /// 描いてもらう。アイコンだけを頼むので、サムネイルは作らない。
    /// </summary>
    /// <param name="pixelSize">表示する画素数（DIP ではなく、拡大率をかけたあと）。</param>
    /// <param name="linkOverlay">エクスプローラーのショートカットと同じ矢印を重ねる。</param>
    /// <remarks>
    /// 実在しないファイルはシェルが解釈できないので、覚えている同じ拡張子の絵か、
    /// 無ければ <see cref="Get"/> の絵（拡張子だけで引く）にする。
    /// </remarks>
    public static ImageSource? GetForDisplay(string path, bool isFolder, bool linkOverlay, int pixelSize)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        pixelSize = Math.Clamp(pixelSize, 16, 256);
        var extension = Path.GetExtension(path);
        var byExtension = !isFolder && extension.Length > 0 && !PerFileExtensions.Contains(extension);
        var key = (byExtension ? extension : Path.GetFullPath(path)) + "|" + pixelSize;
        var fullKey = linkOverlay ? key + "|link" : key;

        lock (Gate)
            if (DisplayCache.TryGetValue(fullKey, out var cached))
                return cached;

        BitmapSource? baseIcon;
        lock (Gate)
            baseIcon = DisplayCache.TryGetValue(key, out var plain) ? plain as BitmapSource : null;

        if (baseIcon == null)
        {
            var exists = isFolder ? Directory.Exists(path) : File.Exists(path);
            baseIcon = exists ? LoadExact(path, pixelSize) : null;
            // 描いてもらえたものだけを覚える。拡張子で引いた代用品を覚えてしまうと、
            // あとから同じ種類の実物が現れても、そちらの絵に切り替わらない。
            if (baseIcon != null)
                lock (Gate)
                    DisplayCache[key] = baseIcon;
        }

        // 実物が無い・描いてもらえない: 従来の絵で代用する（矢印もそちらで重ねる）。
        if (baseIcon == null)
            return Get(path, isFolder, linkOverlay);

        if (!linkOverlay)
            return baseIcon;

        var composed = WithLinkArrow(baseIcon, pixelSize) ?? baseIcon;
        lock (Gate)
            DisplayCache[fullKey] = composed;
        return composed;
    }

    private static BitmapSource? LoadExact(string path, int pixelSize)
    {
        IShellItemImageFactory? factory = null;
        var bitmap = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
            var hr = factory.GetImage(new NativeSize { Width = pixelSize, Height = pixelSize },
                SiigbfIconOnly, out bitmap);
            if (hr != 0 || bitmap == IntPtr.Zero)
                return null;

            // 透明を保ったまま受け取る（角の外は透明のまま）。
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException)
        {
            // 描いてもらえなければ、呼び出し側が従来の絵に切り替える。
            ErrorReporter.ReportNonFatal("Read a file icon at its display size", ex);
            return null;
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);
            if (factory != null)
                Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>
    /// 絵の左下に、エクスプローラーのショートカットの矢印を重ねる。矢印は
    /// シェルの「ショートカットの重ね絵」そのもの（自前で描いた偽物ではない）。
    /// </summary>
    private static BitmapSource? WithLinkArrow(BitmapSource baseIcon, int pixelSize)
    {
        var arrow = GetLinkOverlay();
        if (arrow == null)
            return null;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            var area = new Rect(0, 0, pixelSize, pixelSize);
            context.DrawImage(baseIcon, area);
            context.DrawImage(arrow, area);
        }

        var target = new RenderTargetBitmap(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static BitmapSource? GetLinkOverlay()
    {
        lock (Gate)
        {
            if (_linkOverlay != null)
                return _linkOverlay;
        }

        var info = new StockIconInfo { cbSize = (uint)Marshal.SizeOf<StockIconInfo>() };
        if (SHGetStockIconInfo(SiidLink, ShgsiIcon | ShgsiLargeIcon, ref info) != 0 || info.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            lock (Gate)
                _linkOverlay = source;
            return source;
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException)
        {
            ErrorReporter.ReportNonFatal("Read the shortcut overlay", ex);
            return null;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private static ImageSource? Load(string path, bool isFolder, bool linkOverlay, bool usesFileAttributes)
    {
        var info = default(ShFileInfo);
        var flags = ShgfiIcon | ShgfiLargeIcon;
        // 矢印はシェルに重ねてもらう。自前で描くより、エクスプローラーと同じ絵になる。
        if (linkOverlay)
            flags |= ShgfiLinkOverlay;
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
    private const uint ShgfiLinkOverlay = 0x000008000;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileAttributeDirectory = 0x00000010;
    private const int SiigbfIconOnly = 0x4;        // サムネイルは作らず、アイコンだけ
    private const int SiidLink = 29;               // SIID_LINK: ショートカットの重ね絵
    private const uint ShgsiIcon = 0x000000100;
    private const uint ShgsiLargeIcon = 0x000000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StockIconInfo
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szPath;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory factory);

    [DllImport("shell32.dll")]
    private static extern int SHGetStockIconInfo(int identifier, uint flags, ref StockIconInfo info);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr objectHandle);

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
