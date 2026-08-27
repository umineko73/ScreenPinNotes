# ScreenStickyNotes

English | [日本語](README.ja.md)

A desktop sticky notes app for Windows 11. When folded, the first line of the body stays visible in the title bar, so you can keep track of many notes at a glance without unfolding them.

On first launch, a "Markdown sample" note and a "How to use" note are created automatically in the data folder.

![ScreenStickyNotes screenshot](docs/screenshot-en.png)

## Features

- **Fold**: collapse a note down to just its title bar height. The title (or the first body line) stays visible even folded
- **View / Edit mode**: double-click the body to edit, Escape or losing focus returns to view mode
- **Markdown rendering** (see below)
- **Snapping**: notes snap within 10px of screen edges or other notes while dragging/resizing
- **Semi-transparency**: set per-note opacity from 10% to 100% (right-click the title). Hovering automatically boosts opacity for readability
- 24-color palette, 40 icons (customizable via `IconPalette` in `settings.json`)
- Independent font size / font family for body and title
- Always-on-top (per note), lives in the system tray
- Japanese/English and Light/Dark mode switching
- Paste images from the clipboard, reference local image files, register for Windows startup, autosave
- Paste a table from Excel into Markdown, and copy a Markdown table back out for Excel
- Title hover preview, fold animation, and the fold button can each be toggled on/off from the tray's Settings menu
- The note storage folder can be changed from the tray

## Markdown syntax

Edit mode shows the raw Markdown source; view mode renders it.

| Syntax | Effect |
|------|------|
| `# Heading` through `###### Heading` | Headings (6 levels) |
| `**bold**` | **bold** |
| `*italic*` | *italic* |
| `` `code` `` | Inline code |
| ` ```code block``` ` | Code block |
| `- item` / `1. item` | Bullet / numbered list |
| `- [ ]` / `- [x]` | Checklist (clickable in view mode too) |
| `> quote` | Blockquote |
| `---` | Horizontal rule |
| `\| a \| b \|` | Table |
| `[label](https://example.com)` | Link |
| `![alt](assets/image.png)` | Image (size can be set with `{width=240}`) |

The body's context menu can convert a URL/path to a Markdown link; a URL/path pointing at an image file converts to `![image](...)` instead. Only images referenced by a local file path (an absolute path or a `file://` URI) are actually rendered inline, though — an `http(s)://` image URL also converts to `![image](...)` syntax, but won't render inline (download the image and paste it if you want it to display). When pasting a URL from the clipboard, you can also type a display name and paste it as a Markdown link.

A Markdown table can be produced by pasting from Excel; you choose whether the first row becomes the header when you paste. In view mode, selecting a Markdown table and copying it produces Excel-friendly tab-separated text.

Pasting an image in edit mode saves it as a PNG to the note folder's `assets`. Right-click an image in view mode to resize it from 0% to 200% in 20% steps. `Remove image size` clears `{width=...}` and returns it to automatic sizing. If an image is wider than the note, it scrolls horizontally.

The image's menu also offers `Remove image from note` and `Delete image file too`. The latter only works for images stored under that note's own `assets` folder — for external files or external URLs it only detaches the reference from the note; the original file is never deleted.

RAW images (e.g. Sony ARW) aren't supported for display. Export to JPEG/PNG or similar first if you need to paste one.

## Download

Get it from [Releases](https://github.com/umineko73/ScreenStickyNotes/releases). Just unzip it — no installation needed.

| File | Requires |
|----------|-----------|
| `ScreenStickyNotes-x.y.z-win-x64.zip` (~68MB) | Nothing. Pick this if unsure |
| `ScreenStickyNotes-x.y.z-win-x64-runtime.zip` (~11MB) | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

Either way, unzipping gives you `ScreenStickyNotes.exe` and `SampleNotes\` (sample notes; the app runs fine without them) in the same folder.

## Requirements

Windows 10 or later (rounded corners require Windows 11), x64. Building requires the .NET 8 SDK.

## Build and run

```bash
git clone https://github.com/umineko73/ScreenStickyNotes.git
cd ScreenStickyNotes
dotnet build
dotnet run --project src
```

Distributable zips can be generated into `artifacts/` with `powershell -ExecutionPolicy Bypass -File scripts/publish.ps1` (override the version with `-Version 0.1.1`).

## Usage

| Action | Effect |
|------|------|
| Double-click the body | Enter edit mode |
| Escape | Return to view mode |
| Drag / single-click the title bar | Move / fold and unfold |
| Right-click the title | Edit title, z-order, opacity, delete the note |
| Ctrl+mouse wheel over the body | Change body font size |
| Ctrl+mouse wheel over an image | Resize the image |
| Tray icon left-click / right-click | Toggle show-all / open menu |

The tray icon's right-click menu offers Show all / Hide all, New note, Settings, About, and Exit. The Settings submenu has: select storage folder, start with Windows, title hover preview, fold animation, show fold button, dark mode, and language.

While editing, a toolbar (font size, font, icon, color) appears at the bottom of the note. URLs, paths, and Markdown links in the body can be opened by clicking (Ctrl+click in edit mode).

## settings.json

Saved at `%APPDATA%\ScreenStickyNotes\settings.json`. Key fields:

- `Language`: `ja` / `en`, `Theme`: `Light` / `Dark` (applied immediately from the tray menu)
- `StorageRoot`: the note storage root. Defaults to `%APPDATA%\ScreenStickyNotes` when unset
- `ShowTitlePreviewTooltip` / `EnableFoldAnimation` / `ShowFoldButton`: mirror the tray menu's ON/OFF toggles
- `HoverOpacityBoostPercent`: how much opacity to add on hover, in percent (default 10)
- `IconPalette`: candidate icons for the title bar (array of emoji, 40 by default)
- `Timings` / `Interaction` / `Layout`: fine-tuning for animation speed, snap distance, default size, etc.

Editing it directly requires restarting the app.

## Data location

The settings file is always saved under AppData.

```
%AppData%\ScreenStickyNotes\
  settings.json
  logs\app.log
```

Notes themselves are saved under `notes` inside `StorageRoot`, which defaults to the AppData folder too.

```
%AppData%\ScreenStickyNotes\
  notes\{note id}\meta.json, content.md, assets\
```

The storage location can be changed from the tray's `Settings > Select note folder...`. A `ScreenStickyNotes\notes` folder is created under whichever parent folder you pick.

Example:

```
D:\MyData\ScreenStickyNotes\
  notes\{note id}\meta.json, content.md, assets\
```

If the folder you pick doesn't already contain a `ScreenStickyNotes\notes`, you're asked whether to move your current `notes` folder there. Choosing not to move it creates fresh initial notes at the new location instead, leaving the original `notes` folder untouched.

The `settings.json` file's own location can also be changed with the `SCREENSTICKYNOTES_DATA` environment variable. It also supplies the default note storage location on first run, but once you've changed the storage folder via `Settings > Select note folder...`, that choice (`StorageRoot`) takes precedence from then on. Changes are saved with an 800ms debounce.

Exceptions caught while running are logged to `%APPDATA%\ScreenStickyNotes\logs\app.log` — the app is designed to log and keep going rather than crash outright on a UI-event exception.

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
