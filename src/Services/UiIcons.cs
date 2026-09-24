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

    public const string Add = "\uE710";
    public const string Pin = "\uE718";
    public const string ChevronUp = "\uE70E";
    public const string ChevronDown = "\uE70D";
    public const string Lock = "\uE72E";
    public const string Clock = "\uE121";
    public const string Link = "\uE71B";
    /// <summary>下線へ向かう矢印。末尾へ追従している（tail 表示）ことの目印。</summary>
    public const string ToEnd = "\uE896";
    public const string Move = "\uE7C2";
    public const string Undo = "\uE7A7";
    public const string Redo = "\uE7A6";
    public const string Palette = "\uE790";
    /// <summary>笑顔。付箋のアイコンを選ぶボタン。</summary>
    public const string Emoji = "\uE76E";
    public const string CheckMark = "\uE73E";
    public const string Cut = "\uE8C6";
    public const string Copy = "\uE8C8";
    public const string Paste = "\uE77F";
    public const string Cancel = "\uE711";
}
