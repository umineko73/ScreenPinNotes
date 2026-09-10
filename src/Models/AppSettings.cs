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
    /// <summary>タイトルバーを隠している付箋の左端に、見分けのための帯を出すかどうか。</summary>
    public bool ShowTitleBarHiddenSpine { get; set; } = true;
    /// <summary>
    /// 帯の置き方。<see cref="SpineStyleInset"/> は端から
    /// <see cref="LayoutSettings.TitleBarHiddenSpineInset"/> だけ離し、角の丸みに
    /// 食われないよう上下も詰めて、太さの変わらない1本の縦線として描く。
    /// <see cref="SpineStyleEdge"/> は従来どおり左端いっぱいに敷く
    /// （角を丸めていると、その分だけ端が細く見える）。
    /// </summary>
    public string TitleBarHiddenSpineStyle { get; set; } = SpineStyleInset;

    /// <summary>左端に貼り付ける従来の帯。</summary>
    public const string SpineStyleEdge = "Edge";
    /// <summary>端から少し離し、本文との間に浮かせる帯。</summary>
    public const string SpineStyleInset = "Inset";
    /// <summary>
    /// 付箋の外枠の色。<see cref="NoteBorderNone"/> / <see cref="NoteBorderGray"/> /
    /// <see cref="NoteBorderNoteColor"/>、または "#RRGGBB" 形式の色。
    /// </summary>
    public string NoteBorderColor { get; set; } = NoteBorderNone;
    /// <summary>付箋のアイコンを色を抜いて描くかどうか。</summary>
    public bool MonochromeIcons { get; set; }

    public const string NoteBorderNone = "None";
    public const string NoteBorderGray = "Gray";
    public const string NoteBorderNoteColor = "NoteColor";

    /// <summary>既定の外枠の色。設定が「グレー」のときに使う。</summary>
    public const string DefaultNoteBorderHex = "#9A9A9A";
    public bool DoubleClickToToggleView { get; set; } = true;
    /// <summary>各付箋のウィンドウをタスクバーにも表示するかどうか。</summary>
    public bool ShowNotesInTaskbar { get; set; }
    /// <summary>タスクトレイアイコンを左クリックしたときの動作。
    /// "ToggleAll"（全付箋の表示・非表示切り替え）または "NewNote"（新規付箋を追加）。</summary>
    public string TrayClickAction { get; set; } = "ToggleAll";
    public int HoverOpacityBoostPercent { get; set; } = 10;
    public int MaxNoteContentBytes { get; set; } = 1024 * 1024;
    public string StorageRoot { get; set; } = "";
    public string NotesRoot { get; set; } = "";
    public string Language { get; set; } = "ja";
    public string Theme { get; set; } = "Light";
    public string NewNoteHotkey { get; set; } = ScreenPinNotes.Services.GlobalNoteHotkey.DefaultGesture;
    public List<string> SearchHistory { get; set; } = new();
    public Dictionary<string, int> FontUsage { get; set; } = new();
    public TimingSettings Timings { get; set; } = new();
    public InteractionSettings Interaction { get; set; } = new();
    public LayoutSettings Layout { get; set; } = new();

    // タイトルバーに付けられるアイコンパレット。「アイコンなし」は常に先頭に
    // 別途表示するのでここには含めない。settings.json で好きな絵文字に
    // 差し替えられる。
    /// <summary>
    /// 新しく作る付箋の初期値。既存の付箋の「＋」から増やしたときは、
    /// そちらの書式を引き継ぐのでこの値は使わない。
    /// </summary>
    public NoteDefaultSettings NoteDefaults { get; set; } = new();

    public List<string> IconPalette { get; set; } = DefaultIconPalette();
    public int IconPaletteVersion { get; set; }

    /// <summary>
    /// 外枠の色の設定を読める値にそろえる。決め打ちの3種類は表記ゆれを吸収し、
    /// それ以外は "#RRGGBB" 形式の色として通す（settings.json を直接書く人向け）。
    /// どちらでもなければグレーに戻す。既定の「なし」へ倒すと枠が消えるだけで、
    /// 書き損じたことに気付けないため、直すべき状態が見える色を選ぶ。
    /// </summary>
    public static string NormalizeNoteBorderColor(string? value)
    {
        var text = (value ?? "").Trim();
        foreach (var known in new[] { NoteBorderNone, NoteBorderGray, NoteBorderNoteColor })
            if (string.Equals(text, known, StringComparison.OrdinalIgnoreCase))
                return known;
        return IsHexColor(text) ? text.ToUpperInvariant() : NoteBorderGray;
    }

    private static bool IsHexColor(string text)
    {
        if (text.Length is not (4 or 7 or 9) || text[0] != '#') return false;
        for (var i = 1; i < text.Length; i++)
            if (!Uri.IsHexDigit(text[i]))
                return false;
        return true;
    }

    public static AppSettings CreateDefault()
        => new() { Language = GetDefaultLanguage(CultureInfo.CurrentUICulture) };

    public static string GetDefaultLanguage(CultureInfo culture)
        => ScreenPinNotes.Services.LocalizationService.ResolveLanguage(culture.Name);

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
        ("IconAnimals", ["🐶", "🐱", "🐈", "🐰", "🦊", "🐻", "🐼", "🐨", "🦦", "🐸", "🐧", "🐓", "🦉", "🦜", "🐢", "🐙", "🐝", "🦋", "🦄"]),
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
        Language = ScreenPinNotes.Services.LocalizationService.ResolveLanguage(Language, "ja");
        Theme = string.Equals(Theme, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
        TrayClickAction = string.Equals(TrayClickAction, "NewNote", StringComparison.OrdinalIgnoreCase) ? "NewNote" : "ToggleAll";
        StorageRoot = StorageRoot?.Trim() ?? "";
        NotesRoot = NotesRoot?.Trim() ?? "";

        Timings ??= new TimingSettings();
        Interaction ??= new InteractionSettings();
        Layout ??= new LayoutSettings();
        FontUsage ??= new();
        NewNoteHotkey = ScreenPinNotes.Services.GlobalNoteHotkey.TryParse(NewNoteHotkey, out _, out _, out var hotkey) ? hotkey : ScreenPinNotes.Services.GlobalNoteHotkey.DefaultGesture;
        SearchHistory = (SearchHistory ?? []).Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim()).Distinct(StringComparer.Ordinal).Take(30).ToList();
        if (IconPalette == null || IconPalette.Count == 0 || IconPalette.SequenceEqual(LegacyIconPalette()))
            IconPalette = DefaultIconPalette();
        // Reserve the chain symbol for external-file status, including saved palettes.
        IconPalette = IconPalette.Select(icon => icon is "🔗" or "🔗️" ? "🌐" : icon).Distinct().ToList();
        // 動物を増やすたびにここを上げる。保存済みパレットに不足分だけ配り、
        // Distinct で既にある分は二重にならない。
        if (IconPaletteVersion < 2)
        {
            IconPalette = IconPalette.Concat(IconGroups.Single(group => group.Key == "IconAnimals").Icons).Distinct().ToList();
            IconPaletteVersion = 2;
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
        // 上限は最小幅140pxの付箋でも本文を圧迫しない範囲。0は「出さない」と
        // 見分けが付かなくなるので、消したいときは ShowTitleBarHiddenSpine を使う。
        Layout.TitleBarHiddenSpineWidth = Math.Clamp(Layout.TitleBarHiddenSpineWidth, 1, 12);
        // 上限は本文の左余白（Padding 8px）を大きく越えない範囲。これ以上ずらすと
        // 帯が本文の下に潜り込み、目印として読めなくなる。
        Layout.TitleBarHiddenSpineInset = Math.Clamp(Layout.TitleBarHiddenSpineInset, 0, 12);
        TitleBarHiddenSpineStyle = NormalizeSpineStyle(TitleBarHiddenSpineStyle);
        // 上限は付箋の高さが最小のとき（畳んだ1行）でも輪郭が破綻しない範囲。
        Layout.NoteCornerRadius = Math.Clamp(Layout.NoteCornerRadius, 0, 16);
        NoteBorderColor = NormalizeNoteBorderColor(NoteBorderColor);
        Layout.DefaultNoteWidth = Math.Max(Layout.UnfoldedMinWidth, Layout.DefaultNoteWidth);
        Layout.DefaultNoteHeight = Math.Max(80, Layout.DefaultNoteHeight);

        NoteDefaults ??= new NoteDefaultSettings();
        NoteDefaults.ColorKey = Blank(NoteDefaults.ColorKey) ? "yellow" : NoteDefaults.ColorKey.Trim();
        NoteDefaults.FontFamily = Blank(NoteDefaults.FontFamily) ? "Yu Gothic UI" : NoteDefaults.FontFamily.Trim();
        // 本文サイズの上下限は付箋側の A- / A+ と同じにそろえる。
        NoteDefaults.FontSize = Math.Clamp(NoteDefaults.FontSize, 8, 48);
        NoteDefaults.Icon ??= "";
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    // 知らない値は既定（端から離す方）に倒す。settings.json を手で書き替えて
    // 綴りを間違えたときに、帯そのものが消えるより分かりやすい。
    private static string NormalizeSpineStyle(string? value)
        => string.Equals(value?.Trim(), SpineStyleEdge, StringComparison.OrdinalIgnoreCase)
            ? SpineStyleEdge
            : SpineStyleInset;
}

