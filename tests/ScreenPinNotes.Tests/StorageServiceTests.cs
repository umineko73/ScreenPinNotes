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

using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

// StorageService(dataRoot) を使うことで、実ユーザーの %APPDATA% や
// 環境変数 SCREENPINNOTES_DATA に触れずにテストごとの一時フォルダで完結させる。
public sealed class StorageServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly StorageService _storage;

    [Fact]
    public void TailReadRemovesUtf8Preamble()
    {
        var path = Path.Combine(_tempRoot, "bom.log");
        File.WriteAllText(path, "INFO 日本語\n", new System.Text.UTF8Encoding(true));
        var note = new StickyNote { ExternalContentPath = path, ExternalTailMode = true };
        Assert.Equal("INFO 日本語\n", StorageService.ReadExternalContent(note, 200));
    }

    [Fact]
    public void TailReadRejectsUnexpectedEndInsteadOfReturningZeroPadding()
    {
        using var stream = new MemoryStream(new byte[] { 65 });
        var method = typeof(StorageService).GetMethod("ReadExact",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            method.Invoke(null, new object[] { stream, new byte[10], 10 }));
        Assert.IsType<EndOfStreamException>(exception.InnerException);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsTheHiddenTitleBarSetting()
    {
        var note = new StickyNote { IsTitleBarHidden = true, Content = "body" };
        _storage.Save([note]);

        var loaded = Assert.Single(_storage.Load());

        Assert.True(loaded.IsTitleBarHidden);
    }

    [Fact]
    public void Load_IgnoresOldNotesJsonWithoutModifyingIt()
    {
        var path = Path.Combine(_tempRoot, "notes.json");
        var json = JsonSerializer.Serialize(new[] { new { Id = Guid.NewGuid().ToString(), Content = "old body" } });
        File.WriteAllText(path, json);
        Assert.Empty(_storage.Load());
        Assert.Equal(json, File.ReadAllText(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    public StorageServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ScreenPinNotesTests", Guid.NewGuid().ToString());
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
        var note = new StickyNote
        {
            Content = "# Hello\nworld",
            Title = "My Note",
            LayerOrder = 12,
            FoldedX = 12,
            FoldedY = 34,
            FoldedWidth = 180,
            IsHidden = true,
            IsReadOnly = true,
            IsPositionSeparated = true,
        };

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
        Assert.Equal(12, loadedNote.LayerOrder);
        Assert.Equal("# Hello\nworld", loadedNote.Content);
        Assert.Equal(12, loadedNote.FoldedX);
        Assert.Equal(34, loadedNote.FoldedY);
        Assert.Equal(180, loadedNote.FoldedWidth);
        Assert.True(loadedNote.IsHidden);
        Assert.True(loadedNote.IsReadOnly);
        Assert.True(loadedNote.IsPositionSeparated);
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
    public void Load_ExternalContentNote_ReadsLinkedFileInsteadOfCachedContent()
    {
        var externalPath = Path.Combine(_tempRoot, "external.md");
        File.WriteAllText(externalPath, "# External\nbody");
        var note = new StickyNote
        {
            Content = "cached",
            ExternalContentPath = externalPath,
            IsReadOnly = true,
        };
        _storage.SaveNote(note);

        var loaded = _storage.Load();

        var loadedNote = Assert.Single(loaded);
        Assert.True(loadedNote.IsExternalContent);
        Assert.True(loadedNote.IsReadOnly);
        Assert.Equal(externalPath, loadedNote.ExternalContentPath);
        Assert.Equal("# External\nbody", loadedNote.Content);
    }

    [Fact]
    public void SaveNote_ThenLoad_RoundTripsReminder()
    {
        var nextAt = new DateTime(2026, 8, 31, 14, 30, 0);
        var note = new StickyNote
        {
            Reminder = new ReminderSettings
            {
                NextAt = nextAt,
                Recurrence = "None",
                LastTriggeredAt = nextAt.AddMinutes(-10),
            },
        };

        _storage.SaveNote(note);

        var loadedNote = Assert.Single(_storage.Load());
        Assert.NotNull(loadedNote.Reminder);
        Assert.Equal(nextAt, loadedNote.Reminder.NextAt);
        Assert.Equal("None", loadedNote.Reminder.Recurrence);
        Assert.Equal(nextAt.AddMinutes(-10), loadedNote.Reminder.LastTriggeredAt);
        Assert.True(loadedNote.HasReminder);
    }

    // ShowAlert はこの機能追加より後に足したフィールド。追加前に保存された
    // meta.json にはキー自体が存在しないので、そのまま読み込むと null になる
    // （既定値 false ではない）。null は「従来どおりアラートを出す」に
    // 読み替えて書き戻さないと、既存ユーザーのリマインダーが無警告で
    // トースト通知だけに格下げされてしまう。
    [Fact]
    public void Load_ReminderWithoutShowAlertField_MigratesToAlertEnabled()
    {
        var noteId = Guid.NewGuid().ToString();
        var noteDir = Path.Combine(_tempRoot, "notes", noteId);
        Directory.CreateDirectory(noteDir);
        File.WriteAllText(Path.Combine(noteDir, "meta.json"), """
            {
              "Reminder": {
                "NextAt": "2026-08-31T14:30:00",
                "Recurrence": "None"
              }
            }
            """);
        File.WriteAllText(Path.Combine(noteDir, "content.md"), "");

        var loadedNote = Assert.Single(_storage.Load());

        Assert.NotNull(loadedNote.Reminder);
        Assert.Equal(true, loadedNote.Reminder.ShowAlert);
    }

    [Fact]
    public void SaveNote_ThenLoad_RoundTripsExternalImageWidthOverrides()
    {
        var note = new StickyNote
        {
            ExternalImageWidthOverrides =
            {
                ["1:0:assets/screenshot.png"] = 480,
            },
        };

        _storage.SaveNote(note);

        var loadedNote = Assert.Single(_storage.Load());
        Assert.True(loadedNote.ExternalImageWidthOverrides.TryGetValue("1:0:assets/screenshot.png", out var width));
        Assert.Equal(480, width);
    }

    [Fact]
    public void ReadExternalContent_WhenFileIsMissing_ReturnsReadableError()
    {
        var missingPath = Path.Combine(_tempRoot, "missing.md");
        var note = new StickyNote
        {
            Content = "cached",
            ExternalContentPath = missingPath,
        };

        var content = StorageService.ReadExternalContent(note);

        Assert.Contains("External file not found", content);
        Assert.Contains(missingPath, content);
    }

    [Fact]
    public void Load_ExternalContentNote_WhenFileTransientlyMissing_KeepsCachedContent()
    {
        // 起動時に外部ファイルへ一時的にアクセスできない場合、
        // 直前に content.md へキャッシュされていた内容を保持し、
        // エラー文言で上書きしてはいけない。
        var externalPath = Path.Combine(_tempRoot, "external.md");
        File.WriteAllText(externalPath, "# External\ncached body");
        var note = new StickyNote
        {
            Content = "# External\ncached body",
            ExternalContentPath = externalPath,
            IsReadOnly = true,
        };
        _storage.SaveNote(note);
        File.Delete(externalPath);

        var loaded = _storage.Load();

        var loadedNote = Assert.Single(loaded);
        Assert.Equal("# External\ncached body", loadedNote.Content);
    }

    [Fact]
    public void TryReadExternalContent_WhenFileIsMissing_ReturnsFalseAndDoesNotThrow()
    {
        var missingPath = Path.Combine(_tempRoot, "missing.md");
        var note = new StickyNote { Content = "cached", ExternalContentPath = missingPath };

        var success = StorageService.TryReadExternalContent(note, out var content);

        Assert.False(success);
        Assert.Equal("", content);
    }

    [Fact]
    public void Load_ExternalContentNote_WhenFileIsLocked_KeepsCachedContent()
    {
        var externalPath = Path.Combine(_tempRoot, "external.md");
        File.WriteAllText(externalPath, "latest content");
        var note = new StickyNote
        {
            Content = "cached content",
            ExternalContentPath = externalPath,
            IsReadOnly = true,
        };
        _storage.SaveNote(note);

        using var fileLock = new FileStream(
            externalPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var readable = StorageService.TryReadExternalContent(note, out var content);
        var loadedNote = Assert.Single(_storage.Load());

        Assert.False(readable);
        Assert.Equal("", content);
        Assert.Equal("cached content", loadedNote.Content);
    }

    [Fact]
    public void ReadExternalContent_TailMode_ReturnsOnlyLastLines()
    {
        var path = Path.Combine(_tempRoot, "app.log");
        File.WriteAllText(path, "line1\nline2\nline3\nline4\nline5\n");
        var note = new StickyNote { ExternalContentPath = path, ExternalTailMode = true };

        var content = StorageService.ReadExternalContent(note, tailLineCount: 2);

        Assert.Equal("line4\nline5\n", content);
    }

    [Fact]
    public void ReadExternalContent_TailMode_WithoutTrailingNewline_ReturnsLastLines()
    {
        var path = Path.Combine(_tempRoot, "app.log");
        File.WriteAllText(path, "line1\nline2\nline3");
        var note = new StickyNote { ExternalContentPath = path, ExternalTailMode = true };

        var content = StorageService.ReadExternalContent(note, tailLineCount: 2);

        Assert.Equal("line2\nline3", content);
    }

    [Fact]
    public void ReadExternalContent_TailMode_WhenFileHasFewerLinesThanRequested_ReturnsWholeFile()
    {
        var path = Path.Combine(_tempRoot, "app.log");
        File.WriteAllText(path, "line1\nline2\n");
        var note = new StickyNote { ExternalContentPath = path, ExternalTailMode = true };

        var content = StorageService.ReadExternalContent(note, tailLineCount: 50);

        Assert.Equal("line1\nline2\n", content);
    }

    [Fact]
    public void TryReadExternalContentForDisplay_TailMode_ReadsOnlyTail()
    {
        var path = Path.Combine(_tempRoot, "app.log");
        File.WriteAllText(path, string.Join('\n', Enumerable.Range(1, 1000).Select(i => $"line{i}")) + "\n");
        var note = new StickyNote { ExternalContentPath = path, ExternalTailMode = true };

        var success = StorageService.TryReadExternalContentForDisplay(note, 3, out var content);

        Assert.True(success);
        Assert.Equal("line998\nline999\nline1000\n", content);
    }

    // ログは書き手がファイルを開いたまま追記する。FileShare.Read で開く
    // File.ReadAllText では「別のプロセスが使用中」となり、更新が一切
    // 反映されなくなっていた。
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadExternalContent_WhileTheWriterKeepsTheLogOpen_StillReads(bool tailMode)
    {
        var logPath = Path.Combine(_tempRoot, "app.log");
        File.WriteAllText(logPath, "first line\n");
        var note = new StickyNote { ExternalContentPath = logPath, ExternalTailMode = tailMode };

        using var writer = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        var appended = System.Text.Encoding.UTF8.GetBytes("second line\n");
        writer.Write(appended, 0, appended.Length);
        writer.Flush();

        Assert.True(StorageService.TryReadExternalContentForDisplay(note, 200, out var shown));
        Assert.Contains("second line", shown);
        Assert.True(StorageService.TryReadExternalContent(note, out var full));
        Assert.Contains("second line", full);
        Assert.Contains("second line", StorageService.ReadExternalContent(note));
    }

    [Fact]
    public void TryReadExternalContentForDisplay_NonTailMode_ReadsFullContent()
    {
        var path = Path.Combine(_tempRoot, "notes.md");
        File.WriteAllText(path, "line1\nline2\nline3\n");
        var note = new StickyNote { ExternalContentPath = path, ExternalTailMode = false };

        var success = StorageService.TryReadExternalContentForDisplay(note, 1, out var content);

        Assert.True(success);
        Assert.Equal("line1\nline2\nline3\n", content);
    }

    [Fact]
    public void Load_ExternalTailModeNote_LoadsOnlyConfiguredTailLineCount()
    {
        var externalPath = Path.Combine(_tempRoot, "app.log");
        File.WriteAllText(externalPath, "line1\nline2\nline3\nline4\n");
        var note = new StickyNote
        {
            Content = "",
            ExternalContentPath = externalPath,
            ExternalTailMode = true,
            IsReadOnly = true,
        };
        _storage.SaveNote(note);

        var loadedNote = Assert.Single(_storage.Load(externalTailLineCount: 2));

        Assert.Equal("line3\nline4\n", loadedNote.Content);
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
    public void ExportNotesToZip_WritesNotesFolderLayout()
    {
        var note = new StickyNote { Content = "body" };
        _storage.SaveNote(note);
        var assetsDir = _storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        File.WriteAllText(Path.Combine(assetsDir, "image.png"), "asset");
        var zipPath = Path.Combine(_tempRoot, "export.zip");

        _storage.ExportNotesToZip(zipPath);

        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries.Select(e => e.FullName).ToHashSet();
        Assert.Contains($"notes/{note.Id}/meta.json", entries);
        Assert.Contains($"notes/{note.Id}/content.md", entries);
        Assert.Contains($"notes/{note.Id}/assets/image.png", entries);
    }

    [Fact]
    public void ExportNotesToZip_WhenZipIsUnderNotesRoot_DoesNotIncludeItself()
    {
        var note = new StickyNote { Content = "body" };
        _storage.SaveNote(note);
        var zipPath = Path.Combine(_storage.NotesRoot, "export.zip");

        _storage.ExportNotesToZip(zipPath);

        using var archive = ZipFile.OpenRead(zipPath);
        Assert.DoesNotContain(archive.Entries, e => e.FullName == "notes/export.zip");
    }

    [Fact]
    public void ImportNotesFromZip_AddsNotesAndPreservesAssets()
    {
        var sourceRoot = Path.Combine(_tempRoot, "source");
        var sourceStorage = new StorageService(sourceRoot);
        var note = new StickyNote { Content = "imported", Title = "Imported", IsReadOnly = true };
        sourceStorage.SaveNote(note);
        var sourceAssets = sourceStorage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(sourceAssets);
        File.WriteAllText(Path.Combine(sourceAssets, "asset.txt"), "asset");
        var zipPath = Path.Combine(_tempRoot, "import.zip");
        sourceStorage.ExportNotesToZip(zipPath);

        var result = _storage.ImportNotesFromZip(zipPath);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.SkippedCount);
        var imported = Assert.Single(_storage.Load());
        Assert.Equal(note.Id, imported.Id);
        Assert.Equal("imported", imported.Content);
        Assert.Equal("Imported", imported.Title);
        Assert.True(imported.IsReadOnly);
        Assert.True(File.Exists(Path.Combine(_storage.GetNoteAssetsDirectoryPath(imported.Id), "asset.txt")));
    }

    [Fact]
    public void ImportNotesFromZip_ExternalContentNote_PreservesExportedContentOnDiskInsteadOfReresolvingLocally()
    {
        // エクスポート元マシンの外部ファイルパスが、たまたまインポート先マシンの
        // 別内容のファイルと同じパスを指していても、インポートされたノートの
        // content.md は zip に入っていたスナップショットのまま保持されるべきで、
        // インポート処理中にインポート先でそのパスを再解決した（無関係な）
        // 内容で上書きされてはいけない。
        var sourceRoot = Path.Combine(_tempRoot, "source");
        var sourceStorage = new StorageService(sourceRoot);
        var sharedPath = Path.Combine(_tempRoot, "shared-external.md");
        File.WriteAllText(sharedPath, "content on the exporting machine");
        var note = new StickyNote
        {
            Content = "content on the exporting machine",
            ExternalContentPath = sharedPath,
            IsReadOnly = true,
        };
        sourceStorage.SaveNote(note);
        var zipPath = Path.Combine(_tempRoot, "external-import.zip");
        sourceStorage.ExportNotesToZip(zipPath);

        // インポート先マシンでは、同じ絶対パスに全く別の内容のファイルが存在する
        // （インポート処理中はこのファイルが「解決」されてしまう状況を再現）。
        File.WriteAllText(sharedPath, "unrelated content on the importing machine");

        var result = _storage.ImportNotesFromZip(zipPath);
        Assert.Equal(1, result.ImportedCount);
        var importedId = Assert.Single(_storage.Load()).Id;

        // その後リンク先ファイルが消えても、content.md に永続化されていた
        // スナップショットはエクスポート時点の内容のままであるべき。
        File.Delete(sharedPath);
        var reloaded = Assert.Single(_storage.Load());

        Assert.Equal(importedId, reloaded.Id);
        Assert.Equal("content on the exporting machine", reloaded.Content);
    }

    [Fact]
    public void ImportNotesFromZip_DuplicateIdsAreImportedWithNewIds()
    {
        var existing = new StickyNote { Content = "existing" };
        _storage.SaveNote(existing);

        var sourceRoot = Path.Combine(_tempRoot, "source");
        var sourceStorage = new StorageService(sourceRoot);
        var duplicate = new StickyNote { Id = existing.Id, Content = "duplicate" };
        sourceStorage.SaveNote(duplicate);
        var zipPath = Path.Combine(_tempRoot, "duplicate.zip");
        sourceStorage.ExportNotesToZip(zipPath);

        var result = _storage.ImportNotesFromZip(zipPath);

        Assert.Equal(1, result.ImportedCount);
        var loaded = _storage.Load();
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, n => n.Id == existing.Id && n.Content == "existing");
        Assert.Contains(loaded, n => n.Id != existing.Id && n.Content == "duplicate");
    }

    [Fact]
    public void ImportNotesFromZip_IgnoresTraversalEntries()
    {
        var zipPath = Path.Combine(_tempRoot, "malicious.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var malicious = archive.CreateEntry("../outside.txt");
            using (var writer = new StreamWriter(malicious.Open()))
                writer.Write("outside");

            var note = new StickyNote { Content = "safe" };
            var meta = archive.CreateEntry($"notes/{note.Id}/meta.json");
            using (var writer = new StreamWriter(meta.Open()))
                writer.Write(JsonSerializer.Serialize(note));
            var content = archive.CreateEntry($"notes/{note.Id}/content.md");
            using (var writer = new StreamWriter(content.Open()))
                writer.Write("safe");
        }

        var result = _storage.ImportNotesFromZip(zipPath);

        Assert.Equal(1, result.ImportedCount);
        Assert.False(File.Exists(Path.Combine(_tempRoot, "outside.txt")));
        Assert.Equal("safe", Assert.Single(_storage.Load()).Content);
    }

    [Theory]
    [InlineData(StorageService.ImportConflictAction.Overwrite)]
    [InlineData(StorageService.ImportConflictAction.Rename)]
    [InlineData(StorageService.ImportConflictAction.Skip)]
    public void ImportConflictChoicePreservesOrReplacesWholeFolder(StorageService.ImportConflictAction action)
    {
        var existing = new StickyNote { Content = "original", Title = "Original" };
        _storage.SaveNote(existing);
        var assets = _storage.GetNoteAssetsDirectoryPath(existing.Id);
        Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(assets, "old.txt"), "old");
        var source = new StorageService(Path.Combine(_tempRoot, "conflict-source"));
        source.SaveNote(new StickyNote { Id = existing.Id, Content = "replacement", Title = "Incoming" });
        var incomingAssets = source.GetNoteAssetsDirectoryPath(existing.Id);
        Directory.CreateDirectory(incomingAssets);
        File.WriteAllText(Path.Combine(incomingAssets, "new.txt"), "new");
        var archive = Path.Combine(_tempRoot, "conflict.zip");
        source.ExportNotesToZip(archive);
        var calls = 0;
        var result = _storage.ImportNotesFromZip(archive, note =>
        {
            calls++;
            Assert.Equal(existing.Id, note.Id);
            Assert.Equal("Incoming", note.Title);
            return action;
        });
        Assert.Equal(1, calls);
        Assert.Equal(action == StorageService.ImportConflictAction.Skip ? 0 : 1, result.ImportedCount);
        Assert.Equal(action == StorageService.ImportConflictAction.Skip ? 1 : 0, result.SkippedCount);
        var notes = _storage.Load();
        var retained = Assert.Single(notes, n => n.Id == existing.Id);
        var overwrite = action == StorageService.ImportConflictAction.Overwrite;
        Assert.Equal(overwrite ? "replacement" : "original", retained.Content);
        Assert.Equal(!overwrite, File.Exists(Path.Combine(assets, "old.txt")));
        Assert.Equal(overwrite, File.Exists(Path.Combine(assets, "new.txt")));
        Assert.Equal(action == StorageService.ImportConflictAction.Rename ? 2 : 1, notes.Count);
        if (action == StorageService.ImportConflictAction.Rename)
        {
            var added = Assert.Single(notes, n => n.Id != existing.Id);
            Assert.Equal("replacement", added.Content);
            Assert.True(File.Exists(Path.Combine(_storage.GetNoteAssetsDirectoryPath(added.Id), "new.txt")));
        }
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
        settings.StorageRoot = Path.Combine(_tempRoot, "custom-storage");
        settings.SearchHistory = ["report*", "^task[0-9]+$", "日本語"];
        settings.NewNoteHotkey = "Ctrl+Alt+Shift+N";

        _storage.SaveSettings(settings);
        var loaded = _storage.LoadSettings();

        Assert.Equal("en", loaded.Language);
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal(90, loaded.HoverOpacityBoostPercent);
        Assert.Equal(Path.Combine(_tempRoot, "custom-storage"), loaded.StorageRoot);
        Assert.Equal(settings.SearchHistory, loaded.SearchHistory);
        Assert.Equal("Ctrl+Alt+Shift+N", loaded.NewNoteHotkey);
    }

    [Fact]
    public void LoadSettings_NoFileYet_ReturnsNormalizedDefaults()
    {
        var settings = _storage.LoadSettings();

        Assert.Equal(AppSettings.GetDefaultLanguage(CultureInfo.CurrentUICulture), settings.Language);
        Assert.Equal("Light", settings.Theme);
        Assert.NotEmpty(settings.IconPalette);
    }

    [Fact]
    public void LoadSettings_NoFileYet_UsesEnglishForNonJapaneseOsCulture()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            var settings = _storage.LoadSettings();

            Assert.Equal("en", settings.Language);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void Constructor_WithSeparateNotesRoot_KeepsSettingsInSettingsRoot()
    {
        var settingsRoot = Path.Combine(_tempRoot, "settings-root");
        var storageRoot = Path.Combine(_tempRoot, "storage-root");
        var notesRoot = Path.Combine(storageRoot, "notes");
        var storage = new StorageService(settingsRoot).WithStorageRoot(storageRoot);
        var note = new StickyNote { Content = "body" };

        storage.SaveSettings(new AppSettings { StorageRoot = storageRoot });
        storage.SaveNote(note);

        Assert.True(File.Exists(Path.Combine(settingsRoot, "settings.json")));
        Assert.True(File.Exists(Path.Combine(notesRoot, note.Id, "meta.json")));
        Assert.True(File.Exists(Path.Combine(notesRoot, note.Id, "content.md")));
        Assert.False(Directory.Exists(Path.Combine(settingsRoot, "notes")));
    }

    [Fact]
    public void GetNotesRootFromStorageRoot_UsesNotesSubfolder()
    {
        var storageRoot = Path.Combine(_tempRoot, "storage-root");

        var notesRoot = StorageService.GetNotesRootFromStorageRoot(storageRoot);

        Assert.Equal(Path.Combine(storageRoot, "notes"), notesRoot);
    }

    [Fact]
    public void GetStorageRootFromSelectedFolder_AppendsScreenPinNotesFolder()
    {
        var selectedFolder = Path.Combine(_tempRoot, "selected");

        var storageRoot = StorageService.GetStorageRootFromSelectedFolder(selectedFolder);

        Assert.Equal(Path.Combine(selectedFolder, "ScreenPinNotes"), storageRoot);
    }

    [Fact]
    public void GetStorageRootFromSelectedFolder_DoesNotAppendDuplicateScreenPinNotesFolder()
    {
        var selectedFolder = Path.Combine(_tempRoot, "selected", "ScreenPinNotes");

        var storageRoot = StorageService.GetStorageRootFromSelectedFolder(selectedFolder);

        Assert.Equal(selectedFolder, storageRoot);
    }

    [Fact]
    public void GetSelectableFolderFromStorageRoot_UsesParentForScreenPinNotesFolder()
    {
        var storageRoot = Path.Combine(_tempRoot, "selected", "ScreenPinNotes");

        var selectedFolder = StorageService.GetSelectableFolderFromStorageRoot(storageRoot);

        Assert.Equal(Path.Combine(_tempRoot, "selected"), selectedFolder);
    }

    [Fact]
    public void GetStorageRootFromLegacyNotesRoot_UsesParentWhenFolderIsNotes()
    {
        var legacyNotesRoot = Path.Combine(_tempRoot, "ScreenPinNotes", "notes");

        var storageRoot = StorageService.GetStorageRootFromLegacyNotesRoot(legacyNotesRoot);

        Assert.Equal(Path.Combine(_tempRoot, "ScreenPinNotes"), storageRoot);
    }






}
