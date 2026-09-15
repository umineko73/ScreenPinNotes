# v0.2.5

## 日本語

- 起動時に付箋を表示せず、タスクトレイで待機する設定を追加しました（設定 → 起動）。
- クリップボードの文字・画像・コピーした画像ファイルから付箋を作成できます。トレイメニューと、ショートカット `Ctrl+Alt+Shift+N`（設定で変更・無効化可）から使えます。
- 外部ファイルを書き込み中のアプリが開いたまま追記する場合も、ファイルの長さと更新日時の確認を併用して更新を検知します。
- 非アクティブな1行表示の付箋を、1回のクリックで開けるようにしました。
- 1行表示で幅を変えたときに、開いた付箋が1行の高さのまま残ることがある問題を修正しました。
- タイトルバー右端のボタンをリサイズ枠から離し、付箋の右端をつかみやすくしました。
- 特定の環境でだけ起きる不具合を調べるためのログ出力を追加しました（既定はオフ、`settings.json` の `EnableDiagnosticTrace`）。

## English

- Added an option to start with notes hidden and wait in the tray (Settings → Startup).
- Create a note from the clipboard's text, image, or copied image files, from the tray menu or with `Ctrl+Alt+Shift+N` (changeable or disabled in Settings).
- External notes now also check the file's size and modification time, so they follow files a program keeps open while appending.
- A folded note in the background now opens with a single click.
- Fixed an expanded note that could be left one line tall after changing a folded note's width.
- Moved the title bar buttons clear of the resize border so the note's right edge is easier to grab.
- Added an opt-in diagnostic log for problems that only occur on some PCs (`EnableDiagnosticTrace` in `settings.json`, off by default).

## Downloads

- `ScreenPinNotes-0.2.5-win-x64.zip`: .NET同梱 / self-contained.
- `ScreenPinNotes-0.2.5-win-x64-runtime.zip`: .NET 8 Desktop Runtimeが必要 / requires .NET 8 Desktop Runtime.
