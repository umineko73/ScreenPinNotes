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
        return Path.Combine(appDataDir, "ScreenPinNotes");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // ─── インスタンスごとの保存先 ──────────────────────────────────
    // 通常は上記の静的な既定フォルダを使うが、テストでは互いに独立した
    // 一時フォルダを渡してデータを隔離できるようにする。

    private readonly string _settingsRoot;
    private readonly string _notesRoot;
    private readonly string _settingsPath;

    public StorageService() : this(AppRoot) { }

    public StorageService(string dataRoot) : this(dataRoot, Path.Combine(Path.GetFullPath(dataRoot), "notes")) { }

    public StorageService(string settingsRoot, string notesRoot)
    {
        _settingsRoot = Path.GetFullPath(settingsRoot);
        _notesRoot = Path.GetFullPath(notesRoot);
        _settingsPath = Path.Combine(_settingsRoot, "settings.json");
    }

    public string NotesRoot => _notesRoot;

    public sealed record ImportResult(int ImportedCount, int SkippedCount);

    public StorageService WithNotesRoot(string notesRoot)
        => new(_settingsRoot, notesRoot);

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
                File.ReadAllText(_settingsPath, Encoding.UTF8), JsonOptions)
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
        Directory.CreateDirectory(_settingsRoot);
        AtomicWrite(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    // ─── 読み込み ────────────────────────────────────────────────

    public List<StickyNote> Load(int externalTailLineCount = DefaultExternalTailLineCount)
    {

        if (!Directory.Exists(_notesRoot)) return [];

        var notes = new List<StickyNote>();
        foreach (var dir in Directory.GetDirectories(_notesRoot))
        {
            var metaPath = Path.Combine(dir, "meta.json");
            if (!File.Exists(metaPath)) continue;
            try
            {
                var note = JsonSerializer.Deserialize<StickyNote>(
                    File.ReadAllText(metaPath, Encoding.UTF8), JsonOptions);
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
                if (note.IsExternalContent && TryReadExternalContentForDisplay(note, externalTailLineCount, out var externalContent))
                    note.Content = externalContent;
                // この機能追加より前に保存されたリマインダーは ShowAlert を持たない
                // （null）。従来どおりアラートを出す side に固定して書き戻す。
                // これをしないと、リマインダーダイアログを開いただけの再保存で
                // チェックボックスの見た目どおり false が書き込まれてしまう。
                if (note.Reminder is { ShowAlert: null } reminder)
                    reminder.ShowAlert = true;
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
        Directory.CreateDirectory(_notesRoot);
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
            JsonSerializer.Serialize(note, JsonOptions));
    }

    /// <summary>tail 表示が設定を持たない呼び出しで使う既定の行数。</summary>
    public const int DefaultExternalTailLineCount = 200;

    public static string ReadExternalContent(StickyNote note, int tailLineCount = DefaultExternalTailLineCount)
    {
        var path = note.ExternalContentPath;
        if (string.IsNullOrWhiteSpace(path))
            return note.Content;

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                return $"External file not found:\n{fullPath}";

            return note.ExternalTailMode
                ? ReadTail(fullPath, Math.Max(1, tailLineCount))
                : ReadAllSharedText(fullPath);
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

            content = ReadAllSharedText(fullPath);
            return true;
        }
        catch
        {
            content = "";
            return false;
        }
    }

    /// <summary>
    /// 外部ファイルの全文を、書き手と共有したまま読む。ログのように別のプロセスが
    /// 開いたまま追記しているファイルは、<see cref="File.ReadAllText(string)"/>
    /// （FileShare.Read で開く）では「別のプロセスが使用中」となり読めない。
    /// </summary>
    private static string ReadAllSharedText(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // tail 表示のときは末尾の行数だけを読む Try 版。ノートの ExternalTailMode に
    // 応じて全文/tail のどちらを読むかを切り替えたい呼び出し側はこちらを使う。
    public static bool TryReadExternalContentForDisplay(StickyNote note, int tailLineCount, out string content)
        => note.ExternalTailMode
            ? TryReadExternalContentTail(note, tailLineCount, out content)
            : TryReadExternalContent(note, out content);

    private static bool TryReadExternalContentTail(StickyNote note, int tailLineCount, out string content)
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

            content = ReadTail(fullPath, Math.Max(1, tailLineCount));
            return true;
        }
        catch
        {
            content = "";
            return false;
        }
    }

    private const int TailReadChunkBytes = 64 * 1024;

    /// <summary>
    /// ファイル末尾の <paramref name="lineCount"/> 行だけを読む。育ち続けるログは
    /// 数百MBになり得るため、全文を読んでから split するのではなく末尾から
    /// チャンク単位で遡って改行を数え、必要な範囲が分かった時点でそこだけ返す。
    /// 改行 (0x0A) は UTF-8 の継続バイト（0x80-0xBF）にも先頭バイトにも現れないので、
    /// デコード前のバイト列を直接走査してよい。
    /// </summary>
    private static string ReadTail(string path, int lineCount)
    {
        // ログはローテーションで消されることもあるので、削除も妨げないで開く。
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        if (length == 0)
            return "";

        var buffer = new byte[TailReadChunkBytes];
        var newlinesNeeded = lineCount;
        var position = length;
        var foundBoundary = false;

        while (position > 0)
        {
            var chunkSize = (int)Math.Min(TailReadChunkBytes, position);
            position -= chunkSize;
            stream.Seek(position, SeekOrigin.Begin);
            var read = ReadExact(stream, buffer, chunkSize);
            for (var i = read - 1; i >= 0; i--)
            {
                if (buffer[i] != (byte)'\n')
                    continue;
                // ファイル末尾ちょうどの改行は最終行の終端でしかないので、
                // 区切りとしては数えない（数えると空行が1行増えて見える）。
                if (position + i == length - 1)
                    continue;

                if (--newlinesNeeded <= 0)
                {
                    position += i + 1;
                    foundBoundary = true;
                    break;
                }
            }
            if (foundBoundary)
                break;
        }

        var resultLength = (int)(length - position);
        var result = new byte[resultLength];
        stream.Seek(position, SeekOrigin.Begin);
        ReadExact(stream, result, resultLength);
        return Encoding.UTF8.GetString(result);
    }

    private static int ReadExact(Stream stream, byte[] buffer, int count)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = stream.Read(buffer, totalRead, count - totalRead);
            if (read == 0)
                break;
            totalRead += read;
        }

        return totalRead;
    }

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
