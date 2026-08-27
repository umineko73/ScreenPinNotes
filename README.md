# ScreenStickyNotes

English | [日本語](README.ja.md)

A desktop sticky notes app for Windows 11. Fold a note down to just its title bar — the title (or first body line) stays visible — so you can keep many notes on screen without them taking over the desktop.

![ScreenStickyNotes screenshot](docs/screenshot-en.png)

## Features

- **Fold**: collapse to title-bar height, title/first line still visible
- **View / Edit mode**: double-click to edit, Escape to view
- **Markdown rendering**, including tables, checklists, and images
- **Snapping** to screen edges and other notes while dragging/resizing
- **Semi-transparency**: per-note opacity 10–100%, right-click the title
- 24 colors, 40 icons, independent fonts for body/title
- Always-on-top (per note), lives in the system tray
- Japanese/English, Light/Dark mode
- Paste images from the clipboard, paste/copy Excel tables as Markdown
- Startup registration, autosave, and a movable note storage folder — all from the tray menu

## Markdown syntax

Edit mode shows raw Markdown; view mode renders it.

| Syntax | Effect |
|------|------|
| `# Heading` – `###### Heading` | Headings (6 levels) |
| `**bold**` / `*italic*` | **bold** / *italic* |
| `` `code` `` / ` ```block``` ` | Inline code / code block |
| `- item` / `1. item` / `- [ ]` | Lists, incl. clickable checklists |
| `> quote` / `---` | Blockquote / horizontal rule |
| `\| a \| b \|` | Table |
| `[label](url)` | Link |
| `![alt](assets/image.png)` | Image (`{width=240}` to size it) |

Pasted images are saved as PNGs under the note's `assets` folder; right-click an image to resize (0–200%) or remove it. Only local file images render inline — an `http(s)://` image URL converts to `![image](...)` syntax but won't preview.

## Download

Get it from [Releases](https://github.com/umineko73/ScreenStickyNotes/releases) and unzip — no installation needed.

| File | Requires |
|----------|-----------|
| `ScreenStickyNotes-x.y.z-win-x64.zip` (~68MB) | Nothing. Pick this if unsure |
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
| Right-click the title | Edit title, z-order, opacity, delete |
| Ctrl+wheel over body/image | Resize font / image |
| Tray icon left/right-click | Show all / open menu |

The tray menu's Settings submenu covers startup, storage folder, dark mode, language, and the title-preview/fold-animation/fold-button toggles.

## settings.json

Saved at `%APPDATA%\ScreenStickyNotes\settings.json` — `Language`, `Theme`, `StorageRoot`, and various UI toggles/timings. Edit it directly and restart the app to apply.

## Data location

```
%AppData%\ScreenStickyNotes\
  settings.json
  logs\app.log
  notes\{note id}\meta.json, content.md, assets\
```

Notes live under `StorageRoot` (defaults to the folder above); change it from the tray's **Settings > Select note folder...**, or set it via the `SCREENSTICKYNOTES_DATA` environment variable before first run.

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

Each note is a single `Window`. `WindowStyle="None"` plus `WindowChrome` draws the custom title bar.

## License

[GNU General Public License v3.0](LICENSE)

Copyright (C) 2026 umineko73

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but **WITHOUT ANY WARRANTY**; see the GNU General Public License for more details.
