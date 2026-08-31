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

using System.IO;
using System.Text.RegularExpressions;

namespace ScreenPinNotes.Services;

public static class LinkDetector
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff",
    };

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

    public static bool IsExactLink(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return false;

        var match = Pattern.Match(trimmed);
        return match.Success && match.Index == 0 && match.Length == trimmed.Length ||
               IsExactWindowsPath(trimmed);
    }

    public static bool IsImageTarget(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return false;

        var extension = GetTargetExtension(trimmed);
        return extension.Length > 0 && ImageExtensions.Contains(extension);
    }

    public static bool IsRenderableImageTarget(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return false;

        var extension = GetTargetExtension(trimmed);
        if (extension.Length == 0)
            extension = Path.GetExtension(trimmed.TrimEnd('\\', '/'));

        return extension.Length > 0 && ImageExtensions.Contains(extension);
    }

    private static string GetTargetExtension(string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp ||
             uri.Scheme == Uri.UriSchemeHttps ||
             uri.Scheme == Uri.UriSchemeFtp ||
             uri.IsFile))
        {
            return Path.GetExtension(uri.IsFile ? uri.LocalPath : uri.AbsolutePath);
        }

        return IsExactWindowsPath(target)
            ? Path.GetExtension(target.TrimEnd('\\', '/'))
            : "";
    }

    public static bool IsFolder(string text)
    {
        var t = text.Trim();
        return (t.Length >= 3 && char.IsLetter(t[0]) && t[1] == ':' && t[2] == '\\')
            || t.StartsWith("\\\\");
    }

    private static bool IsExactWindowsPath(string text)
        => ((text.Length >= 3 && char.IsLetter(text[0]) && text[1] == ':' && text[2] == '\\') ||
            text.StartsWith("\\\\", StringComparison.Ordinal)) &&
           text.IndexOfAny(['\r', '\n', '<', '>', '|', '"']) < 0;
}
