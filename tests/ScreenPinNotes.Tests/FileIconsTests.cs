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

    [WpfFact]
    public void Get_AnswersNothingForAnEmptyPath()
        => Assert.Null(FileIcons.Get("   "));
}
