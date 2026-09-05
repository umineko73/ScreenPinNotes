# Usage Help

This note has editing locked. The body text and title text are protected, but checkboxes, display sizes, position, color, and similar view settings can still be adjusted.

## Basics

- Double-click the body: switch to edit mode
- `Esc`: return to view mode
- Double-click the title bar: collapsed view / expanded view
- Drag the title bar: move the note
- Click a link: open it
- In edit mode, use `Ctrl + click` to open a link
- Right-drag in a body pane with scrollbars: scroll the pane

## Toolbars

A mini toolbar appears above the context menu: `A-` / `A+` change body size, `T-` / `T+` change title size, `Aa` opens fonts, 🦊 opens icons, and 🎨 opens colors. There are no separate Change color / Change icon menu items.

During editing, the same toolbar appears below the note with a green `✓` to finish editing. `Ctrl+Enter` or `Esc` also returns to view mode; these actions do not discard edits. The toolbar follows movement and resizing and hides while the link editor is open.

## Editing size

The first edit uses the normal note size. Resizing during editing remembers a separate width and height for that note. Finishing restores the normal size; the next edit restores the editing size. Position is shared with normal view and adjusted if necessary to keep the window on screen. Collapsed width remains separate.

## Colors, icons, and fonts

- Six dark presets stay dark even in light mode. App dark mode also darkens the existing light presets. Text, links, and palette swatches follow the effective note colors.
- Icons are grouped into colors/shapes, urgency/priority, status/progress, notes/documents, ideas/research, schedule/communication, work/development, daily life, and animals. Choices include 🔥, colored circles/squares, and 19 animals. 🔗 is reserved for external-file status; use 🌐 for Web notes.
- Installed fonts use Japanese display names when available. Confirmed symbol-only fonts are excluded; unknown fonts remain available. Names load on first opening, with a retry action on failure.
- Up to five frequently used fonts appear at the top with `★`. Selection counts are shared across notes and survive restarts.

## Markdown input assistance

While editing, select text and right-click **Markdown formatting** for bold, strikethrough, inline code, headings 1–3, bullets, checklists, and links. Line formatting applies to whole affected lines. With no selection, inline formatting places the caret between markers. Reapplying the matching supported format removes it. Use `Ctrl+Z` to undo.

Place the caret inside an existing link and choose **Edit link...** to edit its display text and URL separately. The URL wraps and the dialog can be resized. **Markdown formatting > Insert / edit link...** also creates new links.

The helper does not validate or repair every possible Markdown combination. Rendering failures fall back to source text. Deep nesting (16 levels), long inline text (over 8,192 characters), and large documents (over 131,072 characters or 2,000 newline characters) limit parsing and display source text without deleting the content.

## Mouse Wheel

| Area | Action |
| --- | --- |
| Body | `Ctrl + wheel` changes the body font size |
| Title bar | `Ctrl + wheel` changes the title font size |
| Image | `Ctrl + wheel` changes the image display size |

## Moving and Snapping

![Expanded view vs. collapsed view appearance](assets/window-position-guide.png)

Left: expanded view (body text visible). Right: collapsed view (only the title bar remains). Each view's position can be adjusted independently with the actions below.

- Normal drag: keep collapsed view and expanded view positions synchronized
- `Ctrl + drag`: adjust only the current view
- `Alt + drag`: disable snapping
- `Ctrl + Alt + drag`: adjust only the current view without snapping
- Notes with separated collapsed view and expanded view positions show `⛓️‍💥` in the title bar
- A setting can restore single-click switching

## Title Context Menu

- Edit title
- Change stacking order
- Change opacity
- Align to collapsed position
- Set / clear a reminder
- For external-file notes, open the file / open its folder / convert to an editable note
- Lock editing
- Hide
- Delete note (keep original file for external-file notes)

While editing is locked, the title text cannot be edited and the note cannot be deleted.

## Body Context Menu

- Cut / copy / paste
- Paste as Markdown link
- Paste / copy Excel tables
- Open link
- Convert to Markdown link
- Resize note to fit images
- Set / clear a reminder
- External-file actions for external-file notes
- Lock editing
- Hide
- Delete note (keep original file for external-file notes)

While editing is locked, text input, paste, link conversion, image insertion, and image removal are disabled.

## External-File Notes

- Use "Open external file as note..." from the tray menu to show a `.md` / `.txt` file as a read-only note
- Use "Note list..." from the tray menu to search titles, body text, and external file paths
- External-file notes show `🔗` at the left of the title bar; hover the title or `🔗` to see the file path
- Notes reload when the external file changes
- Image size changes in external-file notes are saved as note-local display settings; the original file is not modified
- "Convert to editable note" keeps the content in the note and stops following the external file. The original file is kept
- "Delete note (keep original file)" deletes only the note, leaving the original file intact

## Reminders

- Set a one-time reminder from a note context menu or from "Note list..."
- Notes with reminders show `⏰` in the title bar
- Hover `⏰` to see the scheduled time
- When due, the note is shown and you can choose Done, 5 minutes, 15 minutes, or 1 hour snooze

## Title Bar Buttons

| Button | Action |
| --- | --- |
| + | Add a note using this note's color, icon, and font |
| 📌 | Always on top |
| Up / Down | Collapsed view / Expanded view |
| 🔒 | Shows that editing is locked |
| ⛓️‍💥 | Shows that collapsed view and expanded view positions are separated |
| 🔗 | Shows an external-file note |
| ⏰ | Shows that a reminder is set |

The lock is only a status indicator. It is not clickable, so it does not interfere with dragging or switching collapsed/expanded view.

## Tray Icon

- Left-click: show / hide all notes
- Right-click: show hidden notes, create a note, open an external-file note, open the note list, open settings, exit
- Settings: storage folder, export, import, language, dark mode, and more

## Storage and Backup

- Data folder: `%APPDATA%\ScreenPinNotes`
- Each note is stored under `notes` with `meta.json`, `content.md`, and `assets`
- "Export notes..." creates a zip backup
- "Import notes..." adds notes without overwriting existing notes
