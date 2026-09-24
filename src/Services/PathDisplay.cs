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

using System.Globalization;

namespace ScreenPinNotes.Services;

public static class PathDisplay
{
    public static string Fit(string path, double width, Func<string, double> measure)
    {
        if (width <= 0) return "";
        if (measure(path) <= width) return path;
        const string prefix = "…/";
        // Prefer complete trailing directory names before shortening the filename itself.
        for (var i = 0; i < path.Length - 1; i++)
            if (path[i] is '/' or '\\')
            {
                var candidate = prefix + path[(i + 1)..];
                if (measure(candidate) <= width) return candidate;
            }
        var name = path[(Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) + 1)..];
        foreach (var index in StringInfo.ParseCombiningCharacters(name))
        {
            var candidate = "…" + name[index..];
            if (measure(candidate) <= width) return candidate;
        }
        return measure("…") <= width ? "…" : "";
    }
}
