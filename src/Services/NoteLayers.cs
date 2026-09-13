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

namespace ScreenPinNotes.Services;

public enum LayerMove { Top, Up, Down, Bottom }

public static class NoteLayers
{
    // Windows keeps always-on-top windows in a separate band.
    public static List<StickyNote> Ordered(IEnumerable<StickyNote> notes) => notes
        .OrderByDescending(n => n.IsTopmost).ThenBy(n => n.LayerOrder)
        .ThenByDescending(n => n.CreatedAt).ThenBy(n => n.Id, StringComparer.Ordinal).ToList();

    public static void Move(IEnumerable<StickyNote> notes, ISet<string> selected, LayerMove move)
    {
        var ordered = Ordered(notes);
        foreach (var band in ordered.GroupBy(n => n.IsTopmost))
        {
            var rows = band.ToList();
            bool Chosen(StickyNote n) => selected.Contains(n.Id);
            if (move == LayerMove.Top) rows = rows.Where(Chosen).Concat(rows.Where(n => !Chosen(n))).ToList();
            else if (move == LayerMove.Bottom) rows = rows.Where(n => !Chosen(n)).Concat(rows.Where(Chosen)).ToList();
            else if (move == LayerMove.Up)
            {
                for (int i = 1; i < rows.Count; i++)
                    if (Chosen(rows[i]) && !Chosen(rows[i - 1])) (rows[i - 1], rows[i]) = (rows[i], rows[i - 1]);
            }
            else
            {
                for (int i = rows.Count - 2; i >= 0; i--)
                    if (Chosen(rows[i]) && !Chosen(rows[i + 1])) (rows[i], rows[i + 1]) = (rows[i + 1], rows[i]);
            }
            for (int i = 0; i < rows.Count; i++) rows[i].LayerOrder = i;
        }
    }
}
