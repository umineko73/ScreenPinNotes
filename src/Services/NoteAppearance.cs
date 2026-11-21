// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3 of the License.
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

    /// <summary>
    /// 色のパレット。ライト11色とダーク11色を同じ色相の順（黄・橙・茶・赤・桃・紫・青・空・青緑・緑・灰）に
    /// 並べ、同じ位置の色をテーマ切り替えの対にする（<see cref="ThemeCounterpart"/>）。
    /// 設定で選んだパレットだけをピッカーに出すが、どのパレットの色も常に描けるよう、
    /// キーはパレットごとに別にする（<see cref="Presets"/> にすべて入る）。
    /// </summary>
    public sealed record Palette(string Id, string NameKey,
        IReadOnlyList<(string Key, string Bg, string Header)> Light,
        IReadOnlyList<(string Key, string Bg, string Header)> Dark);

    public const string PaletteClassic = "Classic";
    public const string PaletteModern = "Modern";
    public const string PaletteSoft = "Soft";

    public static IReadOnlyList<Palette> Palettes { get; } =
    [
        // 付箋: 色の付いた紙。既存ノートの互換のため yellow/blue/green/pink/purple/gray のキーは変更しない。
        new(PaletteClassic, "ColorPaletteClassic",
        [
            ("yellow", "#FFFDE7", "#F9A825"),
            ("orange", "#FFEDD5", "#C2410C"),
            ("brown",  "#EFEBE9", "#6D4C41"),
            ("red",    "#FEE2E2", "#B91C1C"),
            ("pink",   "#FCE7F3", "#BE185D"),
            ("purple", "#EEECFB", "#6D28D9"),
            ("blue",   "#DBEAFE", "#1D4ED8"),
            ("sky",    "#E0F2FE", "#0369A1"),
            ("teal",   "#D0F7EF", "#0F766E"),
            ("green",  "#DEFAE8", "#15803D"),
            ("gray",   "#F3F4F6", "#4B5563"),
        ],
        [
            ("dark-olive",    "#32321E", "#9C9A4E"),
            ("dark-rust",     "#3D2A1E", "#C07A48"),
            ("dark-coffee",   "#352D25", "#A0825F"),
            ("dark-wine",     "#3F252D", "#AC657B"),
            ("dark-plum",     "#35283F", "#946AAC"),
            ("dark-indigo",   "#262744", "#7275C4"),
            ("dark-navy",     "#1E293B", "#5277AC"),
            ("dark-sky",      "#183240", "#4B94B8"),
            ("dark-teal",     "#193631", "#43877B"),
            ("dark-forest",   "#1F3322", "#5E9A5A"),
            ("dark-charcoal", "#252A32", "#667085"),
        ]),
        // モダン: 白に近い地に、鮮やかな帯。地はごく薄く色を差すだけにして、色はほぼ帯で見分ける。
        new(PaletteModern, "ColorPaletteModern",
        [
            ("modern-yellow", "#FFFDF4", "#EAB308"),
            ("modern-orange", "#FFFAF5", "#F97316"),
            ("modern-brown",  "#FCFAF8", "#A16207"),
            ("modern-red",    "#FFF8F8", "#EF4444"),
            ("modern-pink",   "#FFF7FB", "#EC4899"),
            ("modern-purple", "#FAF8FF", "#8B5CF6"),
            ("modern-blue",   "#F7FAFF", "#3B82F6"),
            ("modern-sky",    "#F5FBFF", "#0EA5E9"),
            ("modern-teal",   "#F4FCFB", "#14B8A6"),
            ("modern-green",  "#F5FCF7", "#22C55E"),
            ("modern-gray",   "#FAFAFA", "#71717A"),
        ],
        [
            ("modern-dark-yellow", "#232220", "#FACC15"),
            ("modern-dark-orange", "#24211F", "#FB923C"),
            ("modern-dark-brown",  "#232120", "#D29A5C"),
            ("modern-dark-red",    "#241F20", "#F87171"),
            ("modern-dark-pink",   "#241F22", "#F472B6"),
            ("modern-dark-purple", "#211F25", "#A78BFA"),
            ("modern-dark-blue",   "#1F2125", "#60A5FA"),
            ("modern-dark-sky",    "#1E2225", "#38BDF8"),
            ("modern-dark-teal",   "#1E2322", "#2DD4BF"),
            ("modern-dark-green",  "#1F2320", "#4ADE80"),
            ("modern-dark-gray",   "#212124", "#A1A1AA"),
        ]),
        // ソフト: 紙のような生成りの地に、彩度を落としたくすみ色。
        new(PaletteSoft, "ColorPaletteSoft",
        [
            ("soft-yellow", "#FBF6E9", "#C29B2E"),
            ("soft-orange", "#FBEFE6", "#C2774A"),
            ("soft-brown",  "#F4EEE8", "#8C6E5D"),
            ("soft-red",    "#F8EAE8", "#B5615A"),
            ("soft-pink",   "#F8ECF0", "#B06C87"),
            ("soft-purple", "#F0ECF5", "#8672A6"),
            ("soft-blue",   "#E9EEF6", "#5A78A6"),
            ("soft-sky",    "#E8F2F5", "#4C8BA2"),
            ("soft-teal",   "#E6F2EF", "#4B897D"),
            ("soft-green",  "#EAF2E6", "#67895A"),
            ("soft-gray",   "#EFEFEC", "#77776F"),
        ],
        [
            ("soft-dark-yellow", "#2E2A22", "#BFA45A"),
            ("soft-dark-orange", "#2F2722", "#C28B66"),
            ("soft-dark-brown",  "#2B2622", "#A08A78"),
            ("soft-dark-red",    "#2F2424", "#BE7D78"),
            ("soft-dark-pink",   "#2E2429", "#BC8AA0"),
            ("soft-dark-purple", "#29252F", "#9D8DBA"),
            ("soft-dark-blue",   "#22272F", "#7F9BC2"),
            ("soft-dark-sky",    "#212A2E", "#76A9BC"),
            ("soft-dark-teal",   "#212B29", "#74A99D"),
            ("soft-dark-green",  "#252B22", "#8BAA7C"),
            ("soft-dark-gray",   "#282826", "#9A9A92"),
        ]),
    ];

    /// <summary>設定のパレット名から選ぶ。知らない名前は「付箋」。</summary>
    public static Palette PaletteFor(string? id)
        => Palettes.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Palettes[0];

    /// <summary>すべてのパレットの色。保存済みの付箋は、今どのパレットを選んでいても元の色で描く。</summary>
    public static IReadOnlyDictionary<string, (string Bg, string Header)> Presets { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, (string Bg, string Header)>(
            Palettes.SelectMany(p => p.Light.Concat(p.Dark))
                .ToDictionary(c => c.Key, c => (c.Bg, c.Header)));

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
        ["stone"]   = ("#F5F5F4", "#57534E"),
        ["slate"]   = ("#F1F5F9", "#334155"),
        ["white"]   = ("#FFFFFF", "#9CA3AF"),
        ["dark"]    = ("#E5E7EB", "#111827"),
    };

    /// <summary>パレットのライトの色（既定は「付箋」）。</summary>
    public static IEnumerable<string> LightPresetKeys(string? palette = null) => PaletteFor(palette).Light.Select(c => c.Key);
    /// <summary>パレットのダークの色（既定は「付箋」）。</summary>
    public static IEnumerable<string> DarkPresetKeys(string? palette = null) => PaletteFor(palette).Dark.Select(c => c.Key);

    /// <summary>色がどのパレットの、ダークの段か・何番目か。パレットに無い色は null。</summary>
    private static (Palette Palette, bool Dark, int Index)? Locate(string key)
    {
        foreach (var palette in Palettes)
        {
            for (var i = 0; i < palette.Light.Count; i++)
                if (palette.Light[i].Key == key) return (palette, false, i);
            for (var i = 0; i < palette.Dark.Count; i++)
                if (palette.Dark[i].Key == key) return (palette, true, i);
        }
        return null;
    }

    /// <summary>
    /// ライトとダークの対になる色。同じパレットの同じ列（ライトとダークの並びの同じ位置）の色で、
    /// カスタム・旧パレットの色・未知のキーは対がないので null。
    /// </summary>
    public static string? ThemeCounterpart(string key)
        => Locate(key) is { } at ? (at.Dark ? at.Palette.Light : at.Palette.Dark)[at.Index].Key : null;

    /// <summary>
    /// アプリのテーマを切り替えたときの色。ダークへ切り替えるとライトの色は対のダークに、
    /// ライトへ切り替えるとダークの色は対のライトになる。それ以外（すでにテーマ側の色、
    /// カスタム、対のない色）はそのまま。
    /// </summary>
    public static string ColorKeyForTheme(string key, bool darkTheme)
        => Locate(key) is { } at && at.Dark != darkTheme && ThemeCounterpart(key) is { } counterpart ? counterpart : key;

    /// <summary>
    /// パレットを切り替えたときの色。別のパレットの色なら、切り替え先の同じ段・同じ列の色
    /// （同じ色相）にする。カスタムや旧パレットの色はそのまま。
    /// </summary>
    public static string ColorKeyForPalette(string key, string? palette)
    {
        if (Locate(key) is not { } at) return key;
        var target = PaletteFor(palette);
        return (at.Dark ? target.Dark : target.Light)[at.Index].Key;
    }

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

    /// <summary>
    /// 色見本の塗り。左に帯（アクセント）、残りを地の色にして、付箋の見た目をそのまま縮めて見せる。
    /// 地がほぼ白いモダンのパレットでも、帯の色で見分けが付く。
    /// </summary>
    public static WpfBrush SwatchBrush(WpfColor background, WpfColor accent)
    {
        var brush = new System.Windows.Media.LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0.5),
            EndPoint = new System.Windows.Point(1, 0.5),
        };
        brush.GradientStops.Add(new System.Windows.Media.GradientStop(accent, 0));
        brush.GradientStops.Add(new System.Windows.Media.GradientStop(accent, SwatchSpineRatio));
        brush.GradientStops.Add(new System.Windows.Media.GradientStop(background, SwatchSpineRatio));
        brush.GradientStops.Add(new System.Windows.Media.GradientStop(background, 1));
        brush.Freeze();
        return brush;
    }

    /// <summary>色見本の幅のうち、帯にあてる割合。</summary>
    public const double SwatchSpineRatio = 0.2;

    public static string ToHex(WpfColor c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static double Luma(WpfColor c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;

    private static bool IsDark(WpfColor background) => Luma(background) < 0.5;

    /// <summary>
    /// 地が暗い配色か。アプリのテーマではなく付箋の背景色そのもので決まる。
    /// </summary>
    public static bool UsesDarkColors(StickyNote note) => IsDark(ResolveColors(note).Background);

}
