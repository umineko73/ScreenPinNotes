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

namespace ScreenPinNotes.Services;

public static class MarkdownLinkEditor
{
    public sealed record Link(int Start, int Length, string Label, string Target);

    public static Link? FindAt(string text, int caret)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] != '[' || (i > 0 && text[i - 1] == '!')) continue;
            if (!MarkdownSyntax.TryGetMarkdownLink(text, i, out var label, out var target, out var length)) continue;
            if (caret >= i && caret <= i + length)
                return new Link(i, length, label, target);
            i += length - 1;
        }
        return null;
    }
}
