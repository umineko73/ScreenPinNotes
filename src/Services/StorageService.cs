// ScreenStickyNotes - a desktop sticky notes app for Windows 11
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
using System.Text;
using System.Text.Json;
using ScreenStickyNotes.Models;

namespace ScreenStickyNotes.Services;

public class StorageService
{
    // 環境変数 SCREENSTICKYNOTES_DATA でデータ保存先を差し替えられる。
    // テストを実ユーザーのデータから隔離するために使う。
    public const string DataDirEnvVar = "SCREENSTICKYNOTES_DATA";

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

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenStickyNotes");
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ─── インスタンスごとの保存先 ──────────────────────────────────
    // 通常は上記の静的な既定フォルダを使うが、テストでは互いに独立した
    // 一時フォルダを渡してデータを隔離できるようにする。

    private readonly string _root;
    private readonly string _notesDir;
    private readonly string _settingsPath;
    private readonly string _legacyFile;

    public StorageService() : this(AppRoot) { }

    public StorageService(string dataRoot) : this(dataRoot, Path.Combine(Path.GetFullPath(dataRoot), "notes")) { }

    public StorageService(string settingsRoot, string notesRoot)
    {
        _root = Path.GetFullPath(settingsRoot);
        _notesDir = Path.GetFullPath(notesRoot);
        _settingsPath = Path.Combine(_root, "settings.json");
        _legacyFile = Path.Combine(_root, "notes.json"); // 旧形式（移行元）
    }

    public string NotesRoot => _notesDir;

    public StorageService WithNotesRoot(string notesRoot)
        => new(_root, notesRoot);

    public StorageService WithStorageRoot(string storageRoot)
        => WithNotesRoot(GetNotesRootFromStorageRoot(storageRoot));

    public static string GetNotesRootFromStorageRoot(string storageRoot)
        => Path.Combine(Path.GetFullPath(storageRoot), "notes");

    public static string GetStorageRootFromSelectedFolder(string selectedFolder)
    {
        var fullPath = Path.GetFullPath(selectedFolder);
        return string.Equals(Path.GetFileName(fullPath), "ScreenStickyNotes", StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : Path.Combine(fullPath, "ScreenStickyNotes");
    }

    public static string GetSelectableFolderFromStorageRoot(string storageRoot)
    {
        var fullPath = Path.GetFullPath(storageRoot);
        return string.Equals(Path.GetFileName(fullPath), "ScreenStickyNotes", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(fullPath) ?? fullPath
            : fullPath;
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
            var defaults = new AppSettings();
            defaults.Normalize();
            return defaults;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(_settingsPath, Encoding.UTF8), JsonOpts)
                ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch
        {
            var defaults = new AppSettings();
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
        MigrateFromLegacy();

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

    // ─── 内部：ファイル書き込み（アトミック） ───────────────────

    private void WriteNote(StickyNote note)
    {
        var dir = GetNoteDirectoryPath(note.Id);
        Directory.CreateDirectory(dir);

        // meta.json（Content は [JsonIgnore] により除外される）
        AtomicWrite(Path.Combine(dir, "meta.json"),
            JsonSerializer.Serialize(note, JsonOpts));

        // content.md
        AtomicWrite(Path.Combine(dir, "content.md"), note.Content);
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

    // ─── 旧形式からの移行 ────────────────────────────────────────

    private void MigrateFromLegacy()
    {
        if (!File.Exists(_legacyFile)) return;
        try
        {
            var json   = File.ReadAllText(_legacyFile, Encoding.UTF8);
            var legacy = JsonSerializer.Deserialize<List<LegacyNote>>(json, JsonOpts);
            if (legacy != null)
            {
                Directory.CreateDirectory(_notesDir);
                foreach (var old in legacy)
                {
                    if (!IsSafeNoteId(old.Id))
                        continue;

                    WriteNote(new StickyNote
                    {
                        Id         = old.Id,
                        Content    = old.Content,
                        X          = old.X,         Y      = old.Y,
                        Width      = old.Width,      Height = old.Height,
                        ColorKey   = old.ColorKey,
                        FontFamily = old.FontFamily, FontSize = old.FontSize,
                        IsTopmost  = old.IsTopmost,  IsFolded = old.IsFolded,
                        CreatedAt  = old.CreatedAt,  UpdatedAt = old.UpdatedAt,
                    });
                }
            }
            // 旧ファイルを .bak にリネームして保持
            File.Move(_legacyFile, _legacyFile + ".bak", overwrite: true);
        }
        catch { /* 移行失敗は無視 */ }
    }

    // 旧 JSON 読み込み用（Content フィールドあり）
    private sealed class LegacyNote
    {
        public string   Id         { get; set; } = Guid.NewGuid().ToString();
        public string   Content    { get; set; } = "";
        public double   X          { get; set; } = 100;
        public double   Y          { get; set; } = 100;
        public double   Width      { get; set; } = 260;
        public double   Height     { get; set; } = 220;
        public string   ColorKey   { get; set; } = "yellow";
        public string   FontFamily { get; set; } = "Yu Gothic UI";
        public double   FontSize   { get; set; } = 13;
        public bool     IsTopmost  { get; set; }
        public bool     IsFolded   { get; set; }
        public DateTime CreatedAt  { get; set; } = DateTime.Now;
        public DateTime UpdatedAt  { get; set; } = DateTime.Now;
    }
}
