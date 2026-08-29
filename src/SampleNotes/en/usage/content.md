# Usage Help

![Fold and unfold position overview](assets/window-position-guide.png)

This note has editing locked. The body text and title text are protected, but checkboxes, display sizes, position, color, and similar view settings can still be adjusted.

## Basics

- Double-click the body: switch to edit mode
- `Esc`: return to view mode
- Click the title bar: fold / unfold
- Drag the title bar: move the note
- Click a link: open it
- In edit mode, use `Ctrl + click` to open a link

## Mouse Wheel

| Area | Action |
| --- | --- |
| Body | `Ctrl + wheel` changes the body font size |
| Title bar | `Ctrl + wheel` changes the title font size |
| Image | `Ctrl + wheel` changes the image display size |

## Moving and Snapping

- Normal drag: keep folded and unfolded positions synchronized
- `Shift + drag`: adjust folded and unfolded positions independently
- `Alt + drag`: disable snapping
- `Alt + Shift + drag`: adjust only the current state without snapping

## Title Context Menu

- Edit title
- Change z order
- Change opacity
- "Open here" while folded
- Lock editing
- Hide
- Delete

While editing is locked, the title text cannot be edited and the note cannot be deleted.

## Body Context Menu

- Cut / copy / paste
- Paste as Markdown link
- Paste / copy Excel tables
- Open link
- Convert to Markdown link
- Fit window to images
- Lock editing
- Hide
- Delete

While editing is locked, text input, paste, link conversion, image insertion, and image removal are disabled.

## Title Bar Buttons

| Button | Action |
| --- | --- |
| + | Add a note using this note's color, icon, and font |
| 📌 | Always on top |
| Up / Down | Fold / unfold |
| 🔒 | Shows that editing is locked |

The lock is only a status indicator. It is not clickable, so it does not interfere with dragging or folding.

## Tray Icon

- Left-click: show / hide all notes
- Right-click: show hidden notes, create a note, open settings, exit
- Settings: storage folder, export, import, language, dark mode, and more

## Storage and Backup

- Data folder: `%APPDATA%\ScreenStickyNotes`
- Each note is stored under `notes` with `meta.json`, `content.md`, and `assets`
- "Export notes..." creates a zip backup
- "Import notes..." adds notes without overwriting existing notes
