# Usage Help

This note has editing locked. The body text and title text are protected, but checkboxes, display sizes, position, color, and similar view settings can still be adjusted.

## Basics

- Double-click the body: switch to edit mode
- `Esc`: return to view mode
- Click the title bar: fold / unfold
- Drag the title bar: move the note
- Click a link: open it
- In edit mode, use `Ctrl + click` to open a link
- Right-drag in a body pane with scrollbars: scroll the pane

## Mouse Wheel

| Area | Action |
| --- | --- |
| Body | `Ctrl + wheel` changes the body font size |
| Title bar | `Ctrl + wheel` changes the title font size |
| Image | `Ctrl + wheel` changes the image display size |

## Moving and Snapping

![Unfolded vs. folded appearance](assets/window-position-guide.png)

Left: unfolded (body text visible). Right: folded (only the title bar remains). Each state's position can be adjusted independently with the actions below.

- Normal drag: keep folded and unfolded positions synchronized
- `Ctrl + drag`: adjust only the current state
- `Alt + drag`: disable snapping
- `Ctrl + Alt + drag`: adjust only the current state without snapping

## Title Context Menu

- Edit title
- Change z order
- Change opacity
- "Open here" while folded
- For external-file notes, open the file / open its folder / convert to a normal note
- Lock editing
- Hide
- Delete, or unlink for external-file notes

While editing is locked, the title text cannot be edited and the note cannot be deleted.

## Body Context Menu

- Cut / copy / paste
- Paste as Markdown link
- Paste / copy Excel tables
- Open link
- Convert to Markdown link
- Fit window to images
- External-file actions for external-file notes
- Lock editing
- Hide
- Delete, or unlink for external-file notes

While editing is locked, text input, paste, link conversion, image insertion, and image removal are disabled.

## External-File Notes

- Use "Open external file as note..." from the tray menu to show a `.md` / `.txt` file as a read-only note
- Use "Note list..." from the tray menu to search titles, body text, and external file paths
- External-file notes show `🔗` at the left of the title bar; hover the title or `🔗` to see the file path
- Notes reload when the external file changes
- Unlinking an external-file note removes only the note link; the original file is not deleted

## Title Bar Buttons

| Button | Action |
| --- | --- |
| + | Add a note using this note's color, icon, and font |
| 📌 | Always on top |
| Up / Down | Fold / unfold |
| 🔒 | Shows that editing is locked |
| 🔗 | Shows an external-file note |

The lock is only a status indicator. It is not clickable, so it does not interfere with dragging or folding.

## Tray Icon

- Left-click: show / hide all notes
- Right-click: show hidden notes, create a note, open an external-file note, open the note list, open settings, exit
- Settings: storage folder, export, import, language, dark mode, and more

## Storage and Backup

- Data folder: `%APPDATA%\ScreenStickyNotes`
- Each note is stored under `notes` with `meta.json`, `content.md`, and `assets`
- "Export notes..." creates a zip backup
- "Import notes..." adds notes without overwriting existing notes
