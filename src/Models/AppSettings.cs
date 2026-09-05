// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Globalization;

namespace ScreenPinNotes.Models;

/// <summary>アプリケーション全体に関する設定。</summary>
public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }
    public bool ShowTitlePreviewTooltip { get; set; }
    public bool EnableFoldAnimation { get; set; }
    public bool ShowFoldButton { get; set; }
    public bool DoubleClickToToggleView { get; set; } = true;
    public int HoverOpacityBoostPercent { get; set; } = 10;
    public int MaxNoteContentBytes { get; set; } = 1024 * 1024;
    public string StorageRoot { get; set; } = "";
    public string NotesRoot { get; set; } = "";
    public string Language { get; set; } = "ja";
    public string Theme { get; set; } = "Light";
    public Dictionary<string, int> FontUsage { get; set; } = new();
    public TimingSettings Timings { get; set; } = new();
    public InteractionSettings Interaction { get; set; } = new();
    public LayoutSettings Layout { get; set; } = new();

    // タイトルバーに付けられるアイコンパレット。「アイコンなし」は常に先頭に
    // 別途表示するのでここには含めない。settings.json で好きな絵文字に
    // 差し替えられる。
    public List<string> IconPalette { get; set; } = DefaultIconPalette();
    public int IconPaletteVersion { get; set; }

    public static AppSettings CreateDefault()
        => new() { Language = GetDefaultLanguage(CultureInfo.CurrentUICulture) };

    public static string GetDefaultLanguage(CultureInfo culture)
        => culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
            ? "ja"
            : "en";

    public static readonly (string Key, string[] Icons)[] IconGroups =
    [
        ("IconColors", ["🔴", "🟠", "🟡", "🟢", "🔵", "🟣", "🟤", "⚫", "⚪", "🟥", "🟧", "🟨", "🟩", "🟦", "🟪", "🟫", "⬛", "⬜"]),
        ("IconPriority", ["🔥", "🚨", "⚠", "❗", "‼", "⭐", "🚩"]),
        ("IconStatus", ["☐", "✅", "☑", "🔄", "⏳", "💤", "⏸", "🚧", "🏁"]),
        ("IconNotes", ["📌", "📝", "📋", "📎", "📁", "📚", "🌐"]),
        ("IconIdeas", ["💡", "🔍", "❓", "💭", "🎯", "🧪"]),
        ("IconSchedule", ["📅", "⏰", "🔔", "✉", "📞", "💬", "👤", "👥", "🗓"]),
        ("IconWork", ["💼", "🐛", "🔧", "⚙", "💻", "🖥", "🚀", "✏"]),
        ("IconDaily", ["🏠", "🛒", "📦", "💰", "☕", "🎁", "❤", "🍽", "🎵", "🔑", "🔒", "👍", "🎉", "🌟", "🌱"]),
        ("IconAnimals", ["🐶", "🐱", "🐰", "🦊", "🐻", "🐼", "🐨", "🐸", "🐧", "🦉", "🐢", "🐙", "🐝", "🦋", "🦄"]),
    ];

    public static List<string> DefaultIconPalette()
        => IconGroups.SelectMany(group => group.Icons).Distinct().ToList();

    private static List<string> LegacyIconPalette() =>
    [
        "📌", "⭐", "❗", "❓", "✅", "🔥", "💡", "📝",
        "📋", "📅", "⏰", "🔔", "🎯", "🚀", "💼", "🏠",
        "🛒", "🍽", "☕", "🎵", "📚", "✏", "🔧", "🐛",
        "💰", "📞", "✉", "🔑", "🔒", "❤", "👍", "🎉",
        "🎁", "🌟", "⚠", "🚨", "📦", "🗓", "🧪", "🌱",
    ];

    public void Normalize()
    {
        if (!string.Equals(Language, "en", StringComparison.OrdinalIgnoreCase))
            Language = "ja";
        Theme = string.Equals(Theme, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
        StorageRoot = StorageRoot?.Trim() ?? "";
        NotesRoot = NotesRoot?.Trim() ?? "";

        Timings ??= new TimingSettings();
        Interaction ??= new InteractionSettings();
        Layout ??= new LayoutSettings();
        FontUsage ??= new();
        if (IconPalette == null || IconPalette.Count == 0 || IconPalette.SequenceEqual(LegacyIconPalette()))
            IconPalette = DefaultIconPalette();
        // Reserve the chain symbol for external-file status, including saved palettes.
        IconPalette = IconPalette.Select(icon => icon is "🔗" or "🔗️" ? "🌐" : icon).Distinct().ToList();
        if (IconPaletteVersion < 1)
        {
            IconPalette = IconPalette.Concat(IconGroups.Single(group => group.Key == "IconAnimals").Icons).Distinct().ToList();
            IconPaletteVersion = 1;
        }

        Timings.TitlePreviewDelayMs = Math.Max(0, Timings.TitlePreviewDelayMs);
        Timings.ToolbarHideDelayMs = Math.Max(0, Timings.ToolbarHideDelayMs);
        Timings.SizeOverlayDurationMs = Math.Max(0, Timings.SizeOverlayDurationMs);
        Timings.FoldAnimationMs = Math.Max(0, Timings.FoldAnimationMs);
        Timings.SizeOverlayFadeMs = Math.Max(0, Timings.SizeOverlayFadeMs);
        Timings.ToolbarFadeMs = Math.Max(0, Timings.ToolbarFadeMs);
        Timings.SaveDebounceMs = Math.Max(0, Timings.SaveDebounceMs);

        HoverOpacityBoostPercent = Math.Clamp(HoverOpacityBoostPercent, 0, 90);
        MaxNoteContentBytes = Math.Max(1024, MaxNoteContentBytes);

        Interaction.SnapDistance = Math.Max(0, Interaction.SnapDistance);
        Interaction.ClickDragThresholdPx = Math.Max(0, Interaction.ClickDragThresholdPx);

        Layout.UnfoldedMinWidth = Math.Max(80, Layout.UnfoldedMinWidth);
        Layout.ResizeBorder = Math.Max(0, Layout.ResizeBorder);
        Layout.RootBorderThickness = Math.Max(0, Layout.RootBorderThickness);
        Layout.DefaultNoteWidth = Math.Max(Layout.UnfoldedMinWidth, Layout.DefaultNoteWidth);
        Layout.DefaultNoteHeight = Math.Max(80, Layout.DefaultNoteHeight);
    }
}

public sealed class TimingSettings
{
    public int TitlePreviewDelayMs { get; set; } = 500;
    public int ToolbarHideDelayMs { get; set; } = 180;
    public int SizeOverlayDurationMs { get; set; } = 900;
    public int FoldAnimationMs { get; set; } = 150;
    public int SizeOverlayFadeMs { get; set; } = 350;
    public int ToolbarFadeMs { get; set; } = 110;
    public int SaveDebounceMs { get; set; } = 800;
}

public sealed class InteractionSettings
{
    public double SnapDistance { get; set; } = 10;
    public double ClickDragThresholdPx { get; set; } = 4;
}

public sealed class LayoutSettings
{
    public double UnfoldedMinWidth { get; set; } = 140;
    public double ResizeBorder { get; set; } = 5;
    public double RootBorderThickness { get; set; } = 1;
    public double NewNoteBaseX { get; set; } = 150;
    public double NewNoteBaseY { get; set; } = 150;
    public double NewNoteCascadeStep { get; set; } = 20;
    public double NewNoteNearCursorOffset { get; set; } = 12;
    public double DefaultNoteWidth { get; set; } = 260;
    public double DefaultNoteHeight { get; set; } = 220;
}
