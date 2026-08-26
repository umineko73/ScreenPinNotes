// ScreenStickyNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.IO;
using ScreenStickyNotes.Models;

namespace ScreenStickyNotes.Services;

// 初回起動時に作成するサンプル付箋。中身（content.md、将来的な画像などの
// assets）は SampleNotes\{ja|en}\{markdown|usage}\ に実ファイルとして置き、
// ビルド・publish のたびに exe と同じフォルダへコピーしている
// （ScreenStickyNotes.csproj 参照）。ここでは配置・書式（座標・色・
// アイコン）だけを決め、本文はそのフォルダから読む。
//
// SampleNotes フォルダが見つからない場合（exe だけを取り出した等）は、
// そのサンプルを黙ってスキップする。初回起動が失敗するよりは
// サンプル無しで起動できるほうがよい。
public static class SampleNoteFactory
{
    private static string SampleRoot => Path.Combine(AppContext.BaseDirectory, "SampleNotes");

    public static List<StickyNote> CreateInitialNotes(AppSettings settings)
    {
        var now = DateTime.Now;
        var layout = settings.Layout;
        var lang = UsesEnglishLanguage(settings) ? "en" : "ja";
        var notes = new List<StickyNote>();

        if (TryLoadSample(lang, "markdown", out var markdownContent, out var markdownAssets))
        {
            var note = new StickyNote
            {
                X = layout.NewNoteBaseX,
                Y = layout.NewNoteBaseY,
                Width = Math.Max(layout.DefaultNoteWidth, 430),
                Height = Math.Max(layout.DefaultNoteHeight, 540),
                Title = LocalizationService.T("SampleMarkdownTitle"),
                Content = markdownContent,
                ColorKey = "sky",
                Icon = "📝",
                CreatedAt = now,
                UpdatedAt = now,
            };
            CopyAssets(markdownAssets, note.Id);
            notes.Add(note);
        }

        if (TryLoadSample(lang, "usage", out var usageContent, out var usageAssets))
        {
            var note = new StickyNote
            {
                X = layout.NewNoteBaseX + layout.NewNoteCascadeStep,
                Y = layout.NewNoteBaseY + layout.NewNoteCascadeStep,
                Width = Math.Max(layout.DefaultNoteWidth, 390),
                Height = Math.Max(layout.DefaultNoteHeight, 420),
                Title = LocalizationService.T("SampleUsageTitle"),
                Content = usageContent,
                ColorKey = "yellow",
                Icon = "💡",
                CreatedAt = now.AddMilliseconds(1),
                UpdatedAt = now.AddMilliseconds(1),
            };
            CopyAssets(usageAssets, note.Id);
            notes.Add(note);
        }

        return notes;
    }

    private static bool TryLoadSample(string lang, string name, out string content, out string assetsDir)
    {
        content = "";
        var dir = Path.Combine(SampleRoot, lang, name);
        var contentPath = Path.Combine(dir, "content.md");
        assetsDir = Path.Combine(dir, "assets");

        if (!File.Exists(contentPath))
            return false;

        try
        {
            content = File.ReadAllText(contentPath, System.Text.Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyAssets(string sourceAssetsDir, string noteId)
    {
        if (!Directory.Exists(sourceAssetsDir))
            return;

        var destDir = StorageService.GetNoteAssetsDirectory(noteId);
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceAssetsDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
    }

    private static bool UsesEnglishLanguage(AppSettings settings)
        => string.Equals(settings.Language, "en", StringComparison.OrdinalIgnoreCase);
}
