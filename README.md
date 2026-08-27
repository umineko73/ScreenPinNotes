# ScreenStickyNotes

English | [日本語](README.ja.md)

A desktop sticky notes app for Windows 11. When folded, the first line of the body stays visible in the title bar, so you can keep track of many notes at a glance without unfolding them.

On first launch, a "Markdown sample" note and a "How to use" note are created automatically in the data folder.

![ScreenStickyNotes screenshot](docs/screenshot.png)

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
- Paste images from the clipboard, register for Windows startup, autosave
- Title hover preview, fold animation, and the fold button can each be toggled on/off from the tray menu

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

Pasting an image in edit mode saves it to the note folder's `assets`. Right-click an image in view mode to resize it from 10% to 100%.

## Download

Get it from [Releases](https://github.com/umineko73/ScreenStickyNotes/releases). Just unzip it — no installation needed.

| File | Requires |
|----------|-----------|
| `ScreenStickyNotes-x.y.z-win-x64.zip` (~68MB) | Nothing. Pick this if unsure |
| `ScreenStickyNotes-x.y.z-win-x64-runtime.zip` (~220KB) | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

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
| Right-click the title | Edit title, z-order, opacity, delete |
| Tray icon left-click / right-click | Toggle show-all / open menu |

The tray icon's right-click menu also lets you toggle registering for startup, language, dark mode, and the title hover preview / fold animation / fold button display, in addition to the actions above.

While editing, a toolbar (font size, font, icon, color) appears at the bottom of the note. URLs, paths, and Markdown links in the body can be opened by clicking (Ctrl+click in edit mode).

## settings.json

Saved at `%APPDATA%\ScreenStickyNotes\settings.json`. Key fields:

- `Language`: `ja` / `en`, `Theme`: `Light` / `Dark` (applied immediately from the tray menu)
- `ShowTitlePreviewTooltip` / `EnableFoldAnimation` / `ShowFoldButton`: mirror the tray menu's ON/OFF toggles
- `HoverOpacityBoostPercent`: how much opacity to add on hover, in percent (default 10)
- `IconPalette`: candidate icons for the title bar (array of emoji, 40 by default)
- `Timings` / `Interaction` / `Layout`: fine-tuning for animation speed, snap distance, default size, etc.

Editing it directly requires restarting the app.

## Data location

```
%AppData%\ScreenStickyNotes\
  settings.json
  notes\{note id}\meta.json, content.md, assets\
```

The storage location can be changed with the `SCREENSTICKYNOTES_DATA` environment variable. Changes are saved with an 800ms debounce.

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
