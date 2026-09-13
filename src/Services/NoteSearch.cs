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

using System.Text.RegularExpressions;

namespace ScreenPinNotes.Services;

public enum NoteSearchMode { Plain, Wildcard, Regex }

public static class NoteSearch
{
    public static Func<string, bool> CreateMatcher(string query, NoteSearchMode mode)
    {
        if (mode == NoteSearchMode.Plain)
            return text => text.Contains(query, StringComparison.OrdinalIgnoreCase);
        var pattern = mode == NoteSearchMode.Wildcard
            ? Regex.Escape(query).Replace("\\*", "[\\s\\S]*").Replace("\\?", "[\\s\\S]")
            : query;
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        return regex.IsMatch;
    }
}
