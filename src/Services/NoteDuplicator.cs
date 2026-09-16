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

using System.Text.Json;
using ScreenPinNotes.Models;

namespace ScreenPinNotes.Services;

/// <summary>
/// 付箋の複製を作る。本文・見た目・折りたたみ・ロック・外部ファイルとの連動は
/// そのまま引き継ぎ、複製ならではの項目だけを改める。
/// </summary>
public static class NoteDuplicator
{
    /// <param name="offset">元の付箋からずらす量（論理ピクセル）。重なって見分けが付かなくならないように。</param>
    /// <param name="layerOrder">複製を置く重なり順。</param>
    public static StickyNote Create(StickyNote source, double offset, int layerOrder, DateTime now)
    {
        // 項目を1つずつ写すと、StickyNote に項目を足したときに写し忘れる。
        // 保存と同じ JSON を通して丸ごと写し、本文（保存では別ファイル）だけ足す。
        var copy = JsonSerializer.Deserialize<StickyNote>(JsonSerializer.Serialize(source))!;
        copy.Content = source.Content;

        copy.Id = Guid.NewGuid().ToString();
        copy.X += offset;
        copy.Y += offset;
        if (copy.FoldedX is { } foldedX) copy.FoldedX = foldedX + offset;
        if (copy.FoldedY is { } foldedY) copy.FoldedY = foldedY + offset;
        copy.LayerOrder = layerOrder;
        // 複製した付箋は目の前に出す。元が非表示でも（一覧から複製した場合など）同じ。
        copy.IsHidden = false;
        // リマインダーまで写すと、同じ予定の通知が2回届く。
        copy.Reminder = null;
        copy.CreatedAt = now;
        copy.UpdatedAt = now;
        return copy;
    }
}
