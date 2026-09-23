using System.IO;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class StorageLoadDiagnosticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingSettingsAreNormal_CorruptSettingsArePreservedAcrossRootSwitch()
    {
        var storage = new StorageService(_root);
        Assert.False(storage.LoadSettingsWithDiagnostics().HasErrors);
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "{broken settings");
        var loaded = storage.LoadSettingsWithDiagnostics();
        Assert.Equal(StorageLoadFailure.InvalidData, Assert.Single(loaded.Issues).Kind);
        storage = storage.WithNotesRoot(Path.Combine(_root, "other-notes"));
        storage.SaveSettings(loaded.Value);
        var backup = Assert.Single(Directory.GetFiles(_root, "settings.json.corrupt-*.bak"));
        Assert.Equal("{broken settings", File.ReadAllText(backup));
        Assert.False(storage.LoadSettingsWithDiagnostics().HasErrors);
        storage.SaveSettings(loaded.Value);
        Assert.Single(Directory.GetFiles(_root, "settings.json.corrupt-*.bak"));
    }

    [Fact]
    public void CorruptNoteIsReported_HealthyNoteStillLoads()
    {
        var storage = new StorageService(_root);
        storage.SaveNote(new StickyNote { Id = "good", Content = "preserved" });
        var bad = storage.GetNoteDirectoryPath("bad");
        Directory.CreateDirectory(bad);
        File.WriteAllText(Path.Combine(bad, "meta.json"), "null");
        var loaded = storage.LoadWithDiagnostics();
        Assert.Equal("good", Assert.Single(loaded.Value).Id);
        Assert.Equal(StorageLoadFailure.InvalidData, Assert.Single(loaded.Issues).Kind);
        Assert.Equal("null", File.ReadAllText(Path.Combine(bad, "meta.json")));
    }

    [Fact]
    public void LoadingCachedExternalNoteDoesNotOpenItsSource()
    {
        var storage = new StorageService(_root);
        var external = Path.Combine(_root, "source.md");
        storage.SaveNote(new StickyNote { Content = "cached", ExternalContentPath = external });
        File.WriteAllText(external, "new text");
        var loaded = storage.LoadWithDiagnostics(refreshExternalContent: false);
        Assert.Equal("cached", Assert.Single(loaded.Value).Content);
        Assert.Equal("new text", Assert.Single(storage.Load()).Content);
    }

    [Fact]
    public void SaveAfterLoadLeavesUnchangedFilesUntouched()
    {
        var storage = new StorageService(_root);
        var note = new StickyNote();
        storage.SaveNote(note);
        var meta = Path.Combine(storage.GetNoteDirectoryPath(note.Id), "meta.json");
        var body = Path.Combine(storage.GetNoteDirectoryPath(note.Id), "content.md");
        var stamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(meta, stamp);
        File.SetLastWriteTimeUtc(body, stamp);
        var reader = new StorageService(_root);
        reader.Save(reader.Load());
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(meta));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(body));
    }

    [Theory]
    [InlineData("StorageSettingsLoadFailed")]
    [InlineData("StorageNotesLoadFailed")]
    public void DiagnosticMessagesAreTranslated(string key)
    {
        var en = LocalizationService.T(key, "en");
        var ja = LocalizationService.T(key, "ja");
        Assert.NotEqual(key, en);
        Assert.NotEqual(key, ja);
        Assert.NotEqual(en, ja);
        Assert.DoesNotContain("???", ja);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
