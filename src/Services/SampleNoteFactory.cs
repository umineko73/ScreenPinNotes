// ScreenStickyNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.IO;
using System.Text;
using System.Text.Json;
using ScreenStickyNotes.Models;

namespace ScreenStickyNotes.Services;

// 初回起動時に作成するサンプル付箋。中身は通常のノートと同じ形式
// （meta.json + content.md、将来的な画像などは assets\）で
// SampleNotes\{ja|en}\{markdown|usage}\ に実ファイルとして置き、
// ビルド・publish のたびに exe と同じフォルダへコピーしている
// （ScreenStickyNotes.csproj 参照）。座標・色・アイコン・タイトルは
// その meta.json の値をそのまま使う（Id・CreatedAt・UpdatedAt だけ
// ここで新規に発行する）。
//
// SampleNotes フォルダが見つからない、または meta.json/content.md が
// 揃っていない場合は、そのサンプルを黙ってスキップする。初回起動が
// 失敗するよりはサンプル無しで起動できるほうがよい。
public static class SampleNoteFactory
{
    private static string SampleRoot => Path.Combine(AppContext.BaseDirectory, "SampleNotes");
    private static readonly JsonSerializerOptions JsonOpts = new();

    public static List<StickyNote> CreateInitialNotes(AppSettings settings, StorageService? storage = null)
    {
        storage ??= new StorageService();
        var now = DateTime.Now;
        var lang = UsesEnglishLanguage(settings) ? "en" : "ja";
        var notes = new List<StickyNote>();

        foreach (var name in new[] { "markdown", "usage" })
        {
            if (!TryLoadSample(lang, name, out var note))
                continue;

            note.CreatedAt = now;
            note.UpdatedAt = now;
            CopyAssets(Path.Combine(SampleRoot, lang, name, "assets"), note.Id, storage);
            notes.Add(note);
            now = now.AddMilliseconds(1); // 読み込み順を作成日時に反映する
        }

        return notes;
    }

    private static bool TryLoadSample(string lang, string name, out StickyNote note)
    {
        note = null!;
        var dir = Path.Combine(SampleRoot, lang, name);
        var metaPath = Path.Combine(dir, "meta.json");
        var contentPath = Path.Combine(dir, "content.md");

        if (!File.Exists(metaPath) || !File.Exists(contentPath))
            return false;

        try
        {
            var loaded = JsonSerializer.Deserialize<StickyNote>(
                File.ReadAllText(metaPath, Encoding.UTF8), JsonOpts);
            if (loaded == null)
                return false;

            loaded.Content = File.ReadAllText(contentPath, Encoding.UTF8);
            note = loaded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyAssets(string sourceAssetsDir, string noteId, StorageService storage)
    {
        if (!Directory.Exists(sourceAssetsDir))
            return;

        var destDir = storage.GetNoteAssetsDirectoryPath(noteId);
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceAssetsDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
    }

    private static bool UsesEnglishLanguage(AppSettings settings)
        => string.Equals(settings.Language, "en", StringComparison.OrdinalIgnoreCase);
}
