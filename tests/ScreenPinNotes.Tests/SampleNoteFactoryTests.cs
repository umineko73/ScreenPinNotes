using System.IO;
using System.Globalization;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

// SampleNoteFactory reads from AppContext.BaseDirectory\SampleNotes, which MSBuild
// copies into this test project's own output too (ProjectReference to
// src/ScreenPinNotes.csproj carries its CopyToOutputDirectory items). So unlike
// a bare "exe copied out on its own" scenario, SampleNotes is actually present here
// -- these tests cover the real load path instead.
public class SampleNoteFactoryTests
{
    [Fact]
    public void CreateInitialNotes_Japanese_LoadsBothSamplesWithExpectedMetadata()
    {
        var settings = new AppSettings { Language = "ja" };
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);

        var notes = SampleNoteFactory.CreateInitialNotes(settings, storage);

        Assert.Equal(2, notes.Count);

        var markdownNote = notes[0];
        Assert.Equal("Markdown 書式一覧", markdownNote.Title);
        Assert.Equal("sky", markdownNote.ColorKey);
        Assert.Equal("📝", markdownNote.Icon);
        Assert.Equal(645, markdownNote.Width);
        Assert.True(markdownNote.IsReadOnly);
        Assert.False(string.IsNullOrWhiteSpace(markdownNote.Content));
        Assert.Contains("## インライン記法", markdownNote.Content);
        Assert.Contains("## ブロック記法", markdownNote.Content);
        Assert.Contains("タイトルバー上で `Ctrl + マウスホイール`", markdownNote.Content);

        var usageNote = notes[1];
        Assert.Equal("操作ヘルプ", usageNote.Title);
        Assert.Equal("yellow", usageNote.ColorKey);
        Assert.Equal("💡", usageNote.Icon);
        Assert.Equal(585, usageNote.Width);
        Assert.Equal(825, usageNote.X);
        Assert.True(usageNote.IsReadOnly);
        Assert.False(string.IsNullOrWhiteSpace(usageNote.Content));
        Assert.Contains("assets/window-position-guide.png", usageNote.Content);
        Assert.Contains("## 移動とスナップ", usageNote.Content);
        Assert.Contains("`Ctrl + ドラッグ`", usageNote.Content);
        Assert.Contains("`Ctrl + Alt + ドラッグ`", usageNote.Content);
        Assert.Contains("## 外部ファイル付箋", usageNote.Content);
        Assert.Contains("付箋を削除（元のファイルは残す）", usageNote.Content);
        Assert.Contains("画像サイズ変更", usageNote.Content);
        Assert.Contains("## リマインダー", usageNote.Content);
        Assert.Contains("スヌーズ", usageNote.Content);
    }

    [Fact]
    public void CreateInitialNotes_English_LoadsEnglishSamples()
    {
        var settings = new AppSettings { Language = "en" };
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);

        var notes = SampleNoteFactory.CreateInitialNotes(settings, storage);

        Assert.Equal(2, notes.Count);
        Assert.Equal("Markdown Syntax List", notes[0].Title);
        Assert.Equal("Usage Help", notes[1].Title);
        Assert.All(notes, note => Assert.True(note.IsReadOnly));
        Assert.Contains("This note lists the Markdown syntax supported in the body.", notes[0].Content);
        Assert.Contains("Double-click the body", notes[1].Content);
        Assert.Contains("Ctrl + drag", notes[1].Content);
        Assert.Contains("Ctrl + Alt + drag", notes[1].Content);
        Assert.Contains("## External-File Notes", notes[1].Content);
        Assert.Contains("Delete note (keep original file)", notes[1].Content);
        Assert.Contains("Image size changes", notes[1].Content);
        Assert.Contains("## Reminders", notes[1].Content);
        Assert.Contains("snooze", notes[1].Content);
        Assert.Contains("assets/window-position-guide.png", notes[1].Content);
    }

    [Fact]
    public void CreateInitialNotes_DefaultSettingsOnNonJapaneseCulture_LoadsAndCopiesEnglishSamples()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            using var temp = new TempDataDirectory();
            var storage = new StorageService(temp.Path);
            var settings = AppSettings.CreateDefault();

            var notes = SampleNoteFactory.CreateInitialNotes(settings, storage);

            Assert.Equal(2, notes.Count);
            Assert.Equal("Markdown Syntax List", notes[0].Title);
            Assert.Equal("Usage Help", notes[1].Title);
            Assert.Contains("This note lists the Markdown syntax supported in the body.", notes[0].Content);
            Assert.Contains("Double-click the body", notes[1].Content);
            Assert.True(File.Exists(Path.Combine(
                storage.GetNoteAssetsDirectoryPath(notes[1].Id),
                "window-position-guide.png")));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void CreateInitialNotes_AssignsFreshIdsAndIncreasingCreatedAt()
    {
        var settings = new AppSettings { Language = "ja" };
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);

        var notes = SampleNoteFactory.CreateInitialNotes(settings, storage);

        Assert.NotEqual(notes[0].Id, notes[1].Id);
        Assert.True(notes[1].CreatedAt >= notes[0].CreatedAt);
    }

    private sealed class TempDataDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ScreenPinNotes.Tests",
            Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
