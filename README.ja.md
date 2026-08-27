# ScreenStickyNotes

[English](README.md) | 日本語

Windows 11 向けのデスクトップ付箋アプリです。タイトルバーだけの高さに畳んでもタイトル（または本文1行目）は見えるので、何枚並べてもデスクトップを占領しません。

![ScreenStickyNotes のスクリーンショット](docs/screenshot.png)

## 特徴

- **折りたたみ**：タイトルバーだけの高さに畳める。畳んでもタイトル/本文1行目は見える
- **閲覧／編集モード**：本文をダブルクリックで編集、Escapeで閲覧に戻る
- **Markdown 表示**：表・チェックリスト・画像なども含む
- **スナップ**：ドラッグ・リサイズ時に画面端や他の付箋に吸着
- **半透明表示**：付箋ごとに10〜100%の不透明度、タイトル右クリックで設定
- 24色のカラーパレット、40種のアイコン、本文・タイトルの個別フォント
- 常に最前面（付箋ごと）、タスクトレイ常駐
- 日本語/英語・ライト/ダークモードの切り替え
- クリップボード画像の貼り付け、Excel表のMarkdownへの貼り付け/コピー
- スタートアップ登録・自動保存・ノート保存フォルダの変更 — すべてタスクトレイから

## Markdown 記法

編集モードでは Markdown ソースをそのまま表示し、閲覧モードで整形して表示します。

| 記法 | 効果 |
|------|------|
| `# 見出し` 〜 `###### 見出し` | 見出し（6段階） |
| `**太字**` / `*斜体*` | **太字** / *斜体* |
| `` `コード` `` / ` ```ブロック``` ` | インラインコード / コードブロック |
| `- 項目` / `1. 項目` / `- [ ]` | リスト（チェックリストはクリックで切替可） |
| `> 引用` / `---` | 引用 / 水平線 |
| `\| a \| b \|` | 表 |
| `[表示名](url)` | リンク |
| `![説明](assets/image.png)` | 画像（`{width=240}` でサイズ指定） |

貼り付けた画像は付箋フォルダの `assets` にPNGとして保存されます。右クリックでリサイズ（0〜200%）や削除が可能。インライン表示されるのはローカルファイルの画像のみで、`http(s)://` の画像URLは `![image](...)` の形には変換されますがプレビューはされません。

## ダウンロード

[Releases](https://github.com/umineko73/ScreenStickyNotes/releases) から取得し、zip を展開するだけです。インストール不要です。

| ファイル | 必要なもの |
|----------|-----------|
| `ScreenStickyNotes-x.y.z-win-x64.zip`（約68MB） | なし。迷ったらこちら |
| `ScreenStickyNotes-x.y.z-win-x64-runtime.zip`（約11MB） | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

## 動作環境

Windows 10 以降（角丸表示は Windows 11 のみ）、x64。ビルドには .NET 8 SDK が必要です。

## ビルドと実行

```bash
git clone https://github.com/umineko73/ScreenStickyNotes.git
cd ScreenStickyNotes
dotnet build
dotnet run --project src
```

配布用zip: `powershell -ExecutionPolicy Bypass -File scripts/publish.ps1`（`artifacts/` に生成）。

## 使い方

| 操作 | 動作 |
|------|------|
| 本文をダブルクリック | 編集モードに入る |
| Escape | 閲覧モードに戻る |
| タイトルバーをドラッグ / クリック | 移動 / 折りたたみ・展開 |
| タイトル上で右クリック | タイトル編集・重なり順・透明度・削除 |
| 本文/画像上で Ctrl+ホイール | フォント/画像サイズ変更 |
| タスクトレイアイコン 左/右クリック | 全表示切替 / メニュー |

タスクトレイの設定サブメニューでは、スタートアップ登録・保存フォルダ・ダークモード・言語・タイトルホバー/折りたたみアニメーション/折りたたみボタンのON/OFFを扱えます。

## settings.json

`%APPDATA%\ScreenStickyNotes\settings.json` に保存されます。`Language`・`Theme`・`StorageRoot` のほか各種UIトグル・タイミング設定があります。直接編集した場合はアプリの再起動が必要です。

## データの保存場所

```
%AppData%\ScreenStickyNotes\
  settings.json
  logs\app.log
  notes\{付箋ID}\meta.json, content.md, assets\
```

ノート本体は `StorageRoot`（既定は上記フォルダ）配下に保存されます。タスクトレイの **設定 > 保存フォルダを選択...** から変更できるほか、初回起動前に環境変数 `SCREENSTICKYNOTES_DATA` で指定することもできます。

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
