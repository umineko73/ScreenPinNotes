// ScreenStickyNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using ScreenStickyNotes.Models;

namespace ScreenStickyNotes.Services;

public static class LocalizationService
{
    private static AppSettings Settings => App.Current.Settings;

    public static string T(string key)
    {
        var english = string.Equals(Settings.Language, "en", StringComparison.OrdinalIgnoreCase);
        return key switch
        {
            "TrayShowAll" => english ? "Show all" : "全表示",
            "TrayHideAll" => english ? "Hide all" : "全非表示",
            "TrayNewNote" => english ? "New note" : "新規付箋作成",
            "TrayStartup" => english ? "Start with Windows" : "スタートアップに登録",
            "TrayLanguage" => english ? "Language" : "言語切り替え",
            "TrayLanguageJapanese" => english ? "Japanese" : "日本語",
            "TrayLanguageEnglish" => english ? "English" : "英語",
            "TrayDarkMode" => english ? "Dark mode" : "ダークモード",
            "TrayAbout" => english ? "About ScreenStickyNotes" : "ScreenStickyNotes について",
            "TrayExit" => english ? "Exit" : "終了",

            "AboutTitle" => english ? "About ScreenStickyNotes" : "ScreenStickyNotes について",
            "AboutDescription" => english
                ? "A desktop sticky notes app for Windows 11."
                : "Windows 11 向けのデスクトップ付箋アプリです。",
            "AboutLicense" => english
                ? "License: GNU General Public License v3.0 or later"
                : "ライセンス: GNU General Public License v3.0 以降",
            "Close" => english ? "Close" : "閉じる",

            "SampleMarkdownTitle" => english ? "Markdown sample" : "Markdown サンプル",
            "SampleUsageTitle" => english ? "How to use" : "使い方",

            "NoMemo" => english ? "(No memo)" : "（メモなし）",
            "EditBodyTooltip" => english ? "Double-click to edit" : "ダブルクリックして編集",
            "TitleFallbackTooltip" => english
                ? "Leave blank to show the first body line"
                : "空欄なら本文の1行目を表示します",
            "AddNoteTooltip" => english ? "Add a new note" : "新しい付箋を追加",
            "TopmostTooltip" => english ? "Always on top" : "常に最前面",
            "FoldTooltip" => english ? "Fold / unfold" : "折りたたみ / 展開",
            "FontSmallerTooltip" => english ? "Smaller body font (current {0}pt)" : "本文のフォントを小さく (現在 {0}pt)",
            "FontLargerTooltip" => english ? "Larger body font (current {0}pt)" : "本文のフォントを大きく (現在 {0}pt)",
            "TitleSmallerTooltip" => english ? "Smaller title font (current {0}pt)" : "タイトルのフォントを小さく (現在 {0}pt)",
            "TitleLargerTooltip" => english ? "Larger title font (current {0}pt)" : "タイトルのフォントを大きく (現在 {0}pt)",
            "FontTooltip" => english ? "Change font" : "フォントを変更",
            "IconTooltip" => english ? "Change icon" : "アイコンを変更",
            "NoIconTooltip" => english ? "No icon" : "アイコンなし",
            "ColorTooltip" => english ? "Change color" : "色を変更",

            "Cut" => english ? "Cut" : "切り取り",
            "Copy" => english ? "Copy" : "コピー",
            "Paste" => english ? "Paste" : "貼り付け",
            "SelectAll" => english ? "Select all" : "すべて選択",
            "OpenLink" => english ? "Open link" : "リンクを開く",
            "ConvertLink" => english ? "Convert to link" : "リンクとして変換",
            "Delete" => english ? "Delete" : "削除",
            "EditTitle" => english ? "Edit title" : "タイトルを編集",
            "ZOrder" => english ? "Z order" : "重なり順",
            "BringToFront" => english ? "Bring to front" : "前面へ移動",
            "SendToBack" => english ? "Send to back" : "背面へ移動",
            "ImageSmaller" => english ? "Smaller image" : "画像を小さく",
            "ImageLarger" => english ? "Larger image" : "画像を大きく",
            "ImageOriginal" => english ? "Original size" : "元のサイズ",

            "BodySize" => english ? "Body {0:0}pt" : "本文 {0:0}pt",
            "TitleSize" => english ? "Title {0:0}pt" : "タイトル {0:0}pt",
            "DeleteConfirmTitle" => english ? "Confirm" : "確認",
            "DeleteConfirmMessage" => english ? "Delete this note?" : "この付箋を削除しますか？",
            _ => key,
        };
    }
}
