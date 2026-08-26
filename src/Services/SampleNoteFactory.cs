// ScreenStickyNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using ScreenStickyNotes.Models;

namespace ScreenStickyNotes.Services;

public static class SampleNoteFactory
{
    public static List<StickyNote> CreateInitialNotes(AppSettings settings)
    {
        var now = DateTime.Now;
        var layout = settings.Layout;
        var markdown = new StickyNote
        {
            X = layout.NewNoteBaseX,
            Y = layout.NewNoteBaseY,
            Width = Math.Max(layout.DefaultNoteWidth, 430),
            Height = Math.Max(layout.DefaultNoteHeight, 540),
            Title = LocalizationService.T("SampleMarkdownTitle"),
            Content = UsesEnglishLanguage(settings) ? EnglishMarkdownSample : JapaneseMarkdownSample,
            ColorKey = "sky",
            Icon = "📝",
            CreatedAt = now,
            UpdatedAt = now,
        };

        var usage = new StickyNote
        {
            X = layout.NewNoteBaseX + layout.NewNoteCascadeStep,
            Y = layout.NewNoteBaseY + layout.NewNoteCascadeStep,
            Width = Math.Max(layout.DefaultNoteWidth, 390),
            Height = Math.Max(layout.DefaultNoteHeight, 420),
            Title = LocalizationService.T("SampleUsageTitle"),
            Content = UsesEnglishLanguage(settings) ? EnglishUsageSample : JapaneseUsageSample,
            ColorKey = "yellow",
            Icon = "💡",
            CreatedAt = now.AddMilliseconds(1),
            UpdatedAt = now.AddMilliseconds(1),
        };

        return [markdown, usage];
    }

    private static bool UsesEnglishLanguage(AppSettings settings)
        => string.Equals(settings.Language, "en", StringComparison.OrdinalIgnoreCase);

    private const string JapaneseMarkdownSample = """
# Markdown サンプル

これは **太字**、*斜体*、`インラインコード` の表示確認です。

## リスト

- 箇条書き
- URL自動リンク: https://example.com
- Windowsパス自動リンク: C:\Users

1. 番号付きリスト
2. [Markdownリンク](https://openai.com/)

## チェックリスト

- [x] 完了した項目
- [ ] 閲覧モードのままクリックして切り替え

> 引用の表示確認です。

---

| 書式 | 記法 | 表示 |
| --- | --- | --- |
| 太字 | `**text**` | **text** |
| リンク | `[OpenAI](https://openai.com/)` | [OpenAI](https://openai.com/) |

```csharp
var note = "Markdown対応";
Console.WriteLine(note);
```
""";

    private const string EnglishMarkdownSample = """
# Markdown sample

This note shows **bold**, *italic*, and `inline code`.

## Lists

- Bullet item
- Auto URL link: https://example.com
- Auto Windows path link: C:\Users

1. Numbered item
2. [Markdown link](https://openai.com/)

## Checklist

- [x] Completed item
- [ ] Click in view mode to toggle

> This is a quote.

---

| Format | Syntax | Rendered |
| --- | --- | --- |
| Bold | `**text**` | **text** |
| Link | `[OpenAI](https://openai.com/)` | [OpenAI](https://openai.com/) |

```csharp
var note = "Markdown ready";
Console.WriteLine(note);
```
""";

    private const string JapaneseUsageSample = """
# 使い方

## 基本操作

- 本文をダブルクリック: 編集モード
- Escape: 閲覧モードに戻る
- タイトルバーをドラッグ: 付箋を移動
- タイトルバーをクリック: 折りたたみ / 展開
- タイトル上で右クリック: タイトル編集、重なり順、削除

## タイトルバーのボタン

| ボタン | 動作 |
| --- | --- |
| ＋ | この付箋の色・アイコン・フォントを引き継いで追加 |
| 📌 | 常に最前面 |
| ▲ / ▼ | 折りたたみ / 展開 |

## タスクトレイ

- 左クリック: 全付箋の表示 / 非表示
- 右クリック: 新規作成、言語切り替え、ダークモード、終了

## 保存場所

`%APPDATA%\ScreenStickyNotes`
""";

    private const string EnglishUsageSample = """
# How to use

## Basics

- Double-click body: edit mode
- Escape: return to view mode
- Drag title bar: move the note
- Click title bar: fold / unfold
- Right-click title: edit title, z order, delete

## Title bar buttons

| Button | Action |
| --- | --- |
| + | Add a note using this note's color, icon, and font |
| Pin | Always on top |
| Up / Down | Fold / unfold |

## Tray icon

- Left-click: show / hide all notes
- Right-click: new note, language, dark mode, exit

## Data folder

`%APPDATA%\ScreenStickyNotes`
""";
}
