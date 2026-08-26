# ScreenStickyNotes

Windows 11 向けのデスクトップ付箋アプリです。折りたたむと本文の1行目がタイトルバーに残るので、メモを何枚も並べたまま内容を把握できます。

通信機能はありません。メモはすべてローカルに保存されます。

初回起動時は、データフォルダに「Markdown サンプル」と「使い方」の付箋が自動作成されます。

![ScreenStickyNotes のスクリーンショット](docs/screenshot.png)

## 特徴

**折りたたみ**

付箋をタイトルバーだけの高さに畳めます。畳んだ状態でもタイトル、または本文の1行目がタイトルとして残るので、複数枚を並べたまま内容を把握できます。

**閲覧モードと編集モード**

起動直後は閲覧モードです。本文中の URL やフォルダパスはシングルクリックで開けます。本文をダブルクリックすると編集モードに切り替わり、操作ボタンがノート下部のホバー式ツールバーに現れます。タイトルの編集は、タイトル上のコンテキストメニューから「タイトルを編集」を選びます。Escape かフォーカスを外すと閲覧モードに戻ります。

本文は Markdown で書けます。編集時は Markdown ソースをそのまま表示し、閲覧モードでは見出し、太字、斜体、インラインコード、コードブロック、箇条書き、番号付きリスト、チェックリスト、引用、水平線、表、Markdownリンクを整形して表示します。チェックリストは閲覧モードのままクリックして切り替えられます。

編集モードに入るとウィンドウが下方向に伸びるため、背の低い付箋でも本文が隠れません。

タイトルバーをシングルクリックすると、すぐに折りたたみ／展開します。タイトルバー右側のボタンは、タイトルバーへマウスを移動したときだけ表示されます。折りたたみ中にタイトルバーへホバーすると、本文のプレビューを表示します。

**スナップ**

ドラッグ中およびリサイズ中に、画面の端や他の付箋の辺へ 10px 以内で吸着します。リサイズ時は他の付箋と同じ幅・高さにも吸着するので、複数の付箋を揃えて並べられます。

**その他**

- 24色のカラーパレット
- 40種類のカラー絵文字アイコン（タイトルバーに表示）
- 本文とタイトルで独立したフォントサイズ変更（変更中はサイズを一時表示）
- フォントファミリーの変更
- 常に最前面（付箋ごとに設定）
- タスクトレイに常駐（タスクバーには表示されません）
- タスクトレイから日本語/英語とライト/ダークモードを切り替え
- Windows へのスタートアップ登録
- 新規ノートのタイトルに作成日時を自動設定（`yyyy/MM/dd(曜日) HH:mm:ss`）
- 初回起動時にサンプル付箋を自動作成
- `settings.json` によるタイミング・スナップ距離・初期サイズなどの調整
- 自動保存

## ダウンロード

