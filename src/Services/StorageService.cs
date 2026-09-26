// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3 of the License.
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
        return Path.Combine(appDataDir, "ScreenPinNotes");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // ─── インスタンスごとの保存先 ──────────────────────────────────
    // 通常は上記の静的な既定フォルダを使うが、テストでは互いに独立した
    // 一時フォルダを渡してデータを隔離できるようにする。

    private readonly string _settingsRoot;
    private readonly string _notesRoot;
    private readonly string _settingsPath;
    private readonly NoteSaveCoordinator _saves;
    private bool _settingsNeedBackup;

    public StorageService() : this(AppRoot) { }

    public StorageService(string dataRoot) : this(dataRoot, Path.Combine(Path.GetFullPath(dataRoot), "notes")) { }

    public StorageService(string settingsRoot, string notesRoot)
    {
        _settingsRoot = Path.GetFullPath(settingsRoot);
        _notesRoot = Path.GetFullPath(notesRoot);
        _settingsPath = Path.Combine(_settingsRoot, "settings.json");
        _saves = new NoteSaveCoordinator(WriteSnapshot);
    }

    public string NotesRoot => _notesRoot;

    public sealed record ImportResult(int ImportedCount, int SkippedCount);

    public StorageService WithNotesRoot(string notesRoot)
        => new(_settingsRoot, notesRoot) { _settingsNeedBackup = _settingsNeedBackup };

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

    public AppSettings LoadSettings() => LoadSettingsWithDiagnostics().Value;

    public StorageLoadResult<AppSettings> LoadSettingsWithDiagnostics()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(_settingsPath, Encoding.UTF8), JsonOptions)
                ?? throw new JsonException("Settings must contain an object.");
            settings.Normalize();
            _settingsNeedBackup = false;
            return new(settings, []);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(CreateDefaultSettings(), []);
        }
        catch (Exception ex)
        {
            _settingsNeedBackup = true;
            ErrorReporter.ReportNonFatal("Load settings", ex);
            return new(CreateDefaultSettings(), [StorageLoadIssue.From(_settingsPath, ex)]);
        }
    }

    private static AppSettings CreateDefaultSettings()
    {
        var defaults = AppSettings.CreateDefault();
        defaults.Normalize();
        return defaults;
    }

    public void SaveSettings(AppSettings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(_settingsRoot);
        if (_settingsNeedBackup)
        {
            // If preservation fails, abort the save instead of destroying the original.
            var backup = _settingsPath + ".corrupt-" + Guid.NewGuid().ToString("N") + ".bak";
            File.Copy(_settingsPath, backup);
            _settingsNeedBackup = false;
        }
        AtomicWrite(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    // ─── 読み込み ────────────────────────────────────────────────

    public List<StickyNote> Load(int externalTailLineCount = DefaultExternalTailLineCount)
        => LoadWithDiagnostics(externalTailLineCount).Value;

    public StorageLoadResult<List<StickyNote>> LoadWithDiagnostics(
        int externalTailLineCount = DefaultExternalTailLineCount, bool refreshExternalContent = true)
    {
        var notes = new List<StickyNote>();
        var issues = new List<StorageLoadIssue>();
        string[] directories;
        try { directories = Directory.GetDirectories(_notesRoot); }
        catch (DirectoryNotFoundException) { return new(notes, issues); }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Load notes directory", ex);
            return new(notes, [StorageLoadIssue.From(_notesRoot, ex)]);
        }
        foreach (var dir in directories)
        {
            // インポートの作業用フォルダ（.import-* / .backup-*）は付箋ではない。
            // 後片付けに失敗して残っても、古い付箋が2枚目として出てこないようにする。
            if (Path.GetFileName(dir).StartsWith('.')) continue;
            var metaPath = Path.Combine(dir, "meta.json");
            try
            {
                var note = JsonSerializer.Deserialize<StickyNote>(
                    File.ReadAllText(metaPath, Encoding.UTF8), JsonOptions);
                if (note == null) throw new JsonException("Note metadata must contain an object.");

                var noteId = Path.GetFileName(dir);
                if (!IsSafeNoteId(noteId))
                    throw new JsonException("Invalid note directory name.");
                note.Id = noteId;

                var contentPath = Path.Combine(dir, "content.md");
                try { note.Content = File.ReadAllText(contentPath, Encoding.UTF8); }
                catch (FileNotFoundException) { note.Content = ""; }
                _saves.Remember(NoteSnapshot.Capture(note));
                // 外部ファイルが一時的に読めない場合は content.md のキャッシュを
                // エラー文言で潰さず、直前の内容を保持する。
                if (refreshExternalContent && note.IsExternalContent && TryReadExternalContentForDisplay(note, externalTailLineCount, out var externalContent))
                    note.Content = externalContent;
                // この機能追加より前に保存されたリマインダーは ShowAlert を持たない
                // （null）。従来どおりアラートを出す side に固定して書き戻す。
                // これをしないと、リマインダーダイアログを開いただけの再保存で
                // チェックボックスの見た目どおり false が書き込まれてしまう。
                if (note.Reminder is { ShowAlert: null } reminder)
                    reminder.ShowAlert = true;
                // 音も同じ理由で、読み込んだ時点の振る舞い（通知ウィンドウのときだけ鳴る）に固定する。
                if (note.Reminder is { PlaySound: null } soundless)
                    soundless.PlaySound = soundless.PlaysSound;
                notes.Add(note);
            }
            catch (FileNotFoundException ex) when (ex.FileName == metaPath)
            {
                // Asset-only directories can exist before the first save.
            }
            catch (Exception ex)
            {
                ErrorReporter.ReportNonFatal($"Load note {dir}", ex);
                issues.Add(StorageLoadIssue.From(dir, ex));
            }
        }

        // 作成日時順に並べて返す
        notes.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
        return new(notes, issues);
    }

    // ─── 保存（全件） ────────────────────────────────────────────

    // Save は書き込みのみを行い、フォルダの削除は一切しない。
    // 以前は「リストに無いフォルダを消す」実装だったが、アプリが二重起動すると
    // 古いインスタンスの保存で他方のノートが消える事故が起きた。
    // 削除はユーザーが明示的に削除したときの DeleteNote だけが行う。
    public void Save(IEnumerable<StickyNote> notes)
    {
        Directory.CreateDirectory(_notesRoot);
        NoteSaveCoordinator.SaveAll(notes.Select<StickyNote, Action>(note => () => SaveNote(note)));
    }

    // ─── 保存（1件） ─────────────────────────────────────────────

    public void SaveNote(StickyNote note)
    {
        var dir = GetNoteDirectoryPath(note.Id);
        if (!File.Exists(Path.Combine(dir, "meta.json")) || !File.Exists(Path.Combine(dir, "content.md")))
            _saves.Forget(note.Id);
        _saves.Save(note);
    }

    // ─── 削除（1件） ─────────────────────────────────────────────

    public void DeleteNote(string id)
    {
        if (!TryGetNoteDirectoryPath(id, out var dir))
            return;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        _saves.Forget(id);
    }

    public void ExportNotesToZip(string zipPath)
    {
        var fullZipPath = Path.GetFullPath(zipPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullZipPath)!);
        if (File.Exists(fullZipPath))
            File.Delete(fullZipPath);

        using var archive = ZipFile.Open(fullZipPath, ZipArchiveMode.Create);
        if (!Directory.Exists(_notesRoot))
            return;

        foreach (var file in Directory.EnumerateFiles(_notesRoot, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFullPath(file), fullZipPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var relativePath = Path.GetRelativePath(_notesRoot, file)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            archive.CreateEntryFromFile(file, "notes/" + relativePath, CompressionLevel.Optimal);
        }
    }

    public enum ImportConflictAction { Overwrite, Rename, Skip }

    public ImportResult ImportNotesFromZip(string zipPath,
        Func<StickyNote, ImportConflictAction>? resolveConflict = null)
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

            Directory.CreateDirectory(_notesRoot);
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

                var overwrite = false;
                if (Directory.Exists(targetDir))
                {
                    var action = resolveConflict?.Invoke(note) ?? ImportConflictAction.Rename;
                    if (action == ImportConflictAction.Skip)
                    {
                        skipped++;
                        continue;
                    }
                    overwrite = action == ImportConflictAction.Overwrite;
                    if (!overwrite)
                    {
                        do { note.Id = Guid.NewGuid().ToString(); }
                        while (Directory.Exists(GetNoteDirectoryPath(note.Id)));
                        targetDir = GetNoteDirectoryPath(note.Id);
                    }
                }

                // Prepare on the destination volume, then swap directories. Never
                // remove the existing note when copying or metadata writing fails.
                var prepared = Path.Combine(_notesRoot, ".import-" + Guid.NewGuid().ToString("N"));
                var backup = Path.Combine(_notesRoot, ".backup-" + Guid.NewGuid().ToString("N"));
                try
                {
                    // content.md はここで既に正しくコピーされているので、
                    // 外部ファイルノートの内容をインポート先マシンで再解決して
                    // 上書きしないよう meta.json だけを書き直す。
                    CopyDirectory(sourceDir, prepared);
                    WriteNoteMetaOnly(prepared, note);
                    if (overwrite) Directory.Move(targetDir, backup);
                    try { Directory.Move(prepared, targetDir); }
                    catch
                    {
                        if (overwrite) Directory.Move(backup, targetDir);
                        throw;
                    }
                    _saves.Forget(note.Id);
                    imported++;
                    TryDeleteDirectory(backup);
                }
                catch (Exception ex)
                {
                    skipped++;
                    ErrorReporter.ReportNonFatal($"Import note {note.Id}", ex);
                }
                finally { TryDeleteDirectory(prepared); }
            }
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }

        return new ImportResult(imported, skipped);
    }

    // ─── 内部：ファイル書き込み（アトミック） ───────────────────

    private void WriteSnapshot(NoteSnapshot snapshot)
    {
        var dir = GetNoteDirectoryPath(snapshot.Id);
        Directory.CreateDirectory(dir);
        AtomicWrite(Path.Combine(dir, "meta.json"), snapshot.Metadata);
        AtomicWrite(Path.Combine(dir, "content.md"), snapshot.Content);
    }

    private static void WriteNoteMetaOnly(string dir, StickyNote note)
    {
        Directory.CreateDirectory(dir);

        // meta.json（Content は [JsonIgnore] により除外される）
        AtomicWrite(Path.Combine(dir, "meta.json"),
            JsonSerializer.Serialize(note, JsonOptions));
    }

    /// <summary>tail 表示が設定を持たない呼び出しで使う既定の行数。</summary>
    public const int DefaultExternalTailLineCount = ExternalContentReader.DefaultExternalTailLineCount;

    public static string ReadExternalContent(StickyNote note, int tailLineCount = DefaultExternalTailLineCount)
        => ExternalContentReader.ReadExternalContent(note, tailLineCount);

    public static bool TryReadExternalContent(StickyNote note, out string content)
        => ExternalContentReader.TryReadExternalContent(note, out content);

    public static bool TryReadExternalContentForDisplay(StickyNote note, int tailLineCount, out string content)
        => ExternalContentReader.TryReadExternalContentForDisplay(note, tailLineCount, out content);

    private static void AtomicWrite(string path, string content)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content, Encoding.UTF8);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private bool TryGetNoteDirectoryPath(string id, out string dir)
    {
        dir = "";
        if (!IsSafeNoteId(id))
            return false;

        var fullPath = Path.GetFullPath(Path.Combine(_notesRoot, id));
        var notesRoot = Path.GetFullPath(_notesRoot) + Path.DirectorySeparatorChar;
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
