# User guide

[English](guide.md) | [日本語](guide.ja.md)

[← README](../README.md)

## Display modes

Title bar visibility and collapse/expand are independent settings.

| Title bar | Expanded | Collapsed |
| --- | --- | --- |
| Visible | Title and body | Title only |
| Hidden | Body with controls at the top right | First body line without Markdown formatting |

- Toggle the title bar with **Hide title bar** in the context menu.
- A note holding nothing but an image drops its body padding so the picture reaches the note edges. **On an image-only note** in settings decides what happens to the spine there: *Beside the image* keeps a strip for it, *Over the image* draws it on top so the picture runs edge to edge, and *Hide the spine* leaves the note bare.
- A note with a hidden title bar carries a spine down its left side. Collapsed, it looks like any note showing only its title bar, so the spine tells them apart. **Hidden title bar marker** in settings turns it off, changes its width (1-12px), and picks how it sits: *Off the edge* keeps an even line in the gap between the edge and the body, with the same gap above and below it (0-12px away, 3px by default), while *Against the edge* restores the older look that hugs the edge and tapers where the corners are rounded. The body text is indented to clear the spine, so the two never crowd each other. Either way the spine doubles as a grab handle: drag it to move the note, the same as dragging a title bar.
- Use `⮝` / `⮟` or double-click the title bar to collapse or expand.
- Image-only notes show their image path in single-line mode. Narrow notes retain the end, such as `…/photos/image.png`.
- An empty title falls back to the body. A title you enter takes priority.
- Editing shows Markdown source and an *Editing* label. The label moves above the horizontal scrollbar when it appears. Edit width and height are remembered separately from view mode.
- Expanding a note temporarily brings it above pinned notes while you work with it. Switching to another window or collapsing the note ends this temporary raise without changing its pin setting.

## Mouse actions

| Action | Result |
| --- | --- |
| Double-click the body | Start editing |
| Drag the title bar | Move; snap to screen edges and other notes |
| Drag the top-right control area with the title bar hidden | Move the note |
| Double-click the title bar | Collapse/expand; configurable as a single click |
| Drag an edge | Resize; collapsed notes resize horizontally only |
| Click a note | Bring it to the front, where it stays until you touch another note. The configured stacking order is left as it is |
| Right-click the title or body | Open the menu for that area |
| Click a link | Open it |
| Click a checklist checkbox | Toggle completion |
| Right-drag a scrollable body | Scroll the body |

## Modifier keys and shortcuts

While the app is running, **Ctrl+Alt+N** creates a new note from other apps, and **Ctrl+Alt+Shift+N** creates one from the clipboard's text, image, or copied image files (changed under New note from clipboard shortcut). In Settings, focus the New note shortcut field, press a key combination, and click Apply. You can also restore the default or disable the shortcut. If another app has registered the combination, the previous shortcut remains active.

| Action | Result |
| --- | --- |
| `Ctrl` + drag | Move only the current display mode's position |
| `Alt` + drag | Move without snapping |
| `Ctrl+Alt` + drag | Move only the current mode, without snapping |
| `Ctrl` + wheel over the body | Change body font size |
| `Ctrl` + wheel over the title bar | Change title font size |
| `Ctrl` + wheel over an image | Resize the image |
| `Ctrl+Enter` / `Esc` / `✓` | Finish editing and keep changes |
| `Enter` while editing the title | Confirm the title |
| `Ctrl+Z` | Undo an edit |
| `Ctrl+Y` | Redo an undone edit |
| `Enter` on a list line while editing | Start the next item with the same marker (the next number, an unchecked box). On an empty item, outdent it or leave the list |
| `Tab` / `Shift+Tab` while editing | Indent / outdent a list line or all selected lines. On other lines, `Tab` types a tab character |
| `Ctrl+C` / `Ctrl+X` / `Ctrl+V` | Copy/cut/paste |
| `Alt+F4` / taskbar close | Hide the note |

Normal dragging links expanded and collapsed positions. Use **Align to collapsed position** to reconnect separated positions. Each mode remembers its own width. Snapping adjacent notes leaves a one-physical-pixel gap.

## Icons and toolbars

![A note in edit mode, showing the title bar buttons and the editing toolbar](toolbar-en.png)

