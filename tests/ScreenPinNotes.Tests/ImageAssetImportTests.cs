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
