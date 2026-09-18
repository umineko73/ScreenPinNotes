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

public sealed class ImageAssetImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));

    public ImageAssetImportTests() => Directory.CreateDirectory(SourceDirectory);

    private string SourceDirectory => Path.Combine(_root, "source");

    private string AssetsDirectory => Path.Combine(_root, "assets");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void GetImageFiles_KeepsExistingImageFilesOnce()
    {
        var png = CreateSourceFile("a.png");
        var jpg = CreateSourceFile("b.JPG");
        var text = CreateSourceFile("c.txt");

        var files = ImageAssetImport.GetImageFiles(
            [png, jpg, text, Path.Combine(SourceDirectory, "missing.png"), png.ToUpperInvariant(), ""]);

        Assert.Equal([png, jpg], files);
    }

    [Theory]
    [InlineData("旅行 写真 (1).PNG", "旅行-写真-1.PNG")]
    [InlineData("[draft] #2.jpg", "draft-2.jpg")]
    [InlineData("a.b_c.webp", "a.b_c.webp")]
    [InlineData("  .png", "image.png")]
    [InlineData("CON.png", "image-CON.png")]
    public void ToSafeFileName_KeepsNamesReadableAndMarkdownSafe(string fileName, string expected)
        => Assert.Equal(expected, ImageAssetImport.ToSafeFileName(fileName));

    /// <summary>
    /// 付箋に落とせるのは画像だけではない。実在するファイルとフォルダーを、
    /// 落とされた順のまま重複なく返す。
    /// </summary>
    [Fact]
    public void GetDroppedPaths_KeepsExistingFilesAndFoldersOnce()
    {
        var pdf = CreateSourceFile("a.pdf");
        var png = CreateSourceFile("b.png");
        var folder = Path.Combine(SourceDirectory, "folder");
        Directory.CreateDirectory(folder);

        var paths = ImageAssetImport.GetDroppedPaths(
            [pdf, png, folder, Path.Combine(SourceDirectory, "missing.pdf"), pdf.ToUpperInvariant(), ""]);

        Assert.Equal([pdf, png, folder], paths);
    }

    [Theory]
    [InlineData("資料.pdf", "assets/資料.pdf", "![資料.pdf](assets/資料.pdf)")]
    [InlineData("見積 書.xlsx", "D:\\work\\見積 書.xlsx", "![見積 書.xlsx](<D:\\work\\見積 書.xlsx>)")]
    [InlineData("a(1).zip", "D:\\work\\a(1).zip", "![a(1).zip](<D:\\work\\a(1).zip>)")]
    [InlineData("[下書き].docx", "assets/-下書き-.docx", "![](assets/-下書き-.docx)")]
    public void BuildFileMarkdown_WrapsAwkwardTargetsInAngleBrackets(
        string displayName, string target, string expected)
        => Assert.Equal(expected, ImageAssetImport.BuildFileMarkdown(displayName, target));

    [Theory]
    [InlineData(@"\\server\share\file.pdf")]
    [InlineData(@"C:\work\_draft.txt")]
    [InlineData(@"C:\work\[draft].txt")]
    [InlineData(@"C:\")]
    public void BuildFileMarkdown_PreservesWindowsPathsWhenParsed(string target)
    {
        var markdown = ImageAssetImport.BuildFileMarkdown("file", target);
        Assert.Equal(target, MarkdownRenderer.GetImageOnlyTarget(markdown));
    }

    [Theory]
    [InlineData("  .pdf", "file.pdf")]
    [InlineData("NUL.zip", "file-NUL.zip")]
    public void ToSafeFileName_UsesTheGivenFallbackForFilesThatAreNotImages(string fileName, string expected)
        => Assert.Equal(expected, ImageAssetImport.ToSafeFileName(fileName, "file"));

    [Fact]
    public void CopyIntoAssets_CopiesWithoutTouchingTheOriginal()
    {
        var source = CreateSourceFile("photo.png", [1, 2, 3, 4]);

        var name = ImageAssetImport.CopyIntoAssets(source, AssetsDirectory);

        Assert.Equal("photo.png", name);
        Assert.True(File.Exists(source));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(Path.Combine(AssetsDirectory, name)));
    }

    [Fact]
    public void CopyIntoAssets_NumbersNameCollisions()
    {
        var source = CreateSourceFile("photo.png");

        Assert.Equal("photo.png", ImageAssetImport.CopyIntoAssets(source, AssetsDirectory));
        Assert.Equal("photo-2.png", ImageAssetImport.CopyIntoAssets(source, AssetsDirectory));
        Assert.Equal("photo-3.png", ImageAssetImport.CopyIntoAssets(source, AssetsDirectory));
        Assert.Equal(3, Directory.GetFiles(AssetsDirectory).Length);
    }

    [Fact]
    public void CopyIntoAssets_ReusesAFileAlreadyInThatAssetsFolder()
    {
        Directory.CreateDirectory(AssetsDirectory);
        var existing = Path.Combine(AssetsDirectory, "photo.png");
        File.WriteAllBytes(existing, [1]);

        Assert.Equal("photo.png", ImageAssetImport.CopyIntoAssets(existing, AssetsDirectory));
        Assert.Single(Directory.GetFiles(AssetsDirectory));
    }

    // 見つからないキーはキー名がそのまま返るので、両方の .resx に文言があることの確認になる。
    [Theory]
    [InlineData("ja", "画像ファイルを追加できませんでした")]
    [InlineData("en", "Could not add the image file")]
    public void ImageImportFailedIsInTheCatalog(string culture, string expected)
        => Assert.Equal(expected, LocalizationService.T("ImageImportFailed", culture));

    private string CreateSourceFile(string name, byte[]? bytes = null)
    {
        var path = Path.Combine(SourceDirectory, name);
        File.WriteAllBytes(path, bytes ?? [0]);
        return path;
    }
}