[Releases](https://github.com/umineko73/ScreenStickyNotes/releases) から2種類の実行ファイルを配布しています。どちらも単一ファイルで、インストール作業は不要です。

| ファイル | サイズ | 必要なもの |
|----------|--------|-----------|
| `ScreenStickyNotes-x.y.z-win-x64.exe` | 約68MB | **なし**（ダブルクリックで動きます） |
| `ScreenStickyNotes-x.y.z-win-x64-runtime.exe` | 約220KB | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

**よく分からない場合は上（68MB のほう）を選んでください。** Windows には .NET 8 が標準で含まれていないため、下のファイルはランタイムを別途インストールしないと起動しません。すでに .NET 8 が入っている環境や、複数台に配る場合は下のほうが軽量です。

## 動作環境

- Windows 10 以降（角丸表示は Windows 11 のみ）
- x64

ビルドするには .NET 8 SDK が必要です。

## ビルドと実行

```bash
git clone https://github.com/umineko73/ScreenStickyNotes.git
cd ScreenStickyNotes
dotnet build
dotnet run --project src
```

### リリース用のビルド

配布する2種類の実行ファイルは、スクリプト1本で `artifacts/` に生成できます。

```bash
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

```
artifacts/
  ScreenStickyNotes-0.0.2-win-x64.exe            68.3 MB  自己完結型
  ScreenStickyNotes-0.0.2-win-x64-runtime.exe     0.2 MB  ランタイム必須
```

バージョン番号は `src/ScreenStickyNotes.csproj` の `<Version>` から取得します。`-Version 0.0.2` のように上書きもできます。

自己完結型は `IncludeNativeLibrariesForSelfExtract` で WPF のネイティブ DLL も exe に埋め込み、`EnableCompressionInSingleFile` で圧縮しています（154MB → 68MB）。これらを指定しないと `D3DCompiler_47_cor3.dll` などが exe の隣に残り、単一ファイルになりません。

## 使い方

### 基本操作

| 操作 | 動作 |
|------|------|
| 本文をダブルクリック | 編集モードに入る |
| Escape | 閲覧モードに戻る |
| タイトルバーをドラッグ | 付箋を移動 |
| タイトルバーをシングルクリック | 折りたたみ／展開 |
| タイトル上で右クリック | タイトルのコンテキストメニューを表示 |
| タスクトレイアイコンを左クリック | 全付箋の表示／非表示を切り替え |
| タスクトレイアイコンを右クリック | メニュー（新規作成・スタートアップ登録・言語切り替え・ダークモード・終了） |

### タイトルバー（ホバー時にボタン表示）

| ボタン | 動作 |
|--------|------|
| ＋ | 新しい付箋をマウスカーソル付近に追加（**この付箋の色・アイコン・フォントを引き継ぎます**） |
| 📌 | 常に最前面 |
| ▲ ▼ | 折りたたみ／展開 |

### 編集ツールバー（編集モード時のホバー・コンテキストメニュー表示）

| ボタン | 動作 |
|--------|------|
| A- / A+ | 本文のフォントサイズ（8〜48pt） |
| T- / T+ | タイトルのフォントサイズ（8〜28pt） |
| Aa | フォントファミリーを変更 |
| 😀 | アイコンを選択 |
| 🎨 | 色を選択 |

編集ツールバーはノートの下に横一列で表示されます。ノート上のホバーまたは本文のコンテキストメニュー表示中に利用できます。付箋の削除は本文またはタイトルのコンテキストメニューから実行します。

### コンテキストメニュー

- 本文: 切り取り、コピー、貼り付け、リンク操作、削除
- タイトル表示時: タイトルを編集、コピー、重なり順（前面へ移動/背面へ移動）、削除
- タイトル編集中: 切り取り、コピー、貼り付け、すべて選択、重なり順（前面へ移動/背面へ移動）、削除

### リンク

本文中の `https://` `http://` `ftp://` で始まる URL、`C:\...` 形式のパス、`\\server\share` 形式の UNC パスを自動で検出し、下線付きで表示します。
Markdownリンクは `[表示名](https://example.com)` の形式に対応しています。

- **閲覧モード**：クリックで開く
- **編集モード**：Ctrl + クリックで開く
- URL はブラウザ、フォルダはエクスプローラーで開きます
- 右クリックメニューから「リンクとして変換」も可能です

### settings.json

設定は `%APPDATA%\ScreenStickyNotes\settings.json` に保存されます。`Language` は `ja` / `en`、`Theme` は `Light` / `Dark` を指定できます。タスクトレイメニューからの言語切り替えとダークモード切り替えは即時反映されます。設定ファイルを直接編集した場合はアプリを再起動してください。

```json
{
  "StartWithWindows": false,
  "Language": "ja",
  "Theme": "Light",
  "Timings": {
    "TitlePreviewDelayMs": 500,
    "ToolbarHideDelayMs": 180,
    "SizeOverlayDurationMs": 900,
    "SingleClickGraceMs": 200,
    "FoldAnimationMs": 150,
    "SizeOverlayFadeMs": 350,
    "ToolbarFadeMs": 110,
    "SaveDebounceMs": 800
  },
  "Interaction": {
    "SnapDistance": 10,
    "ClickDragThresholdPx": 4
  },
  "Layout": {
    "UnfoldedMinWidth": 140,
    "ResizeBorder": 5,
    "RootBorderThickness": 1,
    "NewNoteBaseX": 150,
    "NewNoteBaseY": 150,
    "NewNoteCascadeStep": 20,
    "NewNoteNearCursorOffset": 12,
    "DefaultNoteWidth": 260,
    "DefaultNoteHeight": 220
  }
}
```

## データの保存場所

```
%AppData%\ScreenStickyNotes\
  settings.json  アプリ全体の設定（スタートアップ登録・言語・テーマ・動作調整など）
  notes\
    {付箋ID}\
      meta.json     位置・サイズ・色・フォント・アイコン
      content.md    本文
```

付箋1枚につき1フォルダです。本文はプレーンテキストなので、他のエディタからも読めます。設定も含めてバックアップする場合は、`ScreenStickyNotes` フォルダごとコピーしてください。

保存先は環境変数 `SCREENSTICKYNOTES_DATA` で変更できます。テスト時に本番データから隔離する用途で使えます。

```bash
set SCREENSTICKYNOTES_DATA=D:\mynotes
```

保存は変更の 800ms 後に行われます（デバウンス）。ウィンドウを閉じたとき、ログオフ・シャットダウン時、アプリ終了時には保留中の変更が即座に書き出されます。

## 仕様上の制限

**二重起動はできません。** 複数のインスタンスが同じフォルダを読み書きすると保存が競合して付箋が失われるため、名前付き Mutex で1プロセスに制限しています。2つ目を起動すると、既存のインスタンスに全付箋の表示を要求して終了します。

## 開発

```
ScreenStickyNotes.sln
docs/                    スクリーンショットなど
src/
  ScreenStickyNotes.csproj
  App.xaml(.cs)          エントリポイント・タスクトレイ常駐
  Models/                ノート・アプリ設定のデータモデル
  ViewModels/            INotifyPropertyChanged を実装したビューモデル
  Views/                 付箋ウィンドウ（XAML + コードビハインド）
  Services/              永続化・リンク検出・スタートアップ登録
  Resources/             スタイル定義
```

付箋1枚が1つの `Window` です。`WindowStyle="None"` と `WindowChrome` で独自のタイトルバーを描画し、リサイズ中のスナップは `WM_SIZING` をフックして実現しています。角丸は DWM の `DWMWA_WINDOW_CORNER_PREFERENCE` に任せています。

## ライセンス

[GNU General Public License v3.0](LICENSE)

Copyright (C) 2026 umineko73

このプログラムはフリーソフトウェアです。フリーソフトウェア財団が公表した GNU 一般公衆利用許諾書バージョン3、または（任意で）それ以降のバージョンの条項に従って、再頒布・改変することができます。

このプログラムは有用であることを願って頒布されますが、**一切の保証はありません**。商品性や特定目的への適合性についての暗黙の保証すらありません。詳細は GNU 一般公衆利用許諾書をご覧ください。