/// <summary>新しい付箋の初期値。</summary>
public sealed class NoteDefaultSettings
{
    public string ColorKey { get; set; } = "yellow";
    public string FontFamily { get; set; } = "Yu Gothic UI";
    public double FontSize { get; set; } = 13;
    public string Icon { get; set; } = "";
    public bool TitleBarHidden { get; set; }
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
    /// <summary>タイトルバーを隠している付箋の左端に出す帯の太さ。</summary>
    public double TitleBarHiddenSpineWidth { get; set; } = 3;
    /// <summary>
    /// 帯を付箋の左端から何ピクセル離すか。既定の3pxは、外枠(1px)と本文の
    /// 左余白(8px)のちょうど中ほどに3px幅の帯が収まる位置。
    /// <see cref="AppSettings.SpineStyleInset"/> のときだけ使う。
    /// </summary>
    public double TitleBarHiddenSpineInset { get; set; } = 3;
    /// <summary>付箋の四隅の丸み。0 なら角のままにする。</summary>
    public double NoteCornerRadius { get; set; }
    public double NewNoteBaseX { get; set; } = 150;
    public double NewNoteBaseY { get; set; } = 150;
    public double NewNoteCascadeStep { get; set; } = 20;
    public double NewNoteNearCursorOffset { get; set; } = 12;
    public double DefaultNoteWidth { get; set; } = 260;
    public double DefaultNoteHeight { get; set; } = 220;
}
