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

namespace ScreenStickyNotes.Services;

// Excel などからコピーしたタブ区切りテキストと Markdown テーブルの相互変換。
// クリップボード I/O は呼び出し側（StickyNoteWindow）が担当し、ここでは
// 文字列変換のみを行う。
public static class MarkdownTableClipboard
{
    public static bool TryTabularTextToMarkdownTable(
        string text,
        bool useFirstRowAsHeader,
        out string markdownTable)
    {
        markdownTable = "";
        var rows = ParseTabularText(text);
        if (rows.Count == 0 || rows.All(row => row.Count <= 1))
            return false;

        var columnCount = rows.Max(row => row.Count);
        if (columnCount < 2)
            return false;

        foreach (var row in rows)
        {
            while (row.Count < columnCount)
                row.Add("");
        }

        var lines = new List<string>
        {
            BuildMarkdownTableRow(useFirstRowAsHeader ? rows[0] : Enumerable.Repeat("", columnCount)),
            BuildMarkdownTableRow(Enumerable.Repeat("---", columnCount)),
        };
        lines.AddRange((useFirstRowAsHeader ? rows.Skip(1) : rows).Select(BuildMarkdownTableRow));

        markdownTable = string.Join('\n', lines);
        return true;
    }

    public static List<List<string>> ParseTabularText(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n');
        var rows = new List<List<string>>();
        foreach (var line in normalized.Split('\n'))
        {
            var cells = line.Split('\t')
                .Select(cell => cell.Trim().Replace("\n", "<br>"))
                .ToList();
            if (cells.Count > 0 && cells.Any(cell => cell.Length > 0))
                rows.Add(cells);
        }
        return rows;
    }

    public static string BuildMarkdownTableRow(IEnumerable<string> cells)
        => "| " + string.Join(" | ", cells.Select(EscapeMarkdownTableCell)) + " |";

    public static string EscapeMarkdownTableCell(string text)
        => text.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\r", "<br>").Replace("\n", "<br>");

    public static bool TryMarkdownTableToTabularText(string text, out string tabularText)
    {
        tabularText = "";
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        if (lines.Count < 2 ||
            !TrySplitMarkdownTableRow(lines[0], out var headers) ||
            !TrySplitMarkdownTableRow(lines[1], out var separator) ||
            !IsMarkdownTableSeparator(separator))
        {
            return false;
        }

        var rows = headers.All(cell => string.IsNullOrWhiteSpace(cell))
            ? new List<List<string>>()
            : new List<List<string>> { headers };
        foreach (var line in lines.Skip(2))
        {
            if (!TrySplitMarkdownTableRow(line, out var cells) || cells.Count != headers.Count)
                break;
            rows.Add(cells);
        }

        tabularText = string.Join("\r\n", rows.Select(row => string.Join("\t", row.Select(UnescapeMarkdownTableCell))));
        return true;
    }

    public static bool TryCopyableTableTextToTabularText(string text, out string tabularText)
    {
        if (TryMarkdownTableToTabularText(text, out tabularText))
            return true;

        var rows = ParseTabularText(text);
        if (rows.Count == 0 || rows.All(row => row.Count <= 1))
        {
            tabularText = "";
            return false;
        }

        tabularText = string.Join("\r\n", rows.Select(row => string.Join("\t", row)));
        return true;
    }

    public static bool TrySplitMarkdownTableRow(string line, out List<string> cells)
    {
        cells = [];
        var trimmed = line.Trim();
        if (!trimmed.Contains('|'))
            return false;

        if (trimmed.StartsWith('|'))
            trimmed = trimmed[1..];
        if (trimmed.EndsWith('|'))
            trimmed = trimmed[..^1];

        cells = SplitUnescapedPipes(trimmed).Select(cell => cell.Trim()).ToList();
        return cells.Count >= 2;
    }

    public static List<string> SplitUnescapedPipes(string text)
    {
        var cells = new List<string>();
        var start = 0;
        var escaped = false;

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == '|')
            {
                cells.Add(text[start..i]);
                start = i + 1;
            }
        }

        cells.Add(text[start..]);
        return cells;
    }

    public static bool IsMarkdownTableSeparator(IReadOnlyList<string> cells)
        => cells.Count >= 2 && cells.All(IsMarkdownTableSeparatorCell);

    public static bool IsMarkdownTableSeparatorCell(string cell)
    {
        var trimmed = cell.Trim();
        if (trimmed.StartsWith(":"))
            trimmed = trimmed[1..];
        if (trimmed.EndsWith(":"))
            trimmed = trimmed[..^1];

        return trimmed.Length >= 3 && trimmed.All(c => c == '-');
    }

    public static string UnescapeMarkdownTableCell(string text)
        => text.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("\\|", "|")
            .Replace("\\\\", "\\");
}
