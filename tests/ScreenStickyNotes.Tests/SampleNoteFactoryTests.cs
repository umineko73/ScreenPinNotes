using System.IO;
using System.Globalization;
using ScreenStickyNotes.Models;
using ScreenStickyNotes.Services;

namespace ScreenStickyNotes.Tests;

// SampleNoteFactory reads from AppContext.BaseDirectory\SampleNotes, which MSBuild
// copies into this test project's own output too (ProjectReference to
// src/ScreenStickyNotes.csproj carries its CopyToOutputDirectory items). So unlike
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
        Assert.Equal("Markdown・画像マニュアル", markdownNote.Title);
        Assert.Equal("sky", markdownNote.ColorKey);
        Assert.Equal("📝", markdownNote.Icon);
        Assert.Equal(645, markdownNote.Width);
        Assert.False(string.IsNullOrWhiteSpace(markdownNote.Content));
        Assert.Contains("assets/markdown-image-guide.png", markdownNote.Content);

        var usageNote = notes[1];
        Assert.Equal("基本操作マニュアル", usageNote.Title);
        Assert.Equal("yellow", usageNote.ColorKey);
        Assert.Equal("💡", usageNote.Icon);
        Assert.Equal(585, usageNote.Width);
        Assert.Equal(825, usageNote.X);
        Assert.False(string.IsNullOrWhiteSpace(usageNote.Content));
        Assert.Contains("assets/window-position-guide.png", usageNote.Content);
    }

    [Fact]
    public void CreateInitialNotes_English_LoadsEnglishSamples()
    {
        var settings = new AppSettings { Language = "en" };
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);

        var notes = SampleNoteFactory.CreateInitialNotes(settings, storage);

        Assert.Equal(2, notes.Count);
        Assert.Equal("Markdown and Image Manual", notes[0].Title);
        Assert.Equal("Basic Usage Manual", notes[1].Title);
        Assert.Contains("The body supports Markdown.", notes[0].Content);
        Assert.Contains("assets/markdown-image-guide.png", notes[0].Content);
        Assert.Contains("Double-click the body", notes[1].Content);
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
            Assert.Equal("Markdown and Image Manual", notes[0].Title);
            Assert.Equal("Basic Usage Manual", notes[1].Title);
            Assert.Contains("The body supports Markdown.", notes[0].Content);
            Assert.Contains("Double-click the body", notes[1].Content);
            Assert.True(File.Exists(Path.Combine(
                storage.GetNoteAssetsDirectoryPath(notes[0].Id),
                "markdown-image-guide.png")));
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

    [Fact]
    public void CreateInitialNotes_CopiesSampleAssets()
    {
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var settings = new AppSettings { Language = "ja" };

        var notes = SampleNoteFactory.CreateInitialNotes(settings, storage);

        Assert.Equal(2, notes.Count);
        Assert.True(File.Exists(Path.Combine(
            storage.GetNoteAssetsDirectoryPath(notes[0].Id),
            "markdown-image-guide.png")));
        Assert.True(File.Exists(Path.Combine(
            storage.GetNoteAssetsDirectoryPath(notes[1].Id),
            "window-position-guide.png")));
    }

    private sealed class TempDataDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ScreenStickyNotes.Tests",
            Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
