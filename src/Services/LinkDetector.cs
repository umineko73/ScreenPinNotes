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

using System.Text.RegularExpressions;

namespace ScreenStickyNotes.Services;

public static class LinkDetector
{
    // URL と Windows パスを検出
    private static readonly Regex Pattern = new(
        @"https?://[^\s<>""\[\]{}|\\^`]+|ftp://[^\s<>""\[\]{}|\\^`]+" +
        @"|[A-Za-z]:\\[^\s""<>|]+" +
        @"|\\\\[^\s""<>|]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public record Segment(string Text, bool IsLink);

    public static IReadOnlyList<Segment> Parse(string line)
    {
        var result = new List<Segment>();
        int pos = 0;
        foreach (Match m in Pattern.Matches(line))
        {
            if (m.Index > pos)
                result.Add(new Segment(line[pos..m.Index], false));
            result.Add(new Segment(m.Value, true));
            pos = m.Index + m.Length;
        }
        if (pos < line.Length)
            result.Add(new Segment(line[pos..], false));
        return result;
    }

    public static bool IsLink(string text)
        => !string.IsNullOrWhiteSpace(text) && Pattern.IsMatch(text.Trim());

    public static bool IsFolder(string text)
    {
        var t = text.Trim();
        return (t.Length >= 3 && char.IsLetter(t[0]) && t[1] == ':' && t[2] == '\\')
            || t.StartsWith("\\\\");
    }
}
