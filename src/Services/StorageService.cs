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

    /// <summary>実際に使用しているデータフォルダ。</summary>
    public static string DataRoot => AppRoot;

    /// <summary>アプリケーション全体の設定ファイル。</summary>
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

    // 新形式: notes/{id}/meta.json + content.md
    private static readonly string NotesDir = Path.Combine(AppRoot, "notes");

    // 旧形式（移行元）
    private static readonly string LegacyFile = Path.Combine(AppRoot, "notes.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ─── アプリケーション設定 ───────────────────────────────────

    public AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
            var defaults = new AppSettings();
            defaults.Normalize();
            return defaults;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(SettingsPath, Encoding.UTF8), JsonOpts)
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
        Directory.CreateDirectory(AppRoot);
        AtomicWrite(SettingsPath, JsonSerializer.Serialize(settings, JsonOpts));
    }

    // ─── 読み込み ────────────────────────────────────────────────

    public List<StickyNote> Load()
    {
        MigrateFromLegacy();

        if (!Directory.Exists(NotesDir)) return [];

        var notes = new List<StickyNote>();
        foreach (var dir in Directory.GetDirectories(NotesDir))
        {
            var metaPath = Path.Combine(dir, "meta.json");
            if (!File.Exists(metaPath)) continue;
            try
            {
                var note = JsonSerializer.Deserialize<StickyNote>(
                    File.ReadAllText(metaPath, Encoding.UTF8), JsonOpts);
                if (note == null) continue;

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
        Directory.CreateDirectory(NotesDir);
        foreach (var note in notes)
            WriteNote(note);
    }

    // ─── 保存（1件） ─────────────────────────────────────────────

    public void SaveNote(StickyNote note) => WriteNote(note);

    // ─── 削除（1件） ─────────────────────────────────────────────

    public void DeleteNote(string id)
    {
        var dir = Path.Combine(NotesDir, id);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    // ─── 内部：ファイル書き込み（アトミック） ───────────────────

    private static void WriteNote(StickyNote note)
    {
        var dir = Path.Combine(NotesDir, note.Id);
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

    // ─── 旧形式からの移行 ────────────────────────────────────────

    private static void MigrateFromLegacy()
    {
        if (!File.Exists(LegacyFile)) return;
        try
        {
            var json   = File.ReadAllText(LegacyFile, Encoding.UTF8);
            var legacy = JsonSerializer.Deserialize<List<LegacyNote>>(json, JsonOpts);
            if (legacy != null)
            {
                Directory.CreateDirectory(NotesDir);
                foreach (var old in legacy)
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
            // 旧ファイルを .bak にリネームして保持
            File.Move(LegacyFile, LegacyFile + ".bak", overwrite: true);
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
