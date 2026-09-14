# v0.2.4

## 日本語

- ログファイル監視に対応しました。`.log` は既定で末尾追従（tail）モードで開き、指定行数を表示して最新行へ自動スクロールします。
- 更新の最短間隔と末尾の表示行数を設定できます。書き込み中のファイルや、同じパスでのローテーションにも追従します。
- ログレベルと数値を色分けし、非アクティブな付箋は更新時に枠が光ります。新着のエラー行は赤で通知します。
- タイトルバーに更新時刻とtail状態を表示します。通常の外部ファイル表示では更新前の閲覧位置をできるだけ保持します。
- 1行表示の幅変更中に展開した場合のサイズ保存と、メニューの文字色を修正しました。
- UTF-8 BOM付きログの表示と、末尾読み込み中の切り詰め処理を修正しました。

## English

- Added log monitoring. `.log` files open in tail mode by default, displaying the configured last lines and scrolling to new output.
- Configure the minimum refresh interval and tail line count. Follow files held open by writers and rotation at the same path.
- Highlight log levels and numbers. Inactive notes pulse on updates, with a red border for new error lines.
- Show the update time and tail indicator in the title bar; preserve the reading position where possible outside tail mode.
- Fixed expanded size persistence during folded-width dragging and menu text contrast.
- Fixed UTF-8 BOM display and handling of truncation during tail reads.

## Downloads

- `ScreenPinNotes-0.2.4-win-x64.zip`: .NET同梱 / self-contained.
- `ScreenPinNotes-0.2.4-win-x64-runtime.zip`: .NET 8 Desktop Runtimeが必要 / requires .NET 8 Desktop Runtime.
