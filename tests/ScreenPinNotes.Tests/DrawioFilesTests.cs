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

using System.Buffers.Binary;
using System.IO;
using System.Text;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

/// <summary>
/// draw.io は図の XML を PNG の中に忍ばせて保存する。見た目はただの画像なので、
/// 中身の目印を見て「編集できる図面でもある」かを見分けられることを確かめる。
/// </summary>
public sealed class DrawioFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));

    public DrawioFilesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    // draw.io が書き出す PNG は、実物では zTXt の "mxGraphModel"、
    // エディタからの保存では tEXt の "mxfile" で図を持つ。
    [Theory]
    [InlineData("zTXt", "mxGraphModel")]
    [InlineData("tEXt", "mxfile")]
    [InlineData("iTXt", "mxfile")]
    public void IsDiagram_FindsTheDiagramHiddenInAPng(string chunkType, string keyword)
    {
        var path = WritePng("diagram.png", (chunkType, keyword));

        Assert.True(DrawioFiles.IsDiagram(path));
    }

    [Fact]
    public void IsDiagram_LeavesAnOrdinaryPngAlone()
        => Assert.False(DrawioFiles.IsDiagram(WritePng("photo.png")));

    /// <summary>ほかの用途の文字が入っているだけの PNG は図ではない。</summary>
    [Fact]
    public void IsDiagram_IgnoresOtherTextInAPng()
        => Assert.False(DrawioFiles.IsDiagram(WritePng("tagged.png", ("tEXt", "Comment"))));

    [Theory]
    [InlineData("chart.drawio")]
    [InlineData("chart.dio")]
    [InlineData("chart.drawio.xml")]
    [InlineData("chart.drawio.png")]
    public void IsDiagram_TrustsTheNameOfADrawioFile(string fileName)
    {
        var path = Path.Combine(_root, fileName);
        File.WriteAllText(path, "<mxfile></mxfile>");

        Assert.True(DrawioFiles.IsDiagram(path));
    }

    [Fact]
    public void IsDiagram_AnswersNoForSomethingItCannotRead()
    {
        Assert.False(DrawioFiles.IsDiagram(Path.Combine(_root, "missing.png")));
        Assert.False(DrawioFiles.IsDiagram(""));
        // PNG を名乗っていても中身が違えば図ではない。
        var fake = Path.Combine(_root, "fake.png");
        File.WriteAllText(fake, "not a png at all");
        Assert.False(DrawioFiles.IsDiagram(fake));
    }

    [Fact]
    public void FindExecutable_PrefersTheConfiguredPathAndRefusesAMissingOne()
    {
        var configured = Path.Combine(_root, "draw.io.exe");
        File.WriteAllText(configured, "");

        Assert.Equal(configured, DrawioFiles.FindExecutable(configured));
        Assert.Null(DrawioFiles.FindExecutable(Path.Combine(_root, "nowhere", "draw.io.exe")));
    }

    /// <summary>設定が空なら既定の場所を探す（入っていないPCでは null）。</summary>
    [Fact]
    public void FindExecutable_LooksInTheUsualPlacesWhenNothingIsConfigured()
    {
        var found = DrawioFiles.FindExecutable("");

        Assert.True(found == null || File.Exists(found));
    }

    [Fact]
    public void Defaults_LeaveTheDrawioPathToAutoDetection()
        => Assert.Equal("", new ScreenPinNotes.Models.AppSettings().DrawioPath);

    /// <summary>
    /// PNG を1枚書く。<paramref name="text"/> を渡すと、IHDR のうしろに
    /// その文字の塊を挟む（draw.io が図を入れるのと同じ場所）。
    /// CRC は読み取り側が見ないので 0 のままにしてある。
    /// </summary>
    private string WritePng(string fileName, (string Type, string Keyword)? text = null)
    {
        var path = Path.Combine(_root, fileName);
        using var stream = File.Create(path);
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(stream, "IHDR", new byte[13]);
        if (text is { } chunk)
        {
            var payload = Encoding.ASCII.GetBytes(chunk.Keyword + "\0<mxfile></mxfile>");
            WriteChunk(stream, chunk.Type, payload);
        }
        WriteChunk(stream, "IDAT", new byte[8]);
        WriteChunk(stream, "IEND", []);
        return path;
    }

    private static void WriteChunk(Stream stream, string type, byte[] payload)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)payload.Length);
        stream.Write(length);
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(payload);
        stream.Write(new byte[4]);
    }
}
