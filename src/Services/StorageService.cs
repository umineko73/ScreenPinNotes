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
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ScreenPinNotes.Models;

namespace ScreenPinNotes.Services;

public class StorageService
{
    // 環境変数 SCREENPINNOTES_DATA でデータ保存先を差し替えられる。
    // テストを実ユーザーのデータから隔離するために使う。
    public const string DataDirEnvVar = "SCREENPINNOTES_DATA";

    private static readonly string AppRoot = ResolveAppRoot();

    /// <summary>実際に使用しているデータフォルダ（アプリ全体で共有する既定値）。</summary>
    public static string DataRoot => AppRoot;

    /// <summary>既定の保存ルートフォルダ。</summary>
    public static string DefaultStorageRoot => AppRoot;

    /// <summary>既定のノート保存フォルダ。</summary>
    public static string DefaultNotesRoot => GetNotesRootFromStorageRoot(DefaultStorageRoot);

    /// <summary>アプリケーション全体の設定ファイル（既定のデータフォルダ基準）。</summary>
    public static string SettingsPath => Path.Combine(AppRoot, "settings.json");

    private static string ResolveAppRoot()
    {
        var custom = Environment.GetEnvironmentVariable(DataDirEnvVar);
        if (!string.IsNullOrWhiteSpace(custom))
            return Path.GetFullPath(custom);

        var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var newRoot = Path.Combine(appDataDir, "ScreenPinNotes");
        return newRoot;
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ─── インスタンスごとの保存先 ──────────────────────────────────
    // 通常は上記の静的な既定フォルダを使うが、テストでは互いに独立した
    // 一時フォルダを渡してデータを隔離できるようにする。

    private readonly string _root;
    private readonly string _notesDir;
    private readonly string _settingsPath;

    public StorageService() : this(AppRoot) { }

    public StorageService(string dataRoot) : this(dataRoot, Path.Combine(Path.GetFullPath(dataRoot), "notes")) { }

    public StorageService(string settingsRoot, string notesRoot)
    {
        _root = Path.GetFullPath(settingsRoot);
        _notesDir = Path.GetFullPath(notesRoot);
        _settingsPath = Path.Combine(_root, "settings.json");
    }

    public string NotesRoot => _notesDir;

    public sealed record ImportResult(int ImportedCount, int SkippedCount);

    public StorageService WithNotesRoot(string notesRoot)
        => new(_root, notesRoot);

    public StorageService WithStorageRoot(string storageRoot)
        => WithNotesRoot(GetNotesRootFromStorageRoot(storageRoot));

    public static string GetNotesRootFromStorageRoot(string storageRoot)
        => Path.Combine(Path.GetFullPath(storageRoot), "notes");

    public static string GetStorageRootFromSelectedFolder(string selectedFolder)
    {
        var fullPath = Path.GetFullPath(selectedFolder);
        return string.Equals(Path.GetFileName(fullPath), "ScreenPinNotes", StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : Path.Combine(fullPath, "ScreenPinNotes");
    }

    public static string GetSelectableFolderFromStorageRoot(string storageRoot)
    {
        var fullPath = Path.GetFullPath(storageRoot);
        return string.Equals(Path.GetFileName(fullPath), "ScreenPinNotes", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(fullPath) ?? fullPath
            : fullPath;
    }

    /// <summary>
    /// settings.json をアプリ設定として読み込む前に、実際に使われる notes フォルダを
    /// 軽く覗き見る。二重起動防止のミューテックスキーを、DataRoot だけでなく
    /// 実際の notes フォルダ（StorageRoot、または移行前の旧 NotesRoot）に基づいて
    /// 決められるようにするためのもの。設定ファイルが無い/壊れている/notes フォルダが
    /// 未設定の場合は null（呼び出し側は既定の notes フォルダにフォールバックする）。
    /// </summary>
    public static string? PeekConfiguredNotesRoot()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath, Encoding.UTF8));

            if (doc.RootElement.TryGetProperty("StorageRoot", out var storageRootEl) &&
                storageRootEl.GetString() is { Length: > 0 } storageRoot)
                return GetNotesRootFromStorageRoot(storageRoot);

            if (doc.RootElement.TryGetProperty("NotesRoot", out var notesRootEl) &&
                notesRootEl.GetString() is { Length: > 0 } legacyNotesRoot)
                return Path.GetFullPath(legacyNotesRoot);

            return null;
        }
        catch
        {
            return null;
        }
    }

    public static string GetStorageRootFromLegacyNotesRoot(string notesRoot)
    {
        var fullPath = Path.GetFullPath(notesRoot);
        return string.Equals(Path.GetFileName(fullPath), "notes", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(fullPath) ?? fullPath
            : fullPath;
    }

    public string GetNoteDirectoryPath(string id)
    {
        if (!TryGetNoteDirectoryPath(id, out var dir))
            throw new ArgumentException("Invalid note id.", nameof(id));
        return dir;
    }

    public string GetNoteAssetsDirectoryPath(string id)
        => Path.Combine(GetNoteDirectoryPath(id), "assets");

    // ─── アプリケーション設定 ───────────────────────────────────

    public AppSettings LoadSettings()
    {
        if (!File.Exists(_settingsPath))
        {
            var defaults = AppSettings.CreateDefault();
            defaults.Normalize();
            return defaults;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(_settingsPath, Encoding.UTF8), JsonOpts)
                ?? AppSettings.CreateDefault();
            settings.Normalize();
            return settings;
        }
        catch
        {
            var defaults = AppSettings.CreateDefault();
            defaults.Normalize();
            return defaults;
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(_root);
        AtomicWrite(_settingsPath, JsonSerializer.Serialize(settings, JsonOpts));
    }

    // ─── 読み込み ────────────────────────────────────────────────

    public List<StickyNote> Load()
    {

        if (!Directory.Exists(_notesDir)) return [];

        var notes = new List<StickyNote>();
        foreach (var dir in Directory.GetDirectories(_notesDir))
        {
            var metaPath = Path.Combine(dir, "meta.json");
            if (!File.Exists(metaPath)) continue;
            try
            {
                var note = JsonSerializer.Deserialize<StickyNote>(
                    File.ReadAllText(metaPath, Encoding.UTF8), JsonOpts);
                if (note == null) continue;

                var noteId = Path.GetFileName(dir);
                if (!IsSafeNoteId(noteId))
                    continue;
                note.Id = noteId;

                var contentPath = Path.Combine(dir, "content.md");
                note.Content = File.Exists(contentPath)
                    ? File.ReadAllText(contentPath, Encoding.UTF8)
                    : "";
                // 外部ファイルが一時的に読めない場合は content.md のキャッシュを
                // エラー文言で潰さず、直前の内容を保持する。
                if (note.IsExternalContent && TryReadExternalContent(note, out var externalContent))
                    note.Content = externalContent;
                notes.Add(note);
            }
            catch { /* 壊れたノートはスキップ */ }
        }

        // 作成日時順に並べて返す
        notes.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
        return notes;
    }

    // ─── 保存（全件） ────────────────────────────────────────────

    // Save は書き込みのみを行い、フォルダの削除は一切しない。
    // 以前は「リストに無いフォルダを消す」実装だったが、アプリが二重起動すると
    // 古いインスタンスの保存で他方のノートが消える事故が起きた。
    // 削除はユーザーが明示的に削除したときの DeleteNote だけが行う。
    public void Save(IEnumerable<StickyNote> notes)
    {
        Directory.CreateDirectory(_notesDir);
        foreach (var note in notes)
            WriteNote(note);
    }

    // ─── 保存（1件） ─────────────────────────────────────────────

    public void SaveNote(StickyNote note) => WriteNote(note);

    // ─── 削除（1件） ─────────────────────────────────────────────

    public void DeleteNote(string id)
    {
        if (!TryGetNoteDirectoryPath(id, out var dir))
            return;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    public void ExportNotesToZip(string zipPath)
    {
        var fullZipPath = Path.GetFullPath(zipPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullZipPath)!);
        if (File.Exists(fullZipPath))
            File.Delete(fullZipPath);

        using var archive = ZipFile.Open(fullZipPath, ZipArchiveMode.Create);
        if (!Directory.Exists(_notesDir))
            return;

        foreach (var file in Directory.EnumerateFiles(_notesDir, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFullPath(file), fullZipPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var relativePath = Path.GetRelativePath(_notesDir, file)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            archive.CreateEntryFromFile(file, "notes/" + relativePath, CompressionLevel.Optimal);
        }
    }

    public ImportResult ImportNotesFromZip(string zipPath)
    {
        var fullZipPath = Path.GetFullPath(zipPath);
        var stagingRoot = Path.Combine(Path.GetTempPath(), "ScreenPinNotesImport", Guid.NewGuid().ToString("N"));
        var imported = 0;
        var skipped = 0;

        try
        {
            ExtractZipSafely(fullZipPath, stagingRoot);

            var extractedNotesRoot = Directory.Exists(Path.Combine(stagingRoot, "notes"))
                ? Path.Combine(stagingRoot, "notes")
                : stagingRoot;
            var stagingStorage = new StorageService(stagingRoot, extractedNotesRoot);
            var notes = stagingStorage.Load();

            Directory.CreateDirectory(_notesDir);
            foreach (var note in notes)
            {
                if (!TryGetNoteDirectoryPath(note.Id, out var targetDir))
                {
                    skipped++;
                    continue;
                }

                var sourceDir = stagingStorage.GetNoteDirectoryPath(note.Id);
                if (!Directory.Exists(sourceDir))
                {
                    skipped++;
                    continue;
                }

                if (Directory.Exists(targetDir))
                {
                    note.Id = Guid.NewGuid().ToString();
                    targetDir = GetNoteDirectoryPath(note.Id);
                }

                try
                {
                    // content.md はここで既に正しくコピーされているので、
                    // 外部ファイルノートの内容をインポート先マシンで再解決して
                    // 上書きしないよう meta.json だけを書き直す。
                    CopyDirectory(sourceDir, targetDir);
                    WriteNoteMetaOnly(targetDir, note);
                    imported++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    ErrorReporter.ReportNonFatal($"Import note {note.Id}", ex);
                    if (Directory.Exists(targetDir))
                        TryDeleteDirectory(targetDir);
                }
            }
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }

        return new ImportResult(imported, skipped);
    }

    // ─── 内部：ファイル書き込み（アトミック） ───────────────────

    private void WriteNote(StickyNote note)
    {
        var dir = GetNoteDirectoryPath(note.Id);
        Directory.CreateDirectory(dir);

        WriteNoteMetaOnly(dir, note);

        // content.md
        AtomicWrite(Path.Combine(dir, "content.md"), note.Content);
    }

    private static void WriteNoteMetaOnly(string dir, StickyNote note)
    {
        Directory.CreateDirectory(dir);

        // meta.json（Content は [JsonIgnore] により除外される）
        AtomicWrite(Path.Combine(dir, "meta.json"),
            JsonSerializer.Serialize(note, JsonOpts));
    }

    public static string ReadExternalContent(StickyNote note)
    {
        var path = note.ExternalContentPath;
        if (string.IsNullOrWhiteSpace(path))
            return note.Content;

        try
        {
            var fullPath = Path.GetFullPath(path);
            return File.Exists(fullPath)
                ? File.ReadAllText(fullPath, Encoding.UTF8)
                : $"External file not found:\n{fullPath}";
        }
        catch (Exception ex)
        {
            return $"External file could not be read:\n{path}\n\n{ex.Message}";
        }
    }

    // 読み込みに失敗しても直前のキャッシュを壊さないための Try 版。
    // 一時的にファイルが読めない場合でも content.md 上のキャッシュを
    // エラー文言で上書きしないよう、呼び出し側は成功時のみ内容を反映する。
    public static bool TryReadExternalContent(StickyNote note, out string content)
    {
        var path = note.ExternalContentPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            content = "";
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                content = "";
                return false;
            }

            content = File.ReadAllText(fullPath, Encoding.UTF8);
            return true;
        }
        catch
        {
            content = "";
            return false;
        }
    }

    private static void AtomicWrite(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, Encoding.UTF8);
        File.Move(tmp, path, overwrite: true);
    }

    private bool TryGetNoteDirectoryPath(string id, out string dir)
    {
        dir = "";
        if (!IsSafeNoteId(id))
            return false;

        var fullPath = Path.GetFullPath(Path.Combine(_notesDir, id));
        var notesRoot = Path.GetFullPath(_notesDir) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(notesRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        dir = fullPath;
        return true;
    }

    private static bool IsSafeNoteId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "." or "..")
            return false;
        if (Path.IsPathRooted(id))
            return false;
        return id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
               !id.Contains(Path.DirectorySeparatorChar) &&
               !id.Contains(Path.AltDirectorySeparatorChar);
    }

    private static void ExtractZipSafely(string zipPath, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        var root = Path.GetFullPath(destinationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var normalizedName = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalizedName))
                continue;
            if (normalizedName.StartsWith("/", StringComparison.Ordinal) ||
                normalizedName.Split('/').Any(part => part is "" or "." or ".."))
            {
                continue;
            }

            var relativePath = normalizedName.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                continue;

            if (normalizedName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(fullPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            entry.ExtractToFile(fullPath, overwrite: true);
        }
    }

    internal static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, file);
            File.Copy(file, Path.Combine(target, relativePath), overwrite: false);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Temporary import cleanup failure is non-fatal.
        }
    }

}
