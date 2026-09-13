using System.IO;
using System.Windows.Documents;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class ObsidianTests
{
    [WpfTheory]
    [InlineData("---\ntitle: 公演\n---\n\n  \n# 東京公演\n本文", "# 東京公演")]
    [InlineData("\uFEFF---\r\ntags: [live, music]\r\n...\r\n\r\n東京公演", "東京公演")]
    [InlineData("---\ntitle: 公演\n---\n\n", "")]
    [InlineData("\n\n通常の本文\n次の行", "通常の本文")]
    [InlineData("---\nbroken: [\n---\n本文", "---")]
    [InlineData("---\ntitle: unclosed\n本文", "---")]
    public void FoldedPreviewSkipsPropertiesAndBlankLines(string source, string expected)
    {
        var method = typeof(ScreenPinNotes.Views.StickyNoteWindow).GetMethod(
            "GetFoldedPreviewSource", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal(expected, method.Invoke(null, new object[] { source }));
    }

    [WpfFact]
    public void PropertiesPreserveNestedValuesAndSourceLineNumbers()
    {
        var source = "---\ntitle: \"東京: 公演\"\nstart: '17:00'\nstay:\n  name: ホテル\nflights:\n  - route: 札幌→東京\n    depart: '09:25'\ntags: [live, music]\n---\n- [ ] 確認\n![[写真.png|320x200]]";
        var tasks = new List<int>();
        var images = new List<MarkdownRenderer.MarkdownImage>();
        var blocks = MarkdownRenderer.Render(source, 14, (text, target) => new Hyperlink(new Run(text)),
            image => { images.Add(image); return new Run(image.Target); },
            (line, state) => { tasks.Add(line); return new System.Windows.Controls.CheckBox(); }).ToArray();
        var section = Assert.IsType<Section>(blocks[0]);
        var table = Assert.IsType<Table>(section.Blocks.LastBlock);
        var values = table.RowGroups[0].Rows.Select(row => string.Join("=", row.Cells.Select(cell => new TextRange(cell.ContentStart, cell.ContentEnd).Text.Trim()))).ToArray();
        Assert.Contains("title=東京: 公演", values);
        Assert.Contains("start=17:00", values);
        Assert.Contains("stay › name=ホテル", values);
        Assert.Contains("flights [1] › depart=09:25", values);
        Assert.Contains("tags [2]=music", values);
        Assert.Equal(10, Assert.Single(tasks));
        var image = Assert.Single(images);
        Assert.Equal(11, image.LineIndex);
        Assert.Equal("写真.png", image.Target);
        Assert.Equal(320, image.Width);
        Assert.Equal(200, image.Height);
        Assert.Equal("![[写真.png|320x200]]", source.Split('\n')[image.LineIndex].Substring(image.Start, image.Length));
    }

    [WpfTheory]
    [InlineData("---\nbroken: [\n---\n本文")]
    [InlineData("---\ntitle: unclosed\n本文")]
    [InlineData("---\na: &a [*a]\n---\n本文")]
    public void InvalidOrRecursivePropertiesFallBackToSource(string text)
    {
        Assert.False(MarkdownProperties.TryRender(text.Split('\n'), false, out _, out _));
    }

    [WpfFact]
    public void WikiEmbedsRespectCodeAndEscaping()
    {
        var images = new List<MarkdownRenderer.MarkdownImage>();
        _ = MarkdownRenderer.Render("`![[code.png]]` \\![[escaped.png]] ![[note.md]] ![[写真.png]]", 14,
            (text, target) => new Hyperlink(new Run(text)), image => { images.Add(image); return new Run(image.Target); }).ToArray();
        Assert.Equal("写真.png", Assert.Single(images).Target);
        Assert.Equal("写真.png", MarkdownRenderer.GetImageOnlyTarget("![[写真.png|300]]"));
    }

    [Theory]
    [InlineData("アタッチ")]
    [InlineData("./添付")]
    [InlineData(".")]
    public void ResolvesConfiguredAttachmentsWithoutFolderNameAssumptions(string folder)
    {
        using var vault = new Vault();
        File.WriteAllText(Path.Combine(vault.Root, ".obsidian", "app.json"), System.Text.Json.JsonSerializer.Serialize(new { attachmentFolderPath = folder }));
        var baseFolder = folder.StartsWith('.') ? vault.Notes : vault.Root;
        var directory = Path.GetFullPath(Path.Combine(baseFolder, folder));
        Directory.CreateDirectory(directory);
        var image = Path.Combine(directory, "写真 1.png");
        File.WriteAllText(image, "test fixture");
        Assert.Equal(image, new ObsidianAttachments(vault.Note).Resolve("写真 1.png"));
        Assert.Equal(image, new ObsidianAttachments(vault.Note).Resolve("写真%201.png"));
    }

    [Fact]
    public void VaultSearchRejectsAmbiguityAndDoesNotEscapeVault()
    {
        using var vault = new Vault();
        foreach (var name in new[] { "a", "b" })
        {
            Directory.CreateDirectory(Path.Combine(vault.Root, name));
            File.WriteAllText(Path.Combine(vault.Root, name, "same.png"), "test fixture");
        }
        var resolver = new ObsidianAttachments(vault.Note);
        Assert.Null(resolver.Resolve("same.png"));
        Assert.Equal(Path.Combine(vault.Root, "a", "same.png"), resolver.Resolve("a/same.png"));
        Assert.Null(resolver.Resolve("../../outside.png"));
        Assert.Null(new ObsidianAttachments(Path.Combine(Path.GetTempPath(), "no-vault.md")).Resolve("same.png"));
    }

    [WpfFact]
    public void ExternalNoteRendersPropertiesAndAttachmentWithoutChangingFile()
    {
        WpfApplicationFixture.Ensure();
        using var vault = new Vault();
        var attachmentFolder = Path.Combine(vault.Root, "アタッチ");
        Directory.CreateDirectory(attachmentFolder);
        File.WriteAllText(Path.Combine(vault.Root, ".obsidian", "app.json"), "{\"attachmentFolderPath\":\"アタッチ\"}");
        var imagePath = Path.Combine(attachmentFolder, "会場.png");
        var pixels = new byte[320 * 100 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 150; pixels[i + 1] = 100; pixels[i + 2] = 35; pixels[i + 3] = 255; }
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(320, 100, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 320 * 4);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var stream = File.Create(imagePath)) encoder.Save(stream);
        var source = "---\ntype: live\nartist: yosugala\ntitle: FANCLUB LIVE TOUR 2026 東京\ndate: 2026-03-29\nopen: '16:00'\nstart: '17:00'\nvenue: 恵比寿LIQUIDROOM\nstay:\n  name: 東横INN池袋北口1\n  access: JR池袋駅から徒歩4分\nflights:\n  - route: 札幌→東京\n    depart: '09:25'\n  - route: 東京→札幌\n    depart: '17:50'\n---\n# 会場情報\n添付フォルダの画像\n![[会場.png]]";
        File.WriteAllText(vault.Note, source);
        var note = new ScreenPinNotes.Models.StickyNote { Width = 650, Height = 900,
            ExternalContentPath = vault.Note, Content = source, Title = "Obsidian 表示確認" };
        var settings = new ScreenPinNotes.Models.AppSettings { Theme = "Dark", Language = "ja" };
        var window = new ScreenPinNotes.Views.StickyNoteWindow(new ScreenPinNotes.ViewModels.StickyNoteViewModel(note, settings), new StorageService(Path.Combine(vault.Root, "test-storage")));
        try
        {
            window.Show(); window.UpdateLayout();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var images = (System.Collections.IDictionary)window.GetType().GetField("_markdownImageContexts", flags)!.GetValue(window)!;
            Assert.Single(images.Keys.Cast<object>());
            Assert.Equal(source, File.ReadAllText(vault.Note));
            Assert.Equal(source, note.Content);
            if (Environment.GetEnvironmentVariable("SCREENPINNOTES_OBSIDIAN_PREVIEW") is { Length: > 0 } output)
            {
                var visual = (System.Windows.FrameworkElement)window.Content;
                var preview = new System.Windows.Media.Imaging.RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                preview.Render(visual);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(preview));
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                using var file = File.Create(output);
                png.Save(file);
            }
        }
        finally { window.Close(); }
    }

    private sealed class Vault : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        public string Notes => Path.Combine(Root, "予定");
        public string Note => Path.Combine(Notes, "note.md");
        public Vault() { Directory.CreateDirectory(Notes); Directory.CreateDirectory(Path.Combine(Root, ".obsidian")); }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
