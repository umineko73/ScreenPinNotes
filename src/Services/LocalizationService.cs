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
            "TrayNoteManager" => english ? "Note list..." : "付箋一覧...",
            "TrayHiddenNotes" => english ? "Hidden notes" : "非表示の付箋",
            "TrayShowAllHiddenNotes" => english ? "Show all hidden notes" : "非表示付箋の全表示",
            "TrayNoHiddenNotes" => english ? "(None)" : "（なし）",
            "TrayNewNote" => english ? "New note" : "新規付箋作成",
            "TrayOpenExternalNote" => english ? "Open external file as note..." : "外部ファイルを付箋として開く...",
            "TraySettings" => english ? "Settings" : "設定",
            "TraySelectNotesRoot" => english ? "Select note folder..." : "保存フォルダを選択...",
            "TrayExportNotes" => english ? "Export notes..." : "付箋をエクスポート...",
            "TrayImportNotes" => english ? "Import notes..." : "付箋をインポート...",
            "TrayStartup" => english ? "Start with Windows" : "Windows 起動時に開始",
            "TrayTitlePreviewTooltip" => english ? "Title hover preview" : "タイトルのツールチップ",
            "TrayFoldAnimation" => english ? "Fold animation" : "折りたたみアニメーション",
            "TrayFoldButton" => english ? "Show fold button" : "折りたたみボタンを表示",
            "TrayLanguage" => english ? "Language" : "言語",
            "TrayLanguageJapanese" => english ? "Japanese" : "日本語",
            "TrayLanguageEnglish" => english ? "English" : "英語",
            "TrayDarkMode" => english ? "Dark mode" : "ダークモード",
            "TrayAbout" => english ? "About ScreenStickyNotes" : "ScreenStickyNotes について",
            "TrayExit" => english ? "Exit" : "終了",
            "SelectNotesRootTitle" => english ? "Select note folder" : "ノート保存フォルダの選択",
            "SelectNotesRootDescription" => english
                ? "Select the parent folder. A ScreenStickyNotes folder and its notes folder will be created inside it."
                : "親フォルダを選択してください。選択したフォルダ内に ScreenStickyNotes フォルダと notes フォルダを作成します。",
            "SelectNotesRootChangedTitle" => english ? "Note folder changed" : "保存フォルダを変更しました",
            "SelectNotesRootChangedMessage" => english
                ? "The storage folder has been changed. Notes from its ScreenStickyNotes\\notes folder are now loaded."
                : "保存フォルダを変更しました。選択したフォルダ内の ScreenStickyNotes\\notes フォルダから付箋を読み込みました。",
            "InitializeEmptyStorageTitle" => english ? "No notes found" : "付箋データがありません",
            "InitializeEmptyStorageMessage" => english
                ? "No notes were found in the selected storage folder. The app will create initial notes and start with them."
                : "選択した保存フォルダに付箋データがありません。初期ノートを作成して起動します。",
            "MoveNotesConfirmTitle" => english ? "Move existing notes?" : "既存の付箋を移動しますか？",
            "MoveNotesConfirmMessage" => english
                ? "The selected folder does not contain ScreenStickyNotes\\notes.\n\nYes: move the current notes folder to the selected folder.\nNo: create initial notes in the selected folder. The current notes folder will remain unchanged."
                : "選択したフォルダに ScreenStickyNotes\\notes フォルダがありません。\n\nはい: 現在の notes フォルダを選択したフォルダへ移動します。\nいいえ: 選択したフォルダに初期ノートを作成します。現在の notes フォルダはそのまま残ります。",
            "MoveNotesFailedTitle" => english ? "Move failed" : "移動に失敗しました",
            "MoveNotesFailedMessage" => english
                ? "The notes folder could not be moved. The storage folder was not changed. See the log for details."
                : "notes フォルダを移動できませんでした。保存フォルダは変更していません。詳細はログを確認してください。",
            "NotesZipFilter" => english ? "ScreenStickyNotes zip (*.zip)|*.zip" : "ScreenStickyNotes zip (*.zip)|*.zip",
            "ExportNotesTitle" => english ? "Export notes" : "付箋のエクスポート",
            "ExportNotesCompletedTitle" => english ? "Export complete" : "エクスポート完了",
            "ExportNotesCompletedMessage" => english
                ? "Notes were exported."
                : "付箋をエクスポートしました。",
            "ExportNotesFailedTitle" => english ? "Export failed" : "エクスポートに失敗しました",
            "ExportNotesFailedMessage" => english
                ? "Notes could not be exported. See the log for details."
                : "付箋をエクスポートできませんでした。詳細はログを確認してください。",
            "ImportNotesTitle" => english ? "Import notes" : "付箋のインポート",
            "ImportNotesConfirmTitle" => english ? "Import notes?" : "付箋をインポートしますか？",
            "ImportNotesConfirmMessage" => english
                ? "Notes from the selected zip will be added. Existing notes will not be overwritten; duplicate notes will be imported with new IDs."
                : "選択した zip の付箋を追加します。既存の付箋は上書きせず、ID が重複する付箋は新しい ID で取り込みます。",
            "ImportNotesCompletedTitle" => english ? "Import complete" : "インポート完了",
            "ImportNotesCompletedMessage" => english
                ? "Imported {0} note(s). Skipped {1} note(s)."
                : "{0} 件の付箋をインポートしました。{1} 件をスキップしました。",
            "ImportNotesFailedTitle" => english ? "Import failed" : "インポートに失敗しました",
            "ImportNotesFailedMessage" => english
                ? "Notes could not be imported. The current notes were left unchanged. See the log for details."
                : "付箋をインポートできませんでした。現在の付箋は変更していません。詳細はログを確認してください。",
            "ExternalNoteFileFilter" => english ? "Markdown/text files (*.md;*.txt)|*.md;*.txt|All files (*.*)|*.*" : "Markdown/テキストファイル (*.md;*.txt)|*.md;*.txt|すべてのファイル (*.*)|*.*",

            "AboutTitle" => english ? "About ScreenStickyNotes" : "ScreenStickyNotes について",
            "AboutDescription" => english
                ? "A desktop sticky notes app for Windows 11."
                : "Windows 11 向けのデスクトップ付箋アプリです。",
            "AboutLicense" => english
                ? "License: GNU General Public License v3.0 or later"
                : "ライセンス: GNU General Public License v3.0 以降",
            "Close" => english ? "Close" : "閉じる",
            "Cancel" => english ? "Cancel" : "キャンセル",

            "NoMemo" => english ? "(No memo)" : "（メモなし）",
            "EditBodyTooltip" => english ? "Double-click to edit" : "ダブルクリックして編集",
            "EditLockBodyTooltip" => english ? "Editing is locked" : "編集がロックされています",
            "EditLock" => english ? "Lock editing" : "編集をロック",
            "EditLockNotice" => english ? "Editing is locked" : "編集がロックされています",
            "EditLockDeleteBlockedTitle" => english ? "Editing locked" : "編集ロック中",
            "EditLockDeleteBlockedMessage" => english
                ? "This note is locked and cannot be deleted."
                : "この付箋は編集ロック中のため削除できません。",
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
            "PasteMarkdownLink" => english ? "Paste as Markdown link" : "Markdownリンクとして貼り付け",
            "MarkdownLinkLabelTitle" => english ? "Markdown link" : "Markdownリンク",
            "MarkdownLinkLabelPrompt" => english ? "Site name / display text:" : "サイト名 / 表示名:",
            "PasteExcelTable" => english ? "Paste table from Excel" : "Excelから表を貼り付け",
            "PasteExcelTableWithHeader" => english ? "Use first row as header" : "1行目を見出しにする",
            "PasteExcelTableWithoutHeader" => english ? "No header" : "見出しなし",
            "CopyExcelTable" => english ? "Copy table for Excel" : "Excelへ表をコピー",
            "SelectAll" => english ? "Select all" : "すべて選択",
            "OpenLink" => english ? "Open link" : "リンクを開く",
            "ConvertLink" => english ? "Convert to Markdown link" : "Markdownリンクに変換",
            "Delete" => english ? "Delete note" : "付箋の削除",
            "UnlinkExternalNote" => english ? "Unlink external file" : "外部ファイルのリンク解除",
            "HideNote" => english ? "Hide note" : "付箋を非表示",
            "EditTitle" => english ? "Edit title" : "タイトルを編集",
            "ZOrder" => english ? "Z order" : "重なり順",
            "Opacity" => english ? "Opacity" : "透明度",
            "SetUnfoldedPositionHere" => english ? "Open here" : "展開位置をここにする",
            "ExternalFile" => english ? "External file" : "外部ファイル",
            "OpenExternalFile" => english ? "Open external file" : "外部ファイルを開く",
            "OpenExternalFolder" => english ? "Open containing folder" : "外部ファイルのフォルダを開く",
            "ConvertExternalToNormal" => english ? "Convert to normal note" : "通常付箋に変換",
            "ReminderMenu" => english ? "Reminder..." : "リマインダー...",
            "ReminderDialogTitle" => english ? "Reminder" : "リマインダー",
            "ReminderDialogDescription" => english
                ? "Set a one-time reminder for this note."
                : "この付箋の単発リマインダーを設定します。",
            "ReminderDate" => english ? "Date" : "日付",
            "ReminderTime" => english ? "Time" : "時刻",
            "ReminderAfter5" => english ? "In 5 min" : "5分後",
            "ReminderAfter15" => english ? "In 15 min" : "15分後",
            "ReminderAfter60" => english ? "In 1 hour" : "1時間後",
            "ReminderTomorrow" => english ? "Tomorrow 9:00" : "明日 9:00",
            "ReminderClear" => english ? "Clear" : "解除",
            "ReminderInvalid" => english
                ? "Enter a valid date and time."
                : "正しい日付と時刻を入力してください。",
            "ReminderCleared" => english ? "Reminder cleared" : "リマインダーを解除しました",
            "ReminderSetMessage" => english ? "Reminder: {0}" : "リマインダー: {0}",
            "ReminderDueTitle" => english ? "Reminder" : "リマインダー",
            "ReminderDueMessage" => english
                ? "Reminder time: {0}"
                : "リマインダー時刻: {0}",
            "ReminderDismiss" => english ? "Done" : "完了",
            "ReminderSnooze5" => english ? "5 min" : "5分",
            "ReminderSnooze15" => english ? "15 min" : "15分",
            "ReminderSnooze60" => english ? "1 hour" : "1時間",
            "BringToFront" => english ? "Bring to front" : "前面へ移動",
            "SendToBack" => english ? "Send to back" : "背面へ移動",
            "ImageSmaller" => english ? "Smaller image" : "画像を小さく",
            "ImageLarger" => english ? "Larger image" : "画像を大きく",
            "ImageOriginal" => english ? "Original size" : "元のサイズ",
            "RemoveImageWidth" => english ? "Remove image size" : "画像サイズ指定を解除",
            "FitWindowToImage" => english ? "Fit window to image" : "画像にウィンドウをフィット",
            "FitWindowToImages" => english ? "Fit window to images" : "ウィンドウを画像にフィット",
            "DetachImageFromNote" => english ? "Remove image from note" : "付箋から画像を外す",
            "DeleteImageFile" => english ? "Delete image file too" : "画像ファイルごと削除",
            "DeleteImageFileConfirmTitle" => english ? "Confirm" : "確認",
            "DeleteImageFileConfirmMessage" => english
                ? "Remove this image from the note and delete the image file?"
                : "この画像を付箋から外し、画像ファイルも削除しますか？",
            "DeleteExternalImageFileBlocked" => english
                ? "Only image files stored in this note can be deleted."
                : "この付箋内に保存された画像ファイルのみ削除できます。",

            "BodySize" => english ? "Body {0:0}pt" : "本文 {0:0}pt",
            "TitleSize" => english ? "Title {0:0}pt" : "タイトル {0:0}pt",
            "DragMoveNoSnap" => english ? "Move: snap off" : "移動: スナップなし",
            "DragMoveSeparate" => english ? "Move: current state only" : "移動: 現在の状態のみ",
            "DragMoveSeparateNoSnap" => english ? "Move: current state only, snap off" : "移動: 現在の状態のみ・スナップなし",
            "NoteManagerTitle" => english ? "Notes" : "付箋一覧",
            "NoteManagerSearch" => english ? "Search" : "検索",
            "NoteManagerSearchPlaceholder" => english ? "Search title, body, or path" : "タイトル、本文、パスを検索",
            "NoteManagerShow" => english ? "Show" : "表示",
            "NoteManagerHide" => english ? "Hide" : "非表示",
            "NoteManagerDelete" => english ? "Delete" : "削除",
            "NoteManagerRefresh" => english ? "Refresh" : "更新",
            "NoteManagerTypeNormal" => english ? "Normal" : "通常",
            "NoteManagerTypeExternal" => english ? "External" : "外部",
            "NoteManagerVisible" => english ? "Visible" : "表示",
            "NoteManagerHidden" => english ? "Hidden" : "非表示",
            "NoteManagerTitleColumn" => english ? "Title" : "タイトル",
            "NoteManagerTypeColumn" => english ? "Type" : "種類",
            "NoteManagerStateColumn" => english ? "State" : "状態",
            "NoteManagerUpdatedColumn" => english ? "Updated" : "更新",
            "NoteManagerSnippetColumn" => english ? "Snippet" : "本文",
            "NoteManagerReminderColumn" => english ? "Reminder" : "リマインダー",
            "NoteManagerPathColumn" => english ? "Path" : "パス",
            "NoteManagerNoSelection" => english ? "Select a note first." : "付箋を選択してください。",
            "DeleteConfirmTitle" => english ? "Confirm" : "確認",
            "DeleteConfirmMessage" => english ? "Delete this note?" : "この付箋を削除しますか？",
            "DeleteConfirmMessageWithAssets" => english
                ? "Delete this note? Images stored in this note will also be deleted."
                : "この付箋を削除しますか？この付箋内に保存された画像も一緒に削除されます。",
            "UnlinkExternalConfirmMessage" => english
                ? "Unlink this external-file note? The original file will not be deleted."
                : "この外部ファイル付箋のリンクを解除しますか？元のファイルは削除されません。",
            _ => key,
        };
    }
}
