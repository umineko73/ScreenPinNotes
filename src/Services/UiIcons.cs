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

using WpfFontFamily = System.Windows.Media.FontFamily;

namespace ScreenPinNotes.Services;

/// <summary>
/// ボタンや状態表示に使う線画アイコン（Segoe Fluent Icons の字）。
/// 絵文字は色付き・塗りつぶしが混ざって揃わないので、操作の目印はここの字に統一する。
/// 付箋そのもののアイコン（利用者が選ぶ絵文字）はこの対象ではない。
/// Windows 10 には Segoe Fluent Icons が無いので、同じ番号の字を持つ
/// Segoe MDL2 Assets に落とす。
/// </summary>
public static class UiIcons
{
    public static readonly WpfFontFamily Font = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public const string Add = "";
    public const string Pin = "";
    public const string ChevronUp = "";
    public const string ChevronDown = "";
    public const string Lock = "";
    public const string Clock = "";
    public const string Link = "";
    /// <summary>下線へ向かう矢印。末尾へ追従している（tail 表示）ことの目印。</summary>
    public const string ToEnd = "";
    public const string Move = "";
    public const string Undo = "";
    public const string Redo = "";
    public const string Palette = "";
    /// <summary>笑顔。付箋のアイコンを選ぶボタン。</summary>
    public const string Emoji = "";
    public const string CheckMark = "";
    public const string Cancel = "";
}
