# ScreenPinNotes

English | [日本語](README.ja.md)

A desktop sticky notes app for Windows. Notes stay on your desktop, render Markdown, and are stored as plain files you can read without the app.

![ScreenPinNotes screenshot](docs/screenshot-en.png)

## Features

- **Notes that get out of the way** — collapse a note to its title bar, or hide the title bar entirely and collapse to the first line of the body. Notes snap to screen edges and to each other.
- **One look per note** — color, icon, body font, title font, opacity, and always-on-top, each set per note. Light and dark mode.
- **Markdown** — headings, lists, clickable checklists, tables, code, links, and images, with right-click formatting help while editing.
- **Reminders** — once, daily, weekly, or monthly, as a Windows notification with an optional snooze window.
- **Paste from anywhere** — images, Excel tables, and images inside Excel.
- **External files** — show a `.md` or `.txt` file as a read-only note that reloads when the file changes.
- **Note list** — search every note and show, hide, or delete notes from one place.
- **Plain local files** — one folder per note, autosaved. Japanese and English UI. Lives in the system tray, optionally starting with Windows.

## Download

Download a zip from [Releases](https://github.com/umineko73/ScreenPinNotes/releases) and extract it. No installation required.

| File | Requires |
|------|----------|
| `ScreenPinNotes-x.y.z-win-x64.zip` (~68MB) | Nothing |
| `ScreenPinNotes-x.y.z-win-x64-runtime.zip` (~11MB) | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

Windows 10 or later, x64. Rounded corners require Windows 11.

## Usage

| Action | Effect |
|--------|--------|
| Double-click the body | Edit the note |
| `Esc` / `Ctrl+Enter` / toolbar `✓` | Finish editing (nothing is discarded) |
| Drag the title bar | Move |
| Double-click the title bar | Collapse / expand |
| Right-click the title or body | Note menu |
| `Ctrl`+wheel over the body or an image | Resize the font or that image |
| Right-drag a scrollable body | Scroll it |
| Close a note's window (taskbar `×`, `Alt+F4`) | Hide the note — it is not deleted |
| Tray icon left-click / right-click | Show or hide all notes / open the menu |

A mini toolbar sits above the context menu and below a note being edited: `A-` `A+` for body size, `T-` `T+` for title size, `Aa` for fonts, 🦊 for icons, 🎨 for colors, and a green `✓` to finish editing. A note being edited is labelled *Editing* in its bottom left corner. Resizing a note while editing is remembered separately, so editing does not disturb its normal size.

### Collapsed and expanded views

Each note remembers a separate position and width for its collapsed and expanded views. When the two drift apart the title bar shows `⛓️‍💥`, and **Align to collapsed position** brings them back together.

| On the title bar | Effect |
|------------------|--------|
| `Ctrl` + drag | Move only the view you are in |
| `Alt` + drag | Move without snapping |
| `Ctrl+Alt` + drag | Both |

### Markdown formatting help

Select text while editing and right-click **Markdown formatting** for bold, strikethrough, inline code, headings, bullets, checklists, and links. Applying the same format again removes it, and `Ctrl+Z` undoes. **Edit link...** edits an existing link's text and URL separately. Very large or deeply nested documents show their source text instead of rendering; nothing is lost.

### Images

Pasted images are saved as PNGs in the note's `assets` folder. Resize them between 20% and 200% from the context menu or with `Ctrl`+wheel, or match the note to an image with **Fit window to image**. Only local images render inline; an `http(s)://` image URL becomes Markdown but is not previewed.

### Reminders

Set one from a note's menu or the note list: once, daily, weekly (any weekdays), or monthly (day 1–31, falling back to the last day of shorter months). Notes with a reminder show `⏰`, and hovering it shows the next time.

When one is due you get a Windows notification — clicking it opens the note list, and reminders due at the same time share one notification. You can also enable a window with Done and 5-, 15-, or 60-minute snooze; snoozing does not move the recurring time. ScreenPinNotes has to be running in the tray, and anything missed while it was closed is delivered once when it starts again.

### Tray menu

**Note list...** searches titles, bodies, reminders, and external file paths, and manages visibility, reminders, and deletion in one place. **Hidden notes** brings back notes you have hidden — *Show all notes* deliberately leaves them hidden.

**Open external file as note...** shows a `.md` or `.txt` file as a read-only note, marked `🔗`, that reloads when the file changes. Image sizes you set there are kept in the note; the original file is never modified.

**Settings** covers defaults for new notes, dark mode, language, startup, taskbar visibility, what a left-click on the tray icon does, collapse/expand behavior, the storage folder, and export/import. Changes apply and save immediately. The defaults for new notes apply to notes created from the tray — using `＋` on an existing note copies that note's look instead.

## Markdown syntax

Edit mode shows the Markdown source; view mode shows the rendered result.

| Syntax | Effect |
|--------|--------|
| `# Heading` – `###### Heading` | Headings (6 levels) |
| `**bold**` / `__bold__` | Bold |
| `*italic*` / `_italic_` | Italic |
| `~~strike~~` | Strikethrough |
| `` `code` `` / ` ```block``` ` | Inline code / code block |
| `- item` / `1. item` / `- [ ]` | Lists, including clickable checklists |
| `> quote` / `---` | Blockquote / horizontal rule |
| `\| a \| b \|` | Table (`:---`, `:---:`, `---:` set alignment) |
| `[label](url)` / `<https://example.com>` | Link (URLs containing `(` work; link titles are ignored) |
| `![alt](assets/image.png)` | Image (`{width=240}` sets its size) |

Basic escapes such as `\*` and `\[` are supported.

## Data location

```
%AppData%\ScreenPinNotes\
  settings.json
  logs\app.log
  notes\{note id}\meta.json, content.md, assets\
```

Every note is a folder: `meta.json` holds its position, size, color, reminder, and other metadata, `content.md` holds the body, and `assets` holds its images. Note bodies are limited to 1 MB by default (`MaxNoteContentBytes`).

Change where notes live from **Settings > Select note folder...**, or set `SCREENPINNOTES_DATA` before the first run. An empty folder is seeded with sample notes. Edit `settings.json` by hand only while the app is closed.

## Build

```bash
git clone https://github.com/umineko73/ScreenPinNotes.git
cd ScreenPinNotes
dotnet build
dotnet run --project src
```

Requires the .NET 8 SDK. `powershell -ExecutionPolicy Bypass -File scripts/publish.ps1` writes the distributable zips to `artifacts/`.

Each note is a single WPF `Window` with `WindowStyle="None"` and a custom title bar. The source is split into `Models/`, `ViewModels/`, `Views/`, `Services/`, and `SampleNotes/` under `src/`.

## License

[GNU General Public License v3.0 or later](LICENSE)

Copyright (C) 2026 umineko73
