// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using ScreenPinNotes.Models;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfSolidBrush = System.Windows.Media.SolidColorBrush;

namespace ScreenPinNotes.Services;

/// <summary>付箋の配色と不透明度を一箇所で計算する。</summary>
public sealed class NoteAppearance
{
    /// <summary>ユーザーが背景色とアクセント色を選ぶ配色のキー。色は StickyNote.Custom*Color に持つ。</summary>
    public const string CustomColorKey = "custom";

    // 背景とヘッダー（アクセント）の組。アプリのライト/ダークテーマとは無関係に、
    // ライト10色・ダーク10色をこのままの色で描く。キーが "dark-" で始まるものがダーク。
    // 既存ノートの互換のため yellow/blue/green/pink/purple/gray のキーは変更しない。
    public static IReadOnlyDictionary<string, (string Bg, string Header)> Presets { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, (string Bg, string Header)>(
        new Dictionary<string, (string Bg, string Header)>()
    {
        // ライト
        ["yellow"]  = ("#FFFDE7", "#F9A825"),
        ["orange"]  = ("#FFEDD5", "#C2410C"),
        ["red"]     = ("#FEE2E2", "#B91C1C"),
        ["pink"]    = ("#FCE7F3", "#BE185D"),
        ["purple"]  = ("#EEECFB", "#6D28D9"),
        ["blue"]    = ("#DBEAFE", "#1D4ED8"),
        ["sky"]     = ("#E0F2FE", "#0369A1"),
        ["teal"]    = ("#D0F7EF", "#0F766E"),
        ["green"]   = ("#DEFAE8", "#15803D"),
        ["gray"]    = ("#F3F4F6", "#4B5563"),
        // ダーク
        ["dark-olive"]    = ("#32321E", "#9C9A4E"),
        ["dark-rust"]     = ("#3D2A1E", "#C07A48"),
        ["dark-wine"]     = ("#3F252D", "#AC657B"),
        ["dark-plum"]     = ("#35283F", "#946AAC"),
        ["dark-indigo"]   = ("#262744", "#7275C4"),
        ["dark-navy"]     = ("#1E293B", "#5277AC"),
        ["dark-teal"]     = ("#193631", "#43877B"),
        ["dark-forest"]   = ("#1F3322", "#5E9A5A"),
        ["dark-coffee"]   = ("#352D25", "#A0825F"),
        ["dark-charcoal"] = ("#252A32", "#667085"),
    });

    // 以前のパレットにあった色。選択肢からは外したが、保存済みの付箋は元の色のまま描く。
    private static readonly IReadOnlyDictionary<string, (string Bg, string Header)> LegacyPresets =
        new Dictionary<string, (string Bg, string Header)>()
    {
        ["amber"]   = ("#FEF3C7", "#B45309"),
        ["rose"]    = ("#FFE4E6", "#BE123C"),
        ["fuchsia"] = ("#FAE8FF", "#A21CAF"),
        ["violet"]  = ("#E0DAFA", "#5B21B6"),
        ["indigo"]  = ("#E0E7FF", "#4338CA"),
        ["cyan"]    = ("#CFFAFE", "#0E7490"),
        ["emerald"] = ("#D1FAE5", "#047857"),
        ["lime"]    = ("#EBF8CF", "#4D7C0F"),
        ["olive"]   = ("#F7F7DC", "#827717"),
        ["brown"]   = ("#EFEBE9", "#6D4C41"),
        ["stone"]   = ("#F5F5F4", "#57534E"),
        ["slate"]   = ("#F1F5F9", "#334155"),
        ["white"]   = ("#FFFFFF", "#9CA3AF"),
        ["dark"]    = ("#E5E7EB", "#111827"),
    };

    public static IEnumerable<string> LightPresetKeys => Presets.Keys.Where(k => !IsDarkPresetKey(k));
    public static IEnumerable<string> DarkPresetKeys => Presets.Keys.Where(IsDarkPresetKey);
    private static bool IsDarkPresetKey(string key) => key.StartsWith("dark-", StringComparison.Ordinal);

    /// <summary>付箋の背景色とアクセント色。未知のキーや壊れたカスタム色は黄色にする。</summary>
    public static (WpfColor Background, WpfColor Accent) ResolveColors(StickyNote note)
    {
        var fallback = Presets["yellow"];
        if (note.ColorKey == CustomColorKey)
            return (ParseOr(note.CustomBackgroundColor, fallback.Bg), ParseOr(note.CustomAccentColor, fallback.Header));
        if (!Presets.TryGetValue(note.ColorKey, out var preset) &&
            !LegacyPresets.TryGetValue(note.ColorKey, out preset))
            preset = fallback;
        return (Parse(preset.Bg), Parse(preset.Header));
    }

    /// <summary>"#RRGGBB" 形式に整える。色として読めなければ null。</summary>
    public static string? NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var c = (WpfColor)WpfColorConverter.ConvertFromString(value.Trim())!;
            return ToHex(c);
        }
        catch (FormatException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    private static WpfColor ParseOr(string? hex, string fallback)
        => NormalizeHex(hex) is { } normalized ? Parse(normalized) : Parse(fallback);

    private static WpfColor Parse(string hex) => (WpfColor)WpfColorConverter.ConvertFromString(hex)!;

    private readonly StickyNote _model;
    private readonly AppSettings _settings;
    private readonly bool _forceOpaque;
    private readonly bool _isHovered;

    public NoteAppearance(StickyNote model, AppSettings settings, bool forceOpaque = false, bool isHovered = false)
    {
        _model = model;
        _settings = settings;
        _forceOpaque = forceOpaque;
        _isHovered = isHovered;
        UpdateBrushes();
    }

    public WpfBrush BackgroundBrush { get; private set; } = WpfBrushes.Transparent;
    public WpfBrush HeaderBrush { get; private set; } = WpfBrushes.Transparent;
    public WpfBrush TitleBarBrush { get; private set; } = WpfBrushes.Transparent;
    public WpfBrush TitleBarForeground { get; private set; } = WpfBrushes.Transparent;
    public WpfBrush TextForeground { get; private set; } = WpfBrushes.Transparent;
    public WpfBrush NoteBorderBrush { get; private set; } = WpfBrushes.Transparent;

    private void UpdateBrushes()
    {
        var (background, header) = ResolveColors(_model);
        var dark = IsDark(background);
        var black = WpfColor.FromRgb(0, 0, 0);

        // カスタムはタイトルバーを背景色を少し濃くした色にする。プリセットは従来どおりアクセントを混ぜる。
        var titleBar = _model.ColorKey == CustomColorKey
            ? Blend(background, black, dark ? 0.30 : 0.10)
            : Blend(header, background, dark ? 0.45 : 0.90);
        var text = dark ? WpfColor.FromRgb(229, 231, 235) : WpfColor.FromRgb(17, 24, 39);
        var titleText = dark ? WpfColor.FromRgb(249, 250, 251) : Blend(header, black, 0.45);
        // 淡いアクセントを選ぶとタイトル文字が地に溶けるので、そのときは本文と同じ色にする。
        if (Math.Abs(Luma(titleText) - Luma(titleBar)) < 0.45)
            titleText = dark ? WpfColor.FromRgb(249, 250, 251) : text;

        BackgroundBrush = new WpfSolidBrush(WithOpacity(background));
        HeaderBrush = new WpfSolidBrush(WithOpacity(header));
        TitleBarBrush = new WpfSolidBrush(WithOpacity(titleBar));
        TitleBarForeground = new WpfSolidBrush(titleText);
        TextForeground = new WpfSolidBrush(text);
        UpdateNoteBorderBrush(header);
    }

    /// <summary>
    /// 外枠の色を設定から決める。「なし」は太さを0にせず透明で塗る。
    /// 太さを変えると畳んだときの高さ（FoldedHeight）まで動いてしまうため。
    /// </summary>
    private void UpdateNoteBorderBrush(WpfColor header)
    {
        var setting = _settings.NoteBorderColor;
        if (string.Equals(setting, AppSettings.NoteBorderNone, StringComparison.OrdinalIgnoreCase))
        {
            NoteBorderBrush = WpfBrushes.Transparent;
            return;
        }
        if (string.Equals(setting, AppSettings.NoteBorderNoteColor, StringComparison.OrdinalIgnoreCase))
        {
            NoteBorderBrush = new WpfSolidBrush(WithOpacity(header));
            return;
        }

        var hex = string.Equals(setting, AppSettings.NoteBorderGray, StringComparison.OrdinalIgnoreCase)
            ? AppSettings.DefaultNoteBorderHex
            : setting;
        try
        {
            NoteBorderBrush = new WpfSolidBrush((WpfColor)WpfColorConverter.ConvertFromString(hex)!);
        }
        catch (FormatException)
        {
            // Normalize() が弾いたはずの値。枠なしで消すと書き損じに気付けないのでグレーへ。
            NoteBorderBrush = new WpfSolidBrush(
                (WpfColor)WpfColorConverter.ConvertFromString(AppSettings.DefaultNoteBorderHex)!);
        }
    }

    private WpfColor WithOpacity(WpfColor color)
        => WpfColor.FromArgb(
            (byte)Math.Round(255 * GetEffectiveOpacity()),
            color.R,
            color.G,
            color.B);

    private double GetEffectiveOpacity()
    {
        if (_forceOpaque) return 1.0;
        var boost = _isHovered ? Math.Clamp(_settings.HoverOpacityBoostPercent, 0, 90) : 0;
        return Math.Min(100, Math.Clamp(_model.OpacityPercent, 10, 100) + boost) / 100.0;
    }

    private static WpfColor Blend(WpfColor from, WpfColor to, double amount)
    {
        byte Lerp(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return WpfColor.FromRgb(Lerp(from.R, to.R), Lerp(from.G, to.G), Lerp(from.B, to.B));
    }

    public static string ToHex(WpfColor c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static double Luma(WpfColor c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;

    private static bool IsDark(WpfColor background) => Luma(background) < 0.5;

    /// <summary>
    /// 地が暗い配色か。アプリのテーマではなく付箋の背景色そのもので決まる。
    /// </summary>
    public static bool UsesDarkColors(StickyNote note) => IsDark(ResolveColors(note).Background);

}