| Symbol | Meaning or action |
| --- | --- |
| `＋` | Create a note using this note's appearance |
| `📌` | Toggle always-on-top |
| `⮝` / `⮟` | Collapse / expand |
| `🦊` and other icons | Identify a note |
| `🔗` | Linked to an external file |
| `🔒` | Editing is locked |
| `⛓️‍💥` | Expanded and collapsed positions are separate |
| `⏰` | Reminder set; hover for its next time |
| `A−` / `A＋` | Body font size |
| `T−` / `T＋` | Title and single-line font size |
| `Aa` / `🦊` / `🎨` (mini toolbar) | Font / icon / color picker |
| `↶` / `↷` (editing toolbar) | Undo / redo |
| Round `✓` | Finish editing; black in light mode, white in dark mode |

The mini toolbar appears above context menus and below the note while editing. When there is no room below, such as near the taskbar, the editing toolbar appears above the note and stays within the screen's working area. `🔒`, `⛓️‍💥`, and `⏰` are status indicators.

The editing toolbar's `↶` / `↷` buttons act on the body or title field you are editing. Autosaving preserves undo and redo history. Buttons are disabled when no corresponding history is available.

Icon and color palettes appear in front of notes. Click the same editing toolbar button again to close its palette. Clicking outside the palette or switching to another window also closes it.

## Context menus

![The body context menu, with the mini toolbar above it](context-menu-en.png)

| Location | Main actions |
| --- | --- |
| Title | Edit/copy title, stacking order, opacity, reconnect positions |
| Body in view mode | Copy, open links, copy Excel tables, fit the window to images |
| Body in edit mode | Cut/paste/select all, Markdown formatting, edit links, paste Excel tables |
| Image | The body menu gains image actions at the top (image size, fit the note to this image, detach or delete it). Everything else in the menu stays available |
| Shared by title and body | Hide title bar, opacity, reminders, edit lock, duplicate, hide, delete |

Use the mini toolbar for colors, fonts, and icons. Unavailable actions are disabled or hidden.

**Duplicate note** creates a copy with the same body, appearance, and pasted images, slightly offset from the original. Reminders are not copied. **Hide** keeps the note; **Delete** removes it. Edit lock restricts body/title editing and deletion, while appearance, position, and checklist completion remain adjustable.

## Markdown, images, and Excel

