# ScreenPinNotes

English | [日本語](README.ja.md)

A Windows desktop sticky notes app with Markdown, images, and recurring reminders. Notes autosave as local files.

![ScreenPinNotes in use](docs/screenshot-en.png)

## Get started

1. Download and extract a zip from [Releases](https://github.com/umineko73/ScreenPinNotes/releases).
2. Launch `ScreenPinNotes.exe`.
3. Right-click the tray icon to create a note. Double-click its body to edit.

| Package | Requirements |
| --- | --- |
| `ScreenPinNotes-x.y.z-win-x64.zip` | Runtime included |
| `ScreenPinNotes-x.y.z-win-x64-runtime.zip` | .NET 8 Desktop Runtime |

Windows 10 or later, x64. No installation needed. Rounded corners are supported on Windows 11.

## Display modes

Title bar visibility and collapse/expand are independent settings.

| Title bar | Expanded | Collapsed |
| --- | --- | --- |
| Visible | Title and body | Title only |
| Hidden | Body with controls at the top right | First body line without Markdown formatting |

- Toggle the title bar with **Hide title bar** in the context menu.
- Use `⮝` / `⮟` or double-click the title bar to collapse or expand.
- Image-only notes show their image path in single-line mode. Narrow notes retain the end, such as `…/photos/image.png`.
- An empty title falls back to the body. A title you enter takes priority.
- Editing shows Markdown source and an *Editing* label. Edit width and height are remembered separately from view mode.

## Mouse actions

| Action | Result |
| --- | --- |
| Double-click the body | Start editing |
| Drag the title bar | Move; snap to screen edges and other notes |
| Drag the top-right control area with the title bar hidden | Move the note |
| Double-click the title bar | Collapse/expand; configurable as a single click |
| Drag an edge | Resize; collapsed notes resize horizontally only |
| Right-click the title or body | Open the menu for that area |
| Click a link | Open it |
| Click a checklist checkbox | Toggle completion |
| Right-drag a scrollable body | Scroll the body |

## Modifier keys and shortcuts

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
| `Ctrl+C` / `Ctrl+X` / `Ctrl+V` | Copy/cut/paste |
| `Alt+F4` / taskbar close | Hide the note |

Normal dragging links expanded and collapsed positions. Use **Align to collapsed position** to reconnect separated positions. Each mode remembers its own width.

## Icons and toolbars

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
| Round `✓` | Finish editing; black in light mode, white in dark mode |

The mini toolbar appears above context menus and below the note while editing. `🔒`, `⛓️‍💥`, and `⏰` are status indicators.

## Context menus

| Location | Main actions |
| --- | --- |
| Title | Edit/copy title, stacking order, opacity, reconnect positions |
| Body in view mode | Copy, open links, copy Excel tables, fit the window to images |
| Body in edit mode | Cut/paste/select all, Markdown formatting, edit links, paste Excel tables |
| Image | Image sizing and other image actions |
| Shared by title and body | Hide title bar, opacity, reminders, edit lock, hide, delete |

Use the mini toolbar for colors, fonts, and icons. Unavailable actions are disabled or hidden.

**Hide** keeps the note; **Delete** removes it. Edit lock restricts body/title editing and deletion, while appearance, position, and checklist completion remain adjustable.

## Markdown, images, and Excel

| Type | Syntax or action |
| --- | --- |
| Headings | `# Heading` through `###### Heading` |
| Formatting | `**bold**`, `*italic*`, `~~strike~~` |
| Code | Enclose inline code with one backtick; blocks with three |
| Lists | `- item`, `1. item`, `- [ ] task` |
| Quotes and rules | `> quote`, `---` |
| Tables | Pipe-separated Markdown tables, with column alignment |
| Links | `[label](URL)` or a plain URL |
| Images | `![alt](assets/image.png)`; append `{width=240}` to set width |

- Use **Markdown formatting** while editing to insert syntax. **Edit link** changes a link's label and URL.
- Pasted images are saved as PNGs in the note's `assets` folder. Local images render inline; web image URLs do not.
- Resize images between 20% and 200% using their context menu or `Ctrl` + wheel.
- Use the context menu to paste/copy Excel tables. Pasting images is also supported.
- Very large documents or deeply nested formatting fall back to source text without discarding content.

## Reminders

Configure a reminder from a note's context menu or the **Note list**.

| Repeat | Schedule |
| --- | --- |
| Once | Date and time |
| Daily | Start date and daily time |
| Weekly | Start date, time, and one or more weekdays |
| Monthly | Start date, time, and day 1–31; shorter months use their last day |

Click a Windows notification to open the note list. Simultaneous reminders share a notification. Optionally enable the alert window for 5-, 15-, or 60-minute snooze. Snoozing preserves the recurring time.

The app must be running in the tray. Missed reminders are delivered once on restart or resume. Windows notification settings control banners and sound.

## Tray, settings, and external files

| Feature | Purpose |
| --- | --- |
| Tray left-click | Toggle all notes or create a new note, as selected in settings |
| Tray right-click | Create notes, note list, hidden notes, settings, exit |
| Note list | Search titles, bodies, external paths, and more; manage visibility, reminders, and deletion |
| Hidden notes | Restore individually hidden notes; Show all does not restore them |
| Settings | New-note defaults, theme, language, startup, taskbar/tray behavior, and storage |
| Open external file as note | Display `.md` / `.txt` read-only and follow file changes |

Settings save immediately. New-note defaults apply to tray-created notes; `＋` on a note copies its appearance.

External notes show `🔗`. Their menu can open the file or folder, or convert the content into an editable note. Deleting the note or changing image display sizes does not modify the original file.

## Storage and backups

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

## Development and license

Use Windows and the .NET 8 SDK.

```powershell
git clone https://github.com/umineko73/ScreenPinNotes.git
cd ScreenPinNotes
dotnet build
dotnet run --project src
dotnet test
```

Build release zips with `powershell -ExecutionPolicy Bypass -File scripts/publish.ps1` (output: `artifacts/`). See [localization instructions](docs/localization.md) for translations.

[GNU General Public License v3.0 or later](LICENSE) · Copyright (C) 2026 umineko73
