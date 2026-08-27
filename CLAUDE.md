# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build                     # build (Debug)
dotnet build -c Release          # build Release
dotnet run --project src         # run the app locally
dotnet test                      # run tests/ScreenStickyNotes.Tests
```

`tests/ScreenStickyNotes.Tests` (xUnit) covers logic that doesn't need a live
window: `Services/*` (`LinkDetector`, `MarkdownTableClipboard`,
`MarkdownRenderer`, `SampleNoteFactory`, `StorageService`) and `Models/*`
(`AppSettings.Normalize`, `StickyNote`). It targets `net8.0-windows` +
`UseWPF=true` since `MarkdownRenderer` returns WPF `FlowDocument` types.
Constructing a WPF `Control` (e.g. `CheckBox`) needs an STA thread with a
Dispatcher — use `[WpfFact]`/`[WpfTheory]` (`Xunit.StaFact`, pinned to
`1.2.69` because later versions pull in `xunit.v3.*` and collide with this
project's xunit v2) instead of plain `[Fact]` for those. Most of the actual
UI/interaction code in `Views/StickyNoteWindow.xaml.cs` is still untested —
verify UI/behavior changes there by actually running the app (see the `run`
skill / `verify-ui-fixes-by-testing` guidance: reason about GUI fixes only
after confirming them by launching the app, not by inspection alone).

To avoid touching your real notes while testing, point the app at a scratch data folder:

```bash
SCREENSTICKYNOTES_DATA=/path/to/scratch dotnet run --project src
```

Build release zips (self-contained + framework-dependent) into `artifacts/`:

```bash
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1            # uses <Version> from the csproj
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1 -Version 0.2.0
```

Releases are also built by CI: pushing a `v*` tag triggers `.github/workflows/release.yml`, which runs the same `publish.ps1` and attaches both zips to a **draft** GitHub release — publish it manually afterward.

## Architecture

Single WPF project targeting `net8.0-windows` (`src/ScreenStickyNotes.csproj`, `OutputType=WinExe`). No MVVM framework, no DI container — plain code wiring throughout.

**Single-instance app, one `Window` per note.** `App.xaml.cs` is the entry point: it owns `AppSettings`, the list of open `StickyNoteWindow`s, and the tray icon (`System.Windows.Forms.NotifyIcon`, mixed into the WPF app). A second launch is prevented via a named `Mutex` keyed off the resolved data root, and a registered Windows broadcast message asks the already-running instance to show all notes before the newcomer exits — see the "二重起動防止" block in `App.xaml.cs`.

**Notes are files, not a database.** `StorageService` persists everything under `%APPDATA%\ScreenStickyNotes\` (overridable via the `SCREENSTICKYNOTES_DATA` env var): `settings.json` for `AppSettings`, and one `notes\{id}\` folder per note holding `meta.json` (everything on `StickyNote` except `Content`, which is `[JsonIgnore]`), `content.md`, and an `assets\` folder for pasted images. Keeping the Markdown body in its own file (instead of JSON-embedded) keeps notes human-readable/diffable. Writes are atomic (write `.tmp`, then `File.Move` with overwrite) and debounced per `AppSettings.Timings.SaveDebounceMs` (800ms default). `StorageService.Save` never deletes folders that aren't in the in-memory list — only explicit `DeleteNote` does — because two instances sharing a data folder previously caused one to erase the other's notes; keep that invariant if you touch save/load. A legacy single-`notes.json` format is still auto-migrated (`MigrateFromLegacy`). The instance persistence methods (`Load`/`Save`/`SaveNote`/`DeleteNote`/`LoadSettings`/`SaveSettings`) resolve paths from an instance root that defaults to the static `DataRoot`/env-var resolution; pass a temp directory to `new StorageService(dataRoot)` to get an isolated instance in tests. The *static* members (`DataRoot`, `SettingsPath`, `GetNoteDirectory`, `GetNoteAssetsDirectory`) stay as the single source of truth for the app's single-instance mutex key (`App.xaml.cs`) and asset paths (`StickyNoteWindow.xaml.cs`, `SampleNoteFactory.cs`) — don't try to make those instance-based too.

**Window chrome is largely code-behind, not the ViewModel — split across partial-class files by responsibility.** `StickyNoteWindow` (`src/Views/`) is one class split across `StickyNoteWindow.xaml.cs` (fields, constructor, settings/localization application, window lifecycle, autosave — the "core" file) plus `.Content.cs` (Markdown/plaintext loading, images, hyperlinks), `.EditMode.cs` (edit/view mode switch, status bar grow/shrink, focus handling), `.Clipboard.cs` (paste, Excel table paste/copy via `MarkdownTableClipboard`, pasted images), `.ContextMenus.cs` (title/content right-click menus, opacity, z-order), `.WindowChrome.cs` (drag, snap, `WM_SIZING` resize hook, DWM rounded corners, `WndProc`), `.Fold.cs` (fold/unfold + animation), `.Toolbar.cs` (title-bar buttons, size overlay), and `.Popups.cs` (color/icon/font pickers). This is a purely mechanical split along section-divider comments the original single ~2300-line file already had — no behavior change, and every file still shares the same private fields freely since C# partial classes don't restrict that. `ViewModels/StickyNoteViewModel.cs` is thin by comparison — mainly theme-aware brush/color derivation (light/dark, per-note color key, opacity) — so don't expect note-window logic to live in the ViewModel by default. Each note window is `WindowStyle="None"` + `WindowChrome` for the custom title bar, and `AllowsTransparency="True"` for per-note semi-transparency (`StickyNote.OpacityPercent`, boosted on hover via `AppSettings.HoverOpacityBoostPercent`). `AllowsTransparency` was previously avoided by design (risk of breaking `WindowChrome` resize hit-testing) before shipping this way — if you touch `.WindowChrome.cs`, verify resizing/dragging still works by actually running the app (with a real mouse — synthetic/scripted clicks were not reliably observed reaching this app's windows when tried), since that regression risk was never fully re-verified and none of this file's code is unit-testable.

**Markdown is hand-rolled, not a library.** `Services/MarkdownRenderer.cs` parses a Markdown subset (headings, bold/italic, inline code, fences, lists/checkboxes, blockquote, hr, tables, images, links) directly into WPF `FlowDocument` `Block`s — no Markdig or other dependency. Edit mode always shows/edits the raw source string; only View mode renders through `MarkdownRenderer`, so content is never mutated by the renderer.

**Localization is a single switch statement.** `Services/LocalizationService.T(key)` returns ja/en strings from one big `switch` keyed on `Settings.Language` — there's no `.resx`/resource system. Adding UI text means adding a `case` here for both languages.

**Sample notes are real files, copied at first run.** `SampleNoteFactory` reads `meta.json` + `content.md` per language/kind from `SampleNotes\{ja|en}\{markdown|usage}\`, which the csproj copies next to the built exe (`CopyToOutputDirectory=PreserveNewest`). On first launch (empty notes folder), these are instantiated as real notes via `StorageService`. If the `SampleNotes` folder is missing (e.g. exe copied out on its own), sample creation is silently skipped rather than failing startup.

## Conventions

- README is bilingual: `README.md` is the English default, `README.ja.md` is the Japanese counterpart (each links to the other at the top). Keep both in sync when documenting user-facing changes.
- Write git commit messages in English; chat with the user stays in whatever language they use.
- Version is set in one place: `<Version>` in `src/ScreenStickyNotes.csproj`. `scripts/publish.ps1` reads it by default.