| Type | Syntax or action |
| --- | --- |
| Headings | `# Heading` through `###### Heading` |
| Formatting | `**bold**`, `*italic*`, `~~strike~~`, `==highlight==` |
| Code | Enclose inline code with one backtick; blocks with three |
| Lists | `- item`, `1. item` (or `1) item`; numbering starts at the number you write), `- [ ] task`. Indent by two or more spaces or a tab to nest |
| Quotes and rules | `> quote` (consecutive lines form one quote, `>>` nests, and quotes can hold lists and headings), `---` |
| Indentation | Leading spaces, tabs, and full-width spaces indent the line, and wrapped lines stay aligned with the indent |
| Line breaks and paragraphs | Each line is shown on its own by default. Turn on **Join consecutive lines into one paragraph** under **Settings → Appearance → Markdown** to wrap lines up to the next blank line as one paragraph (Japanese text joins directly, words join with a space; end a line with two spaces or `\` to break it) |
| Tables | Pipe-separated Markdown tables, with column alignment |
| Links | `[label](URL)`, `[label][1]` with a separate `[1]: URL` line, or a plain URL |
| Images | `![alt](assets/image.png)`; append `{width=240}` to set width |

- Use **Markdown formatting** while editing to insert syntax (bold, strikethrough, highlight, code, headings, bulleted and numbered lists, checklists, quotes). **Edit link** changes a link's label and URL.
- Pasted images are saved as PNGs in the note's `assets` folder. Local images render inline; web image URLs do not.
- Dropping files onto a note, or pasting files copied in Explorer, copies them into the note's `assets` folder in their original format. The original files stay where they were.
- Files that are not images sit in the text as a small chip with the same icon Explorer shows, plus the file name. **Click** one to open it in its associated app (like a link, it does not start editing the body) (files that can run programs ask first). Right-click a chip for **Open** and **Open with...**, which lets you pick the app, at the top of the menu.
- Hold **Shift** while dropping to link to the file where it is instead of copying it — better for documents you keep editing and for large files. Folders are always linked. A chip that points at the file where it lives carries the same shortcut arrow Explorer puts on a shortcut. Point at a chip to see which it is and where the file lives.
- A chip whose file is gone is shown with a line through its name.
- Resize images between 20% and 200% using their context menu or `Ctrl` + wheel.
- A diagram drawn in draw.io (a PNG with the diagram embedded in it) gets **Edit in draw.io** at the top of its context menu. Saving in draw.io redraws the picture in the note straight away, with the note left open. A chip holding a `.drawio` file offers the same item. draw.io is located automatically; set `DrawioPath` in `settings.json` if it is somewhere unusual.
- Use the context menu to paste/copy Excel tables. Pasting images is also supported.
- Very large documents or deeply nested formatting fall back to source text without discarding content.

## Note list

![The note list window](note-list-en.png)

Open **Note list** from the tray. Columns show layer order, pin status, visibility, edit lock, title, body excerpt, reminder, update time, and external path.

- **Search**: choose plain text, wildcard (`*` for any sequence, `?` for one character), or regular expression. Searches ignore case. Invalid or excessively slow patterns show an error.
- **History**: Enter or leaving the search field saves the query. The latest 30 entries persist across restarts and can be selected from the field's dropdown. **Clear search** clears the query while keeping history.
- **Multiple selection**: Ctrl/Shift-click notes, then use **Show / Hide** for the entire selection. Deletion, reminders, and other individual actions operate on one note at a time.
- **Layers**: higher rows appear in front. Use **Bring to front, Move up, Move down, Send to back**. The order persists across restarts and includes hidden notes. Always-on-top notes form a separate group above normal notes.
- **Sorting**: click a column header, then click again to reverse direction (▲/▼). This sorts only the list, leaving the actual stacking order intact. Click **Z-order** to return to layer order. Layer movement is available in layer order with search cleared.

## Reminders

![The reminder editor](reminder-en.png)

Configure a reminder from a note's context menu or the **Note list**. Choose a date from the calendar. The editor appears above pinned notes.

**+5 min, +10 min, +1 hour** add to the date and time currently entered, including repeated clicks and crossing midnight. Use **Reset to now** then **+10 min** for ten minutes from now. **Set minutes to 00** keeps the date and hour. Choose a future time before saving.

| Repeat | Schedule |
| --- | --- |
| Once | Date and time |
| Daily | Start date and daily time |
| Weekly | Start date, time, one or more weekdays, and optionally the 1st–5th weeks (e.g. 1st and 3rd for every other week); a week is the weekday's occurrence in the month |
| Monthly | Start date, time, and day 1–31 or **Last day**; shorter months use their last day |

Click a Windows notification to open the note list. Simultaneous reminders share a notification. Optionally enable the alert window for 5-, 15-, or 60-minute snooze. Snoozing preserves the recurring time.

**Flash the note for 10 seconds** is enabled by default. It shows hidden notes and slowly pulses the whole note in orange (the text stays readable through it). The flashing note comes in front of other apps' windows; turn this off under **Settings → Behavior → Reminders**. Other notes stay where they are. Clicking, typing, or hiding the note stops the effect. Windows notifications, snooze alerts, and flashing can be combined; flashing alone is also supported.

The app must be running in the tray. Missed reminders are delivered once on restart or resume. Windows notification settings control banners and sound.

## Tray, settings, and external files

![The tray icon menu](tray-menu-en.png)

| Feature | Purpose |
| --- | --- |
| Tray left-click | Toggle all notes or create a new note, as selected in settings |
| Tray right-click | Create notes, note list, hidden notes, settings, exit |
| Note list | Search titles, bodies, external paths, and more; manage visibility, reminders, and deletion |
| Hidden notes | Restore individually hidden notes; Show all does not restore them |
| Settings | New-note defaults, theme, language, note appearance, startup (including starting with notes hidden in the tray), taskbar/tray behavior, external file refresh/tail behavior, and storage |
| Open external file as note | Display `.md` / `.txt` / `.log` read-only and follow file changes (minimum refresh interval configurable in Settings) |
| New note from clipboard | Create a note from the clipboard's text, image, or copied image files (images are copied into the note) |

![The settings window](settings-en.png)

**Settings > Appearance** collects how notes look: corner radius (0-16px, *Square* by default), border color (none by default; gray and the note's own color are also available), icon color (color or monochrome), and the hidden title bar marker. Changes reach open notes right away.

The default *None* leaves a pale note with almost no visible edge over a white background. Set the border color to gray or the note's own color to keep one.

Updating from 0.2.0 or earlier changes how notes look on first launch: rounded corners and a gray frame become square corners and no border. For the old look, set corner radius to 6 and border color to gray in **Settings > Appearance**.

Settings save immediately (shortcut changes require Apply). New-note defaults apply to tray- and shortcut-created notes; `＋` on a note copies its appearance.

External notes show `🔗`, and their title bar shows the time the content was last refreshed so you can tell at a glance whether it's current. Hovering the icon or title shows that changes appear automatically, the file path, and the last update time. Their menu can open the file or folder, toggle tail mode, or convert the content into an editable note. Deleting the note or changing image display sizes does not modify the original file.

Outside of tail mode, an auto-refresh keeps your scroll position and text cursor in place as much as possible instead of jumping back to the top.

Windows may not report changes while the writing program keeps the file open, as loggers usually do. The note therefore also checks the file's size and modification time every second. It stops checking after a minute without changes and starts again when Windows reports a change, or when you point at or click the note. Before it stops, though, it asks Windows whether a program still has that file open. If one does — which is exactly when change reports go missing — it keeps checking even while nothing changes, and goes back to stopping on time once the program closes the file. While it is checking, a small dot at the right end of the title fades with each check, and the tooltip says *Checking for changes*. The interval and the idle time can be changed with `ExternalFile.PollIntervalMs` and `ExternalFile.PollStopAfterMs` in `settings.json`. Set `ExternalFile.PollWhileWriterHoldsOpen` to `false` to stop on time even for a file a program keeps open.

In tail mode, log levels are colored (case-insensitive): green for `TRACE`, `DEBUG`, `INFO`, `VERBOSE` and `NOTICE`; orange for `WARN` and `WARNING`; red for `ERROR`, `FATAL`, `CRITICAL`, `SEVERE`, `PANIC`, `ALERT` and `EMERG`. Three-letter forms such as `TRC`, `DBG`, `INF`, `WRN`, `ERR` and `FTL` are recognized too, as are outputs like `trce`, `dbug`, `fail` and `crit` (single-letter markers are not, since they cannot be told apart from ordinary text). Only the first level word on each line is colored. Numbers are shown in blue so timestamps and counts are easy to pick out; values joined by separators, such as `2026-09-15`, `09:12:03.221` and `16/16`, stay in one piece.

When the file content actually changes, the note's border pulses light blue once. If the lines that just arrived contain `ERROR` or `FATAL`, the border turns red and the note's background pulses twice, shifting a quarter of the way from its own color toward red (the text itself is not covered). It stays quiet while you are working in that note, since you can already see the update, and a save that leaves the content identical triggers neither a reload nor a pulse.

Tail mode shows only the last lines of the file (line count configurable in Settings) as plain text, and always scrolls to the newest line as the file grows — useful for watching logs. `.log` files use tail mode by default; toggle it from the external file menu on any external note. While tailing, the title bar shows `⏬` on the right; hover it to see how many lines are displayed.

## Storage and backups

The body and title autosave during editing. By default, changes save about 0.8 seconds after typing pauses, or about every 5 seconds while typing continuously. Finishing editing also saves pending changes; `Esc` does not discard edits.

Default location: `%AppData%\ScreenPinNotes`.

```text
ScreenPinNotes/
├─ settings.json
├─ logs/app.log
└─ notes/<note-id>/
   ├─ meta.json     # Position, color, reminders, etc.
   ├─ content.md    # Body
   └─ assets/       # Images
```

Use **Settings** to change storage or export/import zip backups. Imports add notes without overwriting existing ones. The default body limit is 1 MB.

Set `SCREENPINNOTES_DATA` to run with a separate data directory. Close the app before manually editing `settings.json`.

To investigate a problem that only happens on one PC, set `"EnableDiagnosticTrace": true` in `settings.json` (or the environment variable `SCREENPINNOTES_TRACE=1`) and restart the app. Resizing, folding, and the related Windows messages are written to `logs\trace.log` in the data directory, together with display and mouse settings. Note titles and contents are not recorded. Turn it off again when done.

## Development and license

Use Windows and the .NET 8 SDK.

```powershell
git clone https://github.com/umineko73/ScreenPinNotes.git
cd ScreenPinNotes
dotnet build
dotnet run --project src
dotnet test
```

Build release zips with `powershell -ExecutionPolicy Bypass -File scripts/publish.ps1` (output: `artifacts/`). See [localization instructions](localization.md) for translations.

[GNU General Public License v3.0 or later](../LICENSE) · Copyright (C) 2026 umineko73
