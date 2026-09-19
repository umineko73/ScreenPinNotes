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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

/// <summary>
/// 付箋に置いたファイルのアイコン。シェルから取り込むので WPF のスレッドで動かす。
/// </summary>
public class FileIconsTests
{
    [WpfFact]
    public void Get_ReturnsAFrozenIconForACommonExtension()
    {
        var icon = FileIcons.Get(Path.Combine(Path.GetTempPath(), "sample.txt"));

        Assert.NotNull(icon);
        // 付箋ごとに使い回すので、凍らせて別スレッドからでも触れるようにしてある。
        Assert.True(icon!.IsFrozen);
    }

    /// <summary>
    /// リンク先が消えていても、拡張子から何のファイルだったかは見せられる。
    /// </summary>
    [WpfFact]
    public void Get_StillAnswersForAFileThatIsNotThere()
        => Assert.NotNull(FileIcons.Get(Path.Combine(Path.GetTempPath(), "no-such-file-9d3f.pdf")));

    /// <summary>拡張子ごとに覚えるので、同じ種類なら同じものが返る。</summary>
    [WpfFact]
    public void Get_ReusesOneIconPerExtension()
    {
        var first = FileIcons.Get(Path.Combine(Path.GetTempPath(), "one.rtf"));
        var second = FileIcons.Get(Path.Combine(Path.GetTempPath(), "another.rtf"));

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    /// <summary>
    /// 元の場所を指すだけの札には、エクスプローラーと同じ矢印を重ねる。
    /// 重ねたものと重ねていないものは別物として覚える。
    /// </summary>
    [WpfFact]
    public void Get_KeepsTheShortcutArrowVersionApart()
    {
        var path = Path.Combine(Path.GetTempPath(), "linked.rtf");

        var plain = FileIcons.Get(path);
        var linked = FileIcons.Get(path, linkOverlay: true);

        Assert.NotNull(plain);
        Assert.NotNull(linked);
        Assert.NotSame(plain, linked);
        // それぞれは覚えたものを使い回す。
        Assert.Same(linked, FileIcons.Get(path, linkOverlay: true));
        Assert.Same(plain, FileIcons.Get(path));
    }

    // ─── 画面に出す大きさちょうどのアイコン ──────────────────────

    /// <summary>
    /// 縮めて使うとにじむので、必要な画素数ちょうどでシェルに描いてもらう。
    /// 返る絵の大きさが、頼んだ画素数と一致する。
    /// </summary>
    [WpfTheory]
    [InlineData(16)]
    [InlineData(22)]
    [InlineData(33)]
    [InlineData(48)]
    public void GetForDisplay_ReturnsTheRequestedPixelSize(int pixels)
    {
        using var dir = new TempFolder();
        var file = dir.Create("sample.txt");

        var icon = Assert.IsAssignableFrom<BitmapSource>(FileIcons.GetForDisplay(file, false, false, pixels));

        Assert.Equal(pixels, icon.PixelWidth);
        Assert.Equal(pixels, icon.PixelHeight);
        Assert.True(icon.IsFrozen);
    }

    /// <summary>同じ種類のファイルは1回だけ描いてもらい、大きさが違えば別物として覚える。</summary>
    [WpfFact]
    public void GetForDisplay_ReusesOneIconPerExtensionAndSize()
    {
        using var dir = new TempFolder();
        var first = FileIcons.GetForDisplay(dir.Create("one.rtf"), false, false, 22);
        var second = FileIcons.GetForDisplay(dir.Create("another.rtf"), false, false, 22);
        var larger = FileIcons.GetForDisplay(dir.Create("one.rtf"), false, false, 33);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.NotSame(first, larger);
    }

    /// <summary>
    /// 矢印を重ねた絵と重ねない絵は別物。矢印はシェルの重ね絵そのもので、
    /// 左下に描かれる（ほかの場所は変わらない）。
    /// </summary>
    [WpfFact]
    public void GetForDisplay_AddsTheShortcutArrowInTheBottomLeftOnly()
    {
        using var dir = new TempFolder();
        var file = dir.Create("linked.rtf");

        var plain = Assert.IsAssignableFrom<BitmapSource>(FileIcons.GetForDisplay(file, false, false, 32));
        var linked = Assert.IsAssignableFrom<BitmapSource>(FileIcons.GetForDisplay(file, false, true, 32));

        Assert.NotSame(plain, linked);
        Assert.Same(linked, FileIcons.GetForDisplay(file, false, true, 32));
        Assert.Equal(plain.PixelWidth, linked.PixelWidth);

        // 重ね合わせで、絵のほかの場所にも ±1 ほどの丸め誤差が出る。矢印による
        // 変化と区別するため、目に見える差（8を超える）だけを数える。
        var before = Pixels(plain);
        var after = Pixels(linked);
        var changedBottomLeft = 0;
        var changedElsewhere = 0;
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                var i = (y * 32 + x) * 4;
                var visible = Enumerable.Range(0, 4).Any(c => Math.Abs(before[i + c] - after[i + c]) > 8);
                if (!visible) continue;
                if (x < 18 && y >= 16) changedBottomLeft++;
                else changedElsewhere++;
            }
        }

        Assert.True(changedBottomLeft > 20, $"only {changedBottomLeft} pixels changed in the bottom left");
        Assert.Equal(0, changedElsewhere);
    }

    private static byte[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(bytes, converted.PixelWidth * 4, 0);
        return bytes;
    }

    /// <summary>
    /// 消えたファイルは、シェルに描いてもらえないので従来の絵で代用する。
    /// 代用品を覚えてしまうと、あとから同じ種類の実物が現れても切り替わらない。
    /// </summary>
    [WpfFact]
    public void GetForDisplay_FallsBackForAMissingFileWithoutRememberingTheStandIn()
    {
        using var dir = new TempFolder();

        var standIn = FileIcons.GetForDisplay(Path.Combine(dir.Path, "gone.rtfd"), false, false, 24);
        Assert.NotNull(standIn);

        var real = Assert.IsAssignableFrom<BitmapSource>(
            FileIcons.GetForDisplay(dir.Create("here.rtfd"), false, false, 24));
        Assert.Equal(24, real.PixelWidth);

        // 実物で描いてもらった絵は覚えられ、消えた同じ種類のファイルにも使われる。
        Assert.Same(real, FileIcons.GetForDisplay(Path.Combine(dir.Path, "gone2.rtfd"), false, false, 24));
    }

    [WpfFact]
    public void GetForDisplay_DrawsAFolderToo()
    {
        using var dir = new TempFolder();

        var icon = Assert.IsAssignableFrom<BitmapSource>(FileIcons.GetForDisplay(dir.Path, true, false, 24));

        Assert.Equal(24, icon.PixelWidth);
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));

        public TempFolder() => Directory.CreateDirectory(Path);

        public string Create(string name)
        {
            var file = System.IO.Path.Combine(Path, name);
            File.WriteAllText(file, "x");
            return file;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }

    [WpfFact]
    public void Get_AnswersNothingForAnEmptyPath()
        => Assert.Null(FileIcons.Get("   "));
}
