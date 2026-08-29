# ScreenStickyNotes

English | [日本語](README.ja.md)

A desktop sticky notes app for Windows 11.

![ScreenStickyNotes screenshot](docs/screenshot-en.png)

## Features

- Folded display
- Separate folded/unfolded positions and widths
- View mode / edit mode
- Markdown rendering
- Hidden notes
- Snapping to screen edges and other notes
- Per-note opacity
- Color, icon, body font, and title font settings
- Per-note always-on-top
- System tray
- Japanese / English
- Light mode / dark mode
- Image paste
- Paste/copy Excel tables
- Paste images from Excel
- Startup registration
- Autosave
- Configurable note storage folder

## Markdown syntax

Edit mode shows Markdown source. View mode shows rendered content.

| Syntax | Effect |
|------|------|
| `# Heading` – `###### Heading` | Headings (6 levels) |
| `**bold**` / `__bold__` | Bold |
| `*italic*` / `_italic_` | Italic |
| `~~strike~~` | Strikethrough |
| `` `code` `` / ` ```block``` ` | Inline code / code block |
| `- item` / `1. item` / `- [ ]` | Lists, incl. clickable checklists |
| `> quote` / `---` | Blockquote / horizontal rule |
| `\| a \| b \|` | Table |
| `[label](url)` / `<https://example.com>` | Link, including URLs/paths containing `(`. Link titles are ignored |
| `![alt](assets/image.png)` | Image (`{width=240}` to size it) |

Table separators `:---`, `:---:`, and `---:` set column alignment. Basic escapes such as `\*` and `\[` are supported.

Pasted images are saved as PNGs under the note's `assets` folder. Image size can be changed from 20% to 200% from the context menu or with the mouse wheel over the image. Images without an explicit width are fitted to the note width when they would overflow.

When a zoomed image has scrollbars, left-drag over the image to scroll it. **Fit window to image** from an image context menu fits the note to that image. **Fit window to images** from the body context menu fits the note to all images in the note.

Only local file images render inline — an `http(s)://` image URL converts to `![image](...)` syntax but won't preview.

## Download

Download a zip from [Releases](https://github.com/umineko73/ScreenStickyNotes/releases) and extract it. Installation is not required.

| File | Requires |
|----------|-----------|
| `ScreenStickyNotes-x.y.z-win-x64.zip` (~68MB) | Nothing |
| `ScreenStickyNotes-x.y.z-win-x64-runtime.zip` (~11MB) | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

## Requirements

Windows 10 or later (rounded corners require Windows 11), x64. Building requires the .NET 8 SDK.

## Build and run

```bash
git clone https://github.com/umineko73/ScreenStickyNotes.git
cd ScreenStickyNotes
dotnet build
dotnet run --project src
```

Distributable zips: `powershell -ExecutionPolicy Bypass -File scripts/publish.ps1` (into `artifacts/`).

## Usage

| Action | Effect |
|------|------|
| Double-click the body | Enter edit mode |
| Escape | Return to view mode |
| Drag / click the title bar | Move / fold-unfold |
| Right-click the title | Edit title, z-order, opacity, unfolded position, hide, delete |
| Ctrl+wheel over body/image | Resize font / image |
| Left-drag over a zoomed image | Scroll the image |
| Tray icon left/right-click | Show all / open menu |

Folded and unfolded positions/widths are saved separately. When a folded note near the bottom of the screen is unfolded, the app moves the window inside the screen. While folded, choose **Open here** from the title context menu to set the unfolded position to the current position.

Use **Hidden notes** in the tray menu to restore hidden notes individually or with **Show all hidden notes**. **Show all** only shows notes with `IsHidden=false`.

The tray menu's Settings submenu contains startup, storage folder, dark mode, language, title preview, fold animation, and fold button settings.

## settings.json

Saved at `%APPDATA%\ScreenStickyNotes\settings.json`. It contains `Language`, `Theme`, `StorageRoot`, UI toggles, and timing settings. When no settings file exists, the initial `Language` is chosen from the OS UI locale. Restart the app after editing the file directly.

## Data location

```
%AppData%\ScreenStickyNotes\
  settings.json
  logs\app.log
  notes\{note id}\meta.json, content.md, assets\
```

Notes are stored under `StorageRoot`. The storage folder can be changed from **Settings > Select note folder...** in the tray menu or set with the `SCREENSTICKYNOTES_DATA` environment variable before first run.

Each note's `meta.json` stores position, size, folded position/width, hidden state, and other metadata. The body is stored in `content.md`. Images are stored under `assets`.

If the storage folder contains no notes, the app copies sample notes from `SampleNotes`. Japanese OS locales use Japanese samples. Other OS locales use English samples.

## Development

```
src/
  App.xaml(.cs)      Entry point, system tray hosting
  Models/            Data models
  ViewModels/        View models
  Views/             Note windows (XAML + code-behind)
  Services/          Persistence, Markdown, link detection, etc.
  SampleNotes/       Sample notes copied on first launch
```

Each note is a single `Window`. `WindowStyle="None"` and `WindowChrome` draw the custom title bar.

## License

License: [GNU General Public License v3.0 or later](LICENSE)

Copyright (C) 2026 umineko73
