// ScreenStickyNotes - a desktop sticky notes app for Windows 11
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

using System.Text.Json.Serialization;

namespace ScreenStickyNotes.Models;

public class StickyNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonIgnore] // content.md に別途保存
    public string Content { get; set; } = "";
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 260;

    // 折りたたみ時専用の幅。null なら Width をそのまま使う
    // （＝まだ折りたたみ時に個別リサイズされたことがない付箋）。
    public double? FoldedWidth { get; set; }

    public double Height { get; set; } = 220;
    public string ColorKey { get; set; } = "yellow";
    public string Icon { get; set; } = "";   // タイトルバーに表示する絵文字。空 = なし
    public string FontFamily { get; set; } = "Yu Gothic UI";
    public double FontSize { get; set; } = 13;
    public double TitleFontSize { get; set; } = 12;
    public bool IsTopmost { get; set; } = false;
    public bool IsFolded { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
