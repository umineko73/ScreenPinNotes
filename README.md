# ScreenStickyNotes

Windows 11 向けのデスクトップ付箋アプリです。折りたたむと本文の1行目がタイトルバーに残るので、メモを何枚も並べたまま内容を把握できます。

初回起動時は、データフォルダに「Markdown サンプル」と「使い方」の付箋が自動作成されます。

![ScreenStickyNotes のスクリーンショット](docs/screenshot.png)

## 特徴

- **折りたたみ**：タイトルバーだけの高さに畳める。畳んでもタイトル（または本文1行目）は見える
- **閲覧／編集モード**：本文をダブルクリックで編集、Escape かフォーカス外しで閲覧に戻る
- **Markdown 表示**（下記参照）
- **スナップ**：ドラッグ・リサイズ時に画面端や他の付箋に10px以内で吸着
- 24色のカラーパレット、40種のアイコン（`settings.json` の `IconPalette` で差し替え可）
- 本文・タイトルで独立したフォントサイズ／フォントファミリー変更
- 常に最前面（付箋ごと）、タスクトレイ常駐
- 日本語/英語・ライト/ダークモードの切り替え
- クリップボード画像の貼り付け、Windows スタートアップ登録、自動保存

## Markdown 記法

編集モードでは Markdown ソースをそのまま表示し、閲覧モードで整形して表示します。

| 記法 | 効果 |
|------|------|
| `# 見出し` 〜 `###### 見出し` | 見出し（6段階） |
| `**太字**` | **太字** |
| `*斜体*` | *斜体* |
| `` `コード` `` | インラインコード |
| ` ```コードブロック``` ` | コードブロック |
| `- 項目` / `1. 項目` | 箇条書き／番号付きリスト |
| `- [ ]` / `- [x]` | チェックリスト（閲覧モードでもクリックで切替） |
| `> 引用` | 引用 |
| `---` | 水平線 |
| `\| a \| b \|` | 表 |
| `[表示名](https://example.com)` | リンク |
| `![説明](assets/image.png)` | 画像（`{width=240}` でサイズ指定も可） |

画像は編集モードで貼り付けると付箋フォルダの `assets` に保存されます。閲覧モードで画像を右クリックすると 10%〜100% でリサイズできます。

## ダウンロード

[Releases](https://github.com/umineko73/ScreenStickyNotes/releases) から取得できます。zip を展開するだけで、インストール不要です。

| ファイル | 必要なもの |
|----------|-----------|
| `ScreenStickyNotes-x.y.z-win-x64.zip`（約68MB） | なし。迷ったらこちら |
| `ScreenStickyNotes-x.y.z-win-x64-runtime.zip`（約220KB） | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

どちらも展開すると `ScreenStickyNotes.exe` と `SampleNotes\`（サンプル付箋。無くても起動できます）が同じフォルダに入っています。

## 動作環境

Windows 10 以降（角丸表示は Windows 11 のみ）、x64。ビルドには .NET 8 SDK が必要です。

## ビルドと実行

```bash
git clone https://github.com/umineko73/ScreenStickyNotes.git
cd ScreenStickyNotes
dotnet build
dotnet run --project src
```

配布用の zip は `powershell -ExecutionPolicy Bypass -File scripts/publish.ps1` で `artifacts/` に生成できます（バージョンは `-Version 0.1.1` で上書き可）。

## 使い方

| 操作 | 動作 |
|------|------|
| 本文をダブルクリック | 編集モードに入る |
| Escape | 閲覧モードに戻る |
| タイトルバーをドラッグ / シングルクリック | 移動 / 折りたたみ・展開 |
| タイトル上で右クリック | タイトル編集・重なり順・削除 |
| タスクトレイアイコン 左クリック / 右クリック | 全表示切替 / メニュー |

編集モード中はノート下部にツールバー（フォントサイズ・フォント・アイコン・色）が表示されます。本文中の URL・パス・Markdownリンクはクリック（編集モードは Ctrl+クリック）で開けます。

## settings.json

`%APPDATA%\ScreenStickyNotes\settings.json` に保存されます。主な項目:

- `Language`: `ja` / `en`、`Theme`: `Light` / `Dark`（トレイメニューから即時反映）
- `IconPalette`: タイトルバーのアイコン候補（絵文字の配列、既定40種）
- `Timings` / `Interaction` / `Layout`: アニメーション速度・スナップ距離・初期サイズなどの細かい調整

直接編集した場合はアプリの再起動が必要です。

## データの保存場所

```
%AppData%\ScreenStickyNotes\
  settings.json
  notes\{付箋ID}\meta.json, content.md, assets\
```

保存先は環境変数 `SCREENSTICKYNOTES_DATA` で変更できます。変更後 800ms でデバウンス保存されます。

## 開発

```
src/
  App.xaml(.cs)      エントリポイント・タスクトレイ常駐
  Models/            データモデル
  ViewModels/        ビューモデル
  Views/             付箋ウィンドウ（XAML + コードビハインド）
  Services/          永続化・Markdown・リンク検出など
  SampleNotes/       初回起動時にコピーされるサンプル付箋
```

付箋1枚が1つの `Window` です。`WindowStyle="None"` + `WindowChrome` で独自タイトルバーを描画しています。

## ライセンス

[GNU General Public License v3.0](LICENSE)

Copyright (C) 2026 umineko73

このプログラムはフリーソフトウェアです。フリーソフトウェア財団が公表した GNU 一般公衆利用許諾書バージョン3、または（任意で）それ以降のバージョンの条項に従って、再頒布・改変することができます。

このプログラムは有用であることを願って頒布されますが、**一切の保証はありません**。詳細は GNU 一般公衆利用許諾書をご覧ください。
