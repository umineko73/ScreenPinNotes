# ScreenPinNotes

English | [日本語](README.ja.md)

Keep schedules, checklists, and photos on your desktop. ScreenPinNotes is a Windows sticky notes app with Markdown and image support. Notes autosave as local files.

[Download](https://github.com/umineko73/ScreenPinNotes/releases) · [User guide](docs/guide.md)

## Notes that fit your desktop

Use colors and icons to tell notes apart, and pin the ones you need on top. Hide title bars, collapse notes to a single line, or adjust their opacity to fit your workspace.

### Light mode

Schedules, checklists, Markdown tables, and photos in the English interface. Each note picks from 10 light and 10 dark colors regardless of the app theme, or a custom background and accent color.

![English light mode with schedules, checklists, Markdown, collapsed notes, translucent notes, and photos](docs/note_light_en.png)

### Dark mode

A darker palette for your desktop, including notes without title bars and notes that display only an image. Change the theme and interface language in Settings.

![English dark mode showing the same notes with dark backgrounds and light text](docs/note_dark_en.png)

## Features

| Feature | What you can do |
| --- | --- |
| Markdown | Display headings, bold, italic, strikethrough, lists, quotes, code, tables, and links |
| Checklists | Toggle completion with a click, even in view mode |
| Images | Paste images or drop image files to save them, resize them, fit a note to its images, and copy them out as a picture or as a file |
| draw.io | Right-click a diagram PNG or a `.drawio` file to edit it in draw.io; saving redraws the picture in the note |
| Files | Drop a file that is not an image to place it as a chip with Explorer's own icon; click to open it. Hold Shift to link to it where it is instead of copying |
| Excel tables | Paste tables from Excel and copy Markdown tables back to Excel |
| Appearance | Choose colors, fonts, icons, opacity, corners, borders, and title bar visibility |
| Placement | Pin notes on top, collapse them, snap to screen edges or other notes, and save stacking order |
| Multiple monitors | Notes that a resolution change or an unplugged monitor would leave off-screen move to a monitor that can show them, and return to their own place once that layout is back |
| Reminders | Schedule once, daily, weekly (including chosen weeks of the month), or monthly (including the last day); flash the note and snooze alerts |
| Note list | Search notes, show or hide them, and manage stacking order and reminders |
| External files | Open `.md` / `.txt` / `.log` files as read-only notes that follow file changes (including logs a program keeps open while appending), with a configurable minimum refresh interval; `.log` files default to tail mode, showing only the last N lines as plain text and auto-scrolling to the newest line |
| Local storage | Markdown bodies and JSON settings, with zip backup export and import |

## Get started

Windows 10 or later, x64. No installation needed.

1. Download a zip from [Releases](https://github.com/umineko73/ScreenPinNotes/releases) and extract it.
2. Launch `ScreenPinNotes.exe`.
3. Press `Ctrl+Alt+N` or right-click the tray icon to create a note and start typing. `Ctrl+Alt+Shift+N` turns the clipboard's text or image into a note.

| Package | Requirements |
| --- | --- |
| `ScreenPinNotes-x.y.z-win-x64.zip` | Runtime included; the usual choice |
| `ScreenPinNotes-x.y.z-win-x64-runtime.zip` | Requires .NET 8 Desktop Runtime |

## Everyday controls

| Action | Result |
| --- | --- |
| Double-click the body | Edit the Markdown source |
| `Ctrl+Enter` / `Esc` / `✓` | Save changes and finish editing |
| Drag the title bar or left-hand strip | Move the note |
| Double-click the title bar, or use the chevron (up / down) | Collapse or expand |
| Pin | Toggle always-on-top |
| Right-click the title, body, or image | Open actions for that area |
| `Ctrl` + mouse wheel | Resize body text, title text, or the image under the pointer |
| Click a checkbox | Toggle task completion |
| Open **Note list** from the tray | Search notes and manage their visibility |

The body and title autosave while editing. `Esc` keeps your changes. **Hide** keeps a note; **Delete** removes it.

See the [user guide](docs/guide.md) for all shortcuts, display modes, reminders, and settings. The app must be running to deliver reminders.

## Storage and backups

The default data folder is `%AppData%\ScreenPinNotes`.

```text
ScreenPinNotes/
├─ settings.json
└─ notes/<note-id>/
   ├─ meta.json     # Position, colors, reminders, etc.
   ├─ content.md    # Body
   └─ assets/       # Images and dropped files
```

Use Settings to change the storage location or export and import zip backups. If a note folder already exists, choose to overwrite it (including attachments), rename the imported folder automatically to keep both notes, or skip it. You can also set the `SCREENPINNOTES_DATA` environment variable to use a separate data folder.

## Development

Use Windows and the .NET 8 SDK.

```powershell
git clone https://github.com/umineko73/ScreenPinNotes.git
cd ScreenPinNotes
dotnet build
dotnet run --project src
dotnet test
```

Build release zips with `powershell -ExecutionPolicy Bypass -File scripts/publish.ps1` (output: `artifacts/`). See the [localization instructions](docs/localization.md) to add translations.

## License

[GNU General Public License v3.0 or later](LICENSE) · Copyright (C) 2026 umineko73
