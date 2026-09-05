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

using System.Globalization;
using ScreenPinNotes.Models;

namespace ScreenPinNotes.Services;

/// <summary>
/// 新しい付箋の初期値を組み立てる。ウィンドウを作る前の純粋な計算だけを置く。
/// </summary>
public static class NewNoteFactory
{
    /// <param name="template">
    /// 引き継ぎ元。既存の付箋の「＋」から増やしたときに渡す。
    /// 見た目はこちらが設定の既定値より優先される。
    /// </param>
    public static StickyNote Create(
        AppSettings settings, StickyNote? template, double x, double y, DateTime now)
    {
        var layout = settings.Layout;
        var defaults = settings.NoteDefaults;
        var note = new StickyNote
        {
            X = x,
            Y = y,
            Width = layout.DefaultNoteWidth,
            Height = layout.DefaultNoteHeight,
            Title = now.ToString("yyyy/MM/dd(ddd) HH:mm:ss", CultureInfo.GetCultureInfo(settings.Language)),
            CreatedAt = now,
            UpdatedAt = now,
            ColorKey = defaults.ColorKey,
            Icon = defaults.Icon,
            FontFamily = defaults.FontFamily,
            FontSize = defaults.FontSize,
            IsTitleBarHidden = defaults.TitleBarHidden,
        };

        if (template != null)
        {
            note.ColorKey = template.ColorKey;
            note.Icon = template.Icon;
            note.FontFamily = template.FontFamily;
            note.FontSize = template.FontSize;
            note.TitleFontSize = template.TitleFontSize;
            note.OpacityPercent = template.OpacityPercent;
            note.IsTitleBarHidden = template.IsTitleBarHidden;
        }

        return note;
    }
}
