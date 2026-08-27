using System.IO;
using System.Text.Json;
using ScreenStickyNotes.Models;
using ScreenStickyNotes.Services;

namespace ScreenStickyNotes.Tests;

// StorageService(dataRoot) を使うことで、実ユーザーの %APPDATA% や
// 環境変数 SCREENSTICKYNOTES_DATA に触れずにテストごとの一時フォルダで完結させる。
public sealed class StorageServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly StorageService _storage;

    public StorageServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ScreenStickyNotesTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRoot);
        _storage = new StorageService(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void SaveNote_ThenLoad_RoundTripsContentSeparatelyFromMeta()
    {
        var note = new StickyNote { Content = "# Hello\nworld", Title = "My Note" };

        _storage.SaveNote(note);

        var noteDir = Path.Combine(_tempRoot, "notes", note.Id);
        Assert.True(File.Exists(Path.Combine(noteDir, "meta.json")));
        Assert.True(File.Exists(Path.Combine(noteDir, "content.md")));
        Assert.Equal("# Hello\nworld", File.ReadAllText(Path.Combine(noteDir, "content.md")));

        // meta.json must not duplicate the body ([JsonIgnore] on StickyNote.Content).
        var metaJson = File.ReadAllText(Path.Combine(noteDir, "meta.json"));
        using var doc = JsonDocument.Parse(metaJson);
        Assert.False(doc.RootElement.TryGetProperty("Content", out _));

        var loaded = _storage.Load();
        var loadedNote = Assert.Single(loaded);
        Assert.Equal(note.Id, loadedNote.Id);
        Assert.Equal("My Note", loadedNote.Title);
        Assert.Equal("# Hello\nworld", loadedNote.Content);
    }

    [Fact]
    public void Load_SortsNotesByCreatedAt()
    {
        var now = DateTime.Now;
        var older = new StickyNote { CreatedAt = now.AddMinutes(-10) };
        var newer = new StickyNote { CreatedAt = now };
        _storage.SaveNote(newer);
        _storage.SaveNote(older);

        var loaded = _storage.Load();

        Assert.Equal([older.Id, newer.Id], loaded.Select(n => n.Id));
    }

    [Fact]
    public void Load_SkipsNoteFolderWithCorruptMeta()
    {
        var good = new StickyNote();
        _storage.SaveNote(good);

        var brokenDir = Path.Combine(_tempRoot, "notes", "broken-note");
        Directory.CreateDirectory(brokenDir);
        File.WriteAllText(Path.Combine(brokenDir, "meta.json"), "{ not valid json");

        var loaded = _storage.Load();

        var loadedNote = Assert.Single(loaded);
        Assert.Equal(good.Id, loadedNote.Id);
    }

    [Fact]
    public void Load_SkipsNoteFolderWithoutMetaJson()
    {
        var emptyDir = Path.Combine(_tempRoot, "notes", "no-meta");
        Directory.CreateDirectory(emptyDir);

        var loaded = _storage.Load();

        Assert.Empty(loaded);
    }

    [Fact]
    public void DeleteNote_RemovesOnlyThatNotesFolder()
    {
        var keep = new StickyNote();
        var remove = new StickyNote();
        _storage.SaveNote(keep);
        _storage.SaveNote(remove);

        _storage.DeleteNote(remove.Id);

        var loaded = _storage.Load();
        var loadedNote = Assert.Single(loaded);
        Assert.Equal(keep.Id, loadedNote.Id);
        Assert.False(Directory.Exists(Path.Combine(_tempRoot, "notes", remove.Id)));
    }

    [Fact]
    public void Load_IgnoresMetaIdAndUsesContainingFolderName()
    {
        var note = new StickyNote { Content = "body" };
        _storage.SaveNote(note);

        var metaPath = Path.Combine(_tempRoot, "notes", note.Id, "meta.json");
        var metaJson = File.ReadAllText(metaPath);
        using var doc = JsonDocument.Parse(metaJson);
        var patched = new Dictionary<string, JsonElement>();
        foreach (var property in doc.RootElement.EnumerateObject())
            patched[property.Name] = property.Value.Clone();
        patched["Id"] = JsonDocument.Parse("\"..\\\\outside\"").RootElement.Clone();
        File.WriteAllText(metaPath, JsonSerializer.Serialize(patched));

        var loaded = _storage.Load();

        var loadedNote = Assert.Single(loaded);
        Assert.Equal(note.Id, loadedNote.Id);
    }

    [Fact]
    public void SaveNote_RejectsUnsafeNoteId()
    {
        var note = new StickyNote { Id = @"..\outside" };

        Assert.Throws<ArgumentException>(() => _storage.SaveNote(note));
    }

    [Fact]
    public void DeleteNote_IgnoresUnsafeNoteId()
    {
        var outside = Path.Combine(_tempRoot, "outside");
        Directory.CreateDirectory(outside);

        _storage.DeleteNote(@"..\outside");

        Assert.True(Directory.Exists(outside));
    }

    [Fact]
    public void GetNoteAssetsDirectoryPath_UsesInstanceDataRoot()
    {
        var note = new StickyNote();

        var assetsDir = _storage.GetNoteAssetsDirectoryPath(note.Id);

        Assert.Equal(Path.Combine(_tempRoot, "notes", note.Id, "assets"), assetsDir);
    }

    [Fact]
    public void SaveSettings_ThenLoad_RoundTripsAndAppliesNormalize()
    {
        var settings = new AppSettings { Language = "en", Theme = "Dark" };
        settings.HoverOpacityBoostPercent = 999; // out of range, Normalize should clamp on save

        _storage.SaveSettings(settings);
        var loaded = _storage.LoadSettings();

        Assert.Equal("en", loaded.Language);
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal(90, loaded.HoverOpacityBoostPercent);
    }

    [Fact]
    public void LoadSettings_NoFileYet_ReturnsNormalizedDefaults()
    {
        var settings = _storage.LoadSettings();

        Assert.Equal("ja", settings.Language);
        Assert.Equal("Light", settings.Theme);
        Assert.NotEmpty(settings.IconPalette);
    }

    [Fact]
    public void Load_MigratesLegacyNotesJsonAndRenamesToBak()
    {
        var legacyId = Guid.NewGuid().ToString();
        var legacyJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                Id = legacyId,
                Content = "legacy body",
                X = 10.0, Y = 20.0, Width = 260.0, Height = 220.0,
                ColorKey = "blue", FontFamily = "Yu Gothic UI", FontSize = 13.0,
                IsTopmost = false, IsFolded = false,
                CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now,
            },
        });
        File.WriteAllText(Path.Combine(_tempRoot, "notes.json"), legacyJson);

        var loaded = _storage.Load();

        var migrated = Assert.Single(loaded);
        Assert.Equal(legacyId, migrated.Id);
        Assert.Equal("legacy body", migrated.Content);
        Assert.False(File.Exists(Path.Combine(_tempRoot, "notes.json")));
        Assert.True(File.Exists(Path.Combine(_tempRoot, "notes.json.bak")));
    }

    [Fact]
    public void Load_MigratesRemainingLegacyNotesEvenWhenOneHasAnUnsafeId()
    {
        var goodId = Guid.NewGuid().ToString();
        var legacyJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                Id = @"..\outside", // unsafe: GetNoteDirectoryPath would throw for this one
                Content = "bad entry",
                X = 0.0, Y = 0.0, Width = 260.0, Height = 220.0,
                ColorKey = "yellow", FontFamily = "Yu Gothic UI", FontSize = 13.0,
                IsTopmost = false, IsFolded = false,
                CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now,
            },
            new
            {
                Id = goodId,
                Content = "good entry",
                X = 10.0, Y = 20.0, Width = 260.0, Height = 220.0,
                ColorKey = "blue", FontFamily = "Yu Gothic UI", FontSize = 13.0,
                IsTopmost = false, IsFolded = false,
                CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now,
            },
        });
        File.WriteAllText(Path.Combine(_tempRoot, "notes.json"), legacyJson);

        var loaded = _storage.Load();

        // The unsafe entry can't be written (no valid folder for it) and is skipped,
        // but that must not block the good entry from migrating or the legacy file
        // from being renamed to .bak.
        var migrated = Assert.Single(loaded);
        Assert.Equal(goodId, migrated.Id);
        Assert.Equal("good entry", migrated.Content);
        Assert.False(File.Exists(Path.Combine(_tempRoot, "notes.json")));
        Assert.True(File.Exists(Path.Combine(_tempRoot, "notes.json.bak")));
    }

    [Fact]
    public void Load_KeepsLegacyNotesJsonWhenMigrationCannotRenameBackup()
    {
        var legacyId = Guid.NewGuid().ToString();
        var legacyJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                Id = legacyId,
                Content = "legacy body",
                X = 10.0, Y = 20.0, Width = 260.0, Height = 220.0,
                ColorKey = "blue", FontFamily = "Yu Gothic UI", FontSize = 13.0,
                IsTopmost = false, IsFolded = false,
                CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now,
            },
        });
        File.WriteAllText(Path.Combine(_tempRoot, "notes.json"), legacyJson);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "notes.json.bak"));

        var loaded = _storage.Load();

        var migrated = Assert.Single(loaded);
        Assert.Equal(legacyId, migrated.Id);
        Assert.True(File.Exists(Path.Combine(_tempRoot, "notes.json")));
    }
}
