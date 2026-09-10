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
    // 背景（淡色）とヘッダー（濃色）の組。暖色→寒色→無彩色の順に並べる。
    // 既存ノートの互換のため yellow/blue/green/pink/purple/gray のキーは変更しない。
    public static IReadOnlyDictionary<string, (string Bg, string Header)> Presets { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, (string Bg, string Header)>(
        new Dictionary<string, (string Bg, string Header)>()
    {
        // 暖色
        ["yellow"]  = ("#FFFDE7", "#F9A825"),
        ["amber"]   = ("#FEF3C7", "#B45309"),
        ["orange"]  = ("#FFEDD5", "#C2410C"),
        ["red"]     = ("#FEE2E2", "#B91C1C"),
        ["rose"]    = ("#FFE4E6", "#BE123C"),
        ["pink"]    = ("#FCE7F3", "#BE185D"),
        // 紫〜青
        ["fuchsia"] = ("#FAE8FF", "#A21CAF"),
        ["purple"]  = ("#EDE9FE", "#6D28D9"),
        ["violet"]  = ("#DDD6FE", "#5B21B6"),
        ["indigo"]  = ("#E0E7FF", "#4338CA"),
        ["blue"]    = ("#DBEAFE", "#1D4ED8"),
        ["sky"]     = ("#E0F2FE", "#0369A1"),
        // 寒色〜緑
        ["cyan"]    = ("#CFFAFE", "#0E7490"),
        ["teal"]    = ("#CCFBF1", "#0F766E"),
        ["emerald"] = ("#D1FAE5", "#047857"),
        ["green"]   = ("#DCFCE7", "#15803D"),
        ["lime"]    = ("#ECFCCB", "#4D7C0F"),
        ["olive"]   = ("#F7F7DC", "#827717"),
        // 無彩色・その他
        ["brown"]   = ("#EFEBE9", "#6D4C41"),
        ["stone"]   = ("#F5F5F4", "#57534E"),
        ["gray"]    = ("#F3F4F6", "#4B5563"),
        ["slate"]   = ("#F1F5F9", "#334155"),
        ["white"]   = ("#FFFFFF", "#9CA3AF"),
        ["dark"]    = ("#E5E7EB", "#111827"),
        ["dark-charcoal"] = ("#252A32", "#667085"),
        ["dark-navy"] = ("#1E293B", "#5277AC"),
        ["dark-teal"] = ("#193631", "#43877B"),
        ["dark-plum"] = ("#35283F", "#946AAC"),
        ["dark-wine"] = ("#3F252D", "#AC657B"),
        ["dark-coffee"] = ("#352D25", "#A0825F"),
    });

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
        if (!Presets.TryGetValue(_model.ColorKey, out var preset))
            preset = Presets["yellow"];

        var background = (WpfColor)WpfColorConverter.ConvertFromString(preset.Bg);
        var header = (WpfColor)WpfColorConverter.ConvertFromString(preset.Header);

        if (UsesDarkColors(_model, _settings))
        {
            var darkBase = WpfColor.FromRgb(17, 24, 39);
            var explicitDark = _model.ColorKey.StartsWith("dark-", StringComparison.Ordinal);
            var darkPanel = explicitDark ? background : Blend(header, darkBase, 0.86);
            var darkHeader = explicitDark ? header : Blend(header, WpfColor.FromRgb(0, 0, 0), 0.25);

            BackgroundBrush = new WpfSolidBrush(WithOpacity(darkPanel));
            HeaderBrush = new WpfSolidBrush(WithOpacity(darkHeader));
            TitleBarBrush = new WpfSolidBrush(WithOpacity(Blend(darkHeader, darkPanel, 0.45)));
            TitleBarForeground = new WpfSolidBrush(WpfColor.FromRgb(249, 250, 251));
            TextForeground = new WpfSolidBrush(WpfColor.FromRgb(229, 231, 235));
            UpdateNoteBorderBrush(darkHeader);
            return;
        }

        BackgroundBrush = new WpfSolidBrush(WithOpacity(background));
        HeaderBrush = new WpfSolidBrush(WithOpacity(header));
        TitleBarBrush = new WpfSolidBrush(WithOpacity(Blend(header, background, 0.90)));
        TitleBarForeground = new WpfSolidBrush(Blend(header, WpfColor.FromRgb(0, 0, 0), 0.45));
        TextForeground = new WpfSolidBrush(WpfColor.FromRgb(17, 24, 39));
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

    public static bool UsesDarkColors(StickyNote note, AppSettings settings) =>
        string.Equals(settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase) ||
        note.ColorKey.StartsWith("dark-", StringComparison.Ordinal);

}
