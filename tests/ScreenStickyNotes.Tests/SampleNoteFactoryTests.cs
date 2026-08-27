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

        var notes = SampleNoteFactory.CreateInitialNotes(settings);

        Assert.Equal(2, notes.Count);

        var markdownNote = notes[0];
        Assert.Equal("Markdown サンプル", markdownNote.Title);
        Assert.Equal("sky", markdownNote.ColorKey);
        Assert.Equal("📝", markdownNote.Icon);
        Assert.False(string.IsNullOrWhiteSpace(markdownNote.Content));

        var usageNote = notes[1];
        Assert.Equal("使い方", usageNote.Title);
        Assert.Equal("yellow", usageNote.ColorKey);
        Assert.Equal("💡", usageNote.Icon);
        Assert.False(string.IsNullOrWhiteSpace(usageNote.Content));
    }

    [Fact]
    public void CreateInitialNotes_English_LoadsEnglishSampleTitles()
    {
        var settings = new AppSettings { Language = "en" };

        var notes = SampleNoteFactory.CreateInitialNotes(settings);

        Assert.Equal(2, notes.Count);
        Assert.Equal("Markdown sample", notes[0].Title);
        Assert.Equal("How to use", notes[1].Title);
    }

    [Fact]
    public void CreateInitialNotes_AssignsFreshIdsAndIncreasingCreatedAt()
    {
        var settings = new AppSettings { Language = "ja" };

        var notes = SampleNoteFactory.CreateInitialNotes(settings);

        Assert.NotEqual(notes[0].Id, notes[1].Id);
        Assert.True(notes[1].CreatedAt >= notes[0].CreatedAt);
    }
}
