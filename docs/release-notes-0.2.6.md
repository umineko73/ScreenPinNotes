# v0.2.6

## 日本語

- 付箋の右クリックメニューに「付箋を複製」を追加しました。本文・見た目・貼り付けた画像を写した付箋を、元の付箋から少しずらして作ります（リマインダーは写しません）。
- Markdown の表示を強化しました。
  - 字下げ（スペース2つ以上・タブ・全角スペース）でリストを入れ子にできます。番号付きリストは書いた番号から始まり、`1)` 形式にも対応します。
  - 続けて書いた `>` の行を1つの引用として表示し、`>>` で入れ子にできます。引用の中にリストや見出しも書けます。
  - 行頭の字下げを保ったまま折り返します。
  - `==ハイライト==` に対応しました。
  - 続けて書いた行を1つの段落として折り返す設定を追加しました（設定 → 表示 → Markdown、既定はオフ）。
- 編集中の操作を改善しました。
  - `Tab` / `Shift+Tab` で、リストの行や選んだ複数行を字下げ・字下げ解除できます。
  - リストの行で `Enter` を押すと、次の項目の印（次の番号・未完了のチェックボックス）が入ります。
  - 「Markdown書式」メニューに、ハイライト・番号付きリスト・引用を追加しました。
- 外部ファイルの付箋の監視を見直しました。
  - 全付箋で1つのタイマーを使い、1分間変化がなければ確認を止めます（Windows からの変更通知や、付箋へのマウスオーバー・クリックで再開）。
  - 確認中はタイトル右端の小さな丸が明滅します。アイコンやタイトルのツールチップに、自動で反映されることと最終更新時刻を表示します。
  - ファイルが一時的に読めなかった場合も、あとで読み直すようにしました。
- ログに `ERROR` / `FATAL` が届いたときは、付箋の背景が赤みを帯びて2回点滅します。リマインダーの点滅は付箋全体を点滅させるようにしました。
- 起動直後などの付箋で、外部ファイルが更新されても点滅しないことがある問題を修正しました。
- 他のアプリの窓を閉じたときなどに、タスクバーに表示している1行表示の付箋が勝手に開く問題を修正しました。
- タイトルの右クリックメニューの「コピー」を「タイトルをコピー」に改めました。

## English

- Added **Duplicate note** to the note context menu. The copy keeps the body, appearance and pasted images and is placed slightly offset from the original. Reminders are not copied.
- Richer Markdown:
  - Lists nest by indentation (two or more spaces, tabs, or full-width spaces). Ordered lists start at the number you write and accept `1)`.
  - Consecutive `>` lines form one quote, `>>` nests, and quotes can hold lists and headings.
  - Indented lines stay aligned when they wrap.
  - Added `==highlight==`.
  - New option to join consecutive lines into one paragraph (Settings → Appearance → Markdown, off by default).
- Editing improvements:
  - `Tab` / `Shift+Tab` indent and outdent list lines or selected lines.
  - `Enter` on a list line starts the next item (the next number, an unchecked box).
  - The Markdown formatting menu gains highlight, numbered list and quote.
- Reworked external file watching:
  - All notes share one timer, and a note stops checking after a minute without changes. Checking resumes on a change notification from Windows or when you point at or click the note.
  - While a note is checking, a small dot at the right end of its title fades with each check. The icon/title tooltip shows that changes appear automatically and when the note was last updated.
  - A file that is briefly unreadable is now read again later.
- An `ERROR` / `FATAL` log line now tints the note's background red for two pulses. Reminder flashes now flash the whole note.
- Fixed external-file update flashes that could be missing for notes activated in the background, e.g. right after startup.
- Fixed folded taskbar notes opening by themselves when another app's window closed.
- The title context menu's **Copy** is now **Copy title**.

## Downloads

- `ScreenPinNotes-0.2.6-win-x64.zip`: .NET同梱 / self-contained.
- `ScreenPinNotes-0.2.6-win-x64-runtime.zip`: .NET 8 Desktop Runtimeが必要 / requires .NET 8 Desktop Runtime.
