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

using System.Windows;
using System.Windows.Documents;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfSolidBrush = System.Windows.Media.SolidColorBrush;

namespace ScreenPinNotes.Services;

public static class MarkdownRenderer
{
    private static readonly WpfFontFamily CodeFontFamily = new("Consolas");

    public sealed record MarkdownImage(
        string Alt,
        string Target,
        int LineIndex,
        int Start,
        int Length,
        double? Width,
        double? Height);

    public static IEnumerable<Block> Render(
        string text,
        double baseFontSize,
        Func<string, string, Hyperlink> createHyperlink,
        Func<MarkdownImage, Inline>? createImage = null,
        Func<int, bool, WpfCheckBox>? createTaskCheckbox = null,
        bool darkMode = false)
    {
        // Bound UI element creation and parser work without discarding source text.
        if (text.Length > 131072 || text.Count(ch => ch == '\n') > 2000)
        {
            yield return new Paragraph(new Run(text));
            yield break;
        }
        var lines = NormalizeLines(text);
        if (lines.Length == 0)
        {
            yield return CreateParagraph();
            yield break;
        }

        for (int i = 0; i < lines.Length;)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (IsFence(line))
            {
                i++;
                var codeLines = new List<string>();
                while (i < lines.Length && !IsFence(lines[i]))
                    codeLines.Add(lines[i++]);
                if (i < lines.Length) i++;

                yield return CreateCodeBlock(string.Join("\n", codeLines), darkMode);
                continue;
            }

            if (trimmed.Length == 0)
            {
                yield return CreateParagraph();
                i++;
                continue;
            }

            if (TryParseTable(lines, i, createHyperlink, createImage, darkMode, out var table, out var nextIndex))
            {
                yield return table;
                i = nextIndex;
                continue;
            }

            if (TryGetHeading(line, out var level, out var headingText))
            {
                var para = CreateParagraph();
                para.FontWeight = FontWeights.Bold;
                para.FontSize = Math.Max(baseFontSize, baseFontSize + 9 - level);
                AddInlineContent(para.Inlines, headingText, i, level + 1, createHyperlink, createImage, darkMode);
                yield return para;
                i++;
                continue;
            }

            if (IsHorizontalRule(trimmed))
            {
                yield return CreateHorizontalRule(darkMode);
                i++;
                continue;
            }

            if (TryGetListItem(line, out var ordered, out _, out var firstTaskState))
            {
                var list = new System.Windows.Documents.List
                {
                    MarkerStyle = firstTaskState.HasValue
                        ? TextMarkerStyle.None
                        : ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Margin = new Thickness(18, 0, 0, 0),
                    Padding = new Thickness(0),
                };

                while (i < lines.Length &&
                       TryGetListItem(lines[i], out var itemOrdered, out var itemText, out var taskState) &&
                       itemOrdered == ordered &&
                       taskState.HasValue == firstTaskState.HasValue)
                {
                    var para = CreateParagraph();
                    if (taskState.HasValue)
                    {
                        para.Inlines.Add(new InlineUIContainer(
                            createTaskCheckbox?.Invoke(i, taskState.Value) ??
                            new WpfCheckBox { IsChecked = taskState.Value })
                        {
                            BaselineAlignment = BaselineAlignment.Center,
                        });
                        para.Inlines.Add(new Run(" "));
                    }
                    var itemTextOffset = lines[i].IndexOf(itemText, StringComparison.Ordinal);
                    AddInlineContent(para.Inlines, itemText, i, itemTextOffset < 0 ? 0 : itemTextOffset, createHyperlink, createImage, darkMode);
                    list.ListItems.Add(new ListItem(para) { Margin = new Thickness(0) });
                    i++;
                }

                yield return list;
                continue;
            }

            if (TryGetQuote(line, out var quoteText))
            {
                var para = CreateParagraph();
                para.Margin = new Thickness(0, 2, 0, 2);
                para.Padding = new Thickness(8, 0, 0, 0);
                para.BorderBrush = GetBorderBrush(darkMode);
                para.BorderThickness = new Thickness(3, 0, 0, 0);
                para.Foreground = darkMode ? WpfBrushes.LightGray : WpfBrushes.DimGray;
                var quoteTextOffset = line.IndexOf(quoteText, StringComparison.Ordinal);
                AddInlineContent(para.Inlines, quoteText, i, quoteTextOffset < 0 ? 0 : quoteTextOffset, createHyperlink, createImage, darkMode);
                yield return para;
                i++;
                continue;
            }

            var paragraph = CreateParagraph();
            AddInlineContent(paragraph.Inlines, line, i, 0, createHyperlink, createImage, darkMode);
            yield return paragraph;
            i++;
        }
    }

    private static string[] NormalizeLines(string text)
        => string.IsNullOrEmpty(text)
            ? []
            : text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

    private static Paragraph CreateParagraph()
        => new() { Margin = new Thickness(0) };

    private static Paragraph CreateCodeBlock(string text, bool darkMode)
    {
        var para = new Paragraph
        {
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(6, 3, 6, 3),
            FontFamily = CodeFontFamily,
            Background = GetCodeBackground(darkMode),
        };

        var lines = NormalizeLines(text);
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) para.Inlines.Add(new LineBreak());
            para.Inlines.Add(new Run(lines[i]));
        }
        return para;
    }

    private static Paragraph CreateHorizontalRule(bool darkMode)
    {
        var para = CreateParagraph();
        para.Margin = new Thickness(0, 4, 0, 4);
        para.BorderBrush = GetBorderBrush(darkMode);
        para.BorderThickness = new Thickness(0, 1, 0, 0);
        para.Inlines.Add(new Run(" "));
        return para;
    }

    private static WpfSolidBrush GetCodeBackground(bool darkMode)
        => darkMode
            ? new WpfSolidBrush(WpfColor.FromArgb(34, 255, 255, 255))
            : new WpfSolidBrush(WpfColor.FromArgb(24, 0, 0, 0));

    private static WpfSolidBrush GetBorderBrush(bool darkMode)
        => darkMode
            ? new WpfSolidBrush(WpfColor.FromArgb(95, 255, 255, 255))
            : new WpfSolidBrush(WpfColor.FromArgb(80, 0, 0, 0));

    private static WpfSolidBrush GetTableHeaderBackground(bool darkMode)
        => darkMode
            ? new WpfSolidBrush(WpfColor.FromArgb(26, 255, 255, 255))
            : new WpfSolidBrush(WpfColor.FromArgb(18, 0, 0, 0));

    private static bool TryParseTable(
        string[] lines,
        int start,
        Func<string, string, Hyperlink> createHyperlink,
        Func<MarkdownImage, Inline>? createImage,
        bool darkMode,
        out Table table,
        out int nextIndex)
    {
        table = new Table();
        nextIndex = start;

        if (start + 1 >= lines.Length ||
            !TrySplitTableRow(lines[start], out var headers) ||
            !TrySplitTableRow(lines[start + 1], out var separatorCells) ||
            !TryGetTableAlignments(separatorCells, out var alignments))
        {
            return false;
        }

        int columnCount = headers.Count;
        if (columnCount == 0 || separatorCells.Count != columnCount)
            return false;

        table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 3, 0, 3),
        };

        for (int i = 0; i < columnCount; i++)
            table.Columns.Add(new TableColumn());

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        var headerRow = new TableRow();
        group.Rows.Add(headerRow);
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            headerRow.Cells.Add(CreateTableCell(headers[columnIndex], createHyperlink, createImage, alignments[columnIndex], isHeader: true, darkMode));

        int rowIndex = start + 2;
        while (rowIndex < lines.Length && TrySplitTableRow(lines[rowIndex], out var cells))
        {
            if (cells.Count != columnCount)
                break;

            var row = new TableRow();
            group.Rows.Add(row);
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
                row.Cells.Add(CreateTableCell(cells[columnIndex], createHyperlink, createImage, alignments[columnIndex], isHeader: false, darkMode));

            rowIndex++;
        }

        nextIndex = rowIndex;
        return true;
    }

    private static TableCell CreateTableCell(
        string text,
        Func<string, string, Hyperlink> createHyperlink,
        Func<MarkdownImage, Inline>? createImage,
        TextAlignment textAlignment,
        bool isHeader,
        bool darkMode)
    {
        var paragraph = CreateParagraph();
        paragraph.TextAlignment = textAlignment;
        AddInlineContent(paragraph.Inlines, text.Trim(), -1, 0, createHyperlink, createImage, darkMode);
        if (isHeader)
            paragraph.FontWeight = FontWeights.Bold;

        return new TableCell(paragraph)
        {
            BorderBrush = GetBorderBrush(darkMode),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5, 2, 5, 2),
            Background = isHeader ? GetTableHeaderBackground(darkMode) : null,
        };
    }

    private static bool TrySplitTableRow(string line, out List<string> cells)
    {
        cells = [];
        var trimmed = line.Trim();
        if (!trimmed.Contains('|'))
            return false;

        if (trimmed.StartsWith('|'))
            trimmed = trimmed[1..];
        if (trimmed.EndsWith('|'))
            trimmed = trimmed[..^1];

        cells = SplitUnescapedPipes(trimmed);
        return cells.Count >= 2;
    }

    private static List<string> SplitUnescapedPipes(string text)
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
                cells.Add(UnescapeTableCell(text[start..i].Trim()));
                start = i + 1;
            }
        }

        cells.Add(UnescapeTableCell(text[start..].Trim()));
        return cells;
    }

    private static string UnescapeTableCell(string text)
        => text.Replace("\\|", "|");

    private static bool TryGetTableAlignments(IReadOnlyList<string> cells, out List<TextAlignment> alignments)
    {
        alignments = [];
        if (cells.Count < 2)
            return false;

        foreach (var cell in cells)
        {
            if (!TryGetTableAlignment(cell, out var alignment))
                return false;

            alignments.Add(alignment);
        }

        return true;
    }

    private static bool TryGetTableAlignment(string cell, out TextAlignment alignment)
    {
        alignment = TextAlignment.Left;
        var trimmed = cell.Trim();
        var leftAligned = trimmed.StartsWith(":", StringComparison.Ordinal);
        var rightAligned = trimmed.EndsWith(":", StringComparison.Ordinal);
        if (leftAligned)
            trimmed = trimmed[1..];
        if (rightAligned)
            trimmed = trimmed[..^1];

        if (trimmed.Length < 3 || !trimmed.All(c => c == '-'))
            return false;

        alignment = (leftAligned, rightAligned) switch
        {
            (true, true) => TextAlignment.Center,
            (false, true) => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
        return true;
    }

    private static bool IsFence(string line)
        => line.TrimStart().StartsWith("```", StringComparison.Ordinal);

    private static bool IsHorizontalRule(string trimmed)
        => trimmed.Length >= 3 &&
           (trimmed.All(c => c == '-') ||
            trimmed.All(c => c == '*') ||
            trimmed.All(c => c == '_'));

    private static bool TryGetHeading(string line, out int level, out string text)
    {
        level = 0;
        text = "";

        while (level < line.Length && level < 6 && line[level] == '#')
            level++;

        if (level == 0 || level >= line.Length || line[level] != ' ')
            return false;

        text = line[(level + 1)..].Trim();
        return true;
    }

    private static bool TryGetQuote(string line, out string text)
    {
        var trimmedStart = line.TrimStart();
        if (!trimmedStart.StartsWith(">"))
        {
            text = "";
            return false;
        }

        text = trimmedStart[1..].TrimStart();
        return true;
    }

    private static bool TryGetListItem(
        string line,
        out bool ordered,
        out string text,
        out bool? taskState)
    {
        ordered = false;
        text = "";
        taskState = null;

        var trimmed = line.TrimStart();
        if (trimmed.Length >= 2 && (trimmed[0] == '-' || trimmed[0] == '*' || trimmed[0] == '+') && trimmed[1] == ' ')
        {
            text = trimmed[2..];
            ReadTaskState(ref text, out taskState);
            return true;
        }

        int pos = 0;
        while (pos < trimmed.Length && char.IsDigit(trimmed[pos]))
            pos++;

        if (pos == 0 || pos + 1 >= trimmed.Length || trimmed[pos] != '.' || trimmed[pos + 1] != ' ')
            return false;

        ordered = true;
        text = trimmed[(pos + 2)..];
        ReadTaskState(ref text, out taskState);
        return true;
    }

    private static void ReadTaskState(ref string text, out bool? taskState)
    {
        taskState = null;
        if (text.Length < 4 || text[0] != '[' || text[2] != ']' || text[3] != ' ')
            return;

        if (text[1] is ' ' or 'x' or 'X')
        {
            taskState = text[1] is 'x' or 'X';
            text = text[4..];
        }
    }

    private static void AddInlineContent(
        InlineCollection inlines,
        string text,
        int lineIndex,
        int lineOffset,
        Func<string, string, Hyperlink> createHyperlink,
        Func<MarkdownImage, Inline>? createImage,
        bool darkMode, int depth = 0)
    {
        foreach (var inline in ParseInline(text, lineIndex, lineOffset, createHyperlink, createImage, darkMode, depth))
            inlines.Add(inline);
    }

    private static IEnumerable<Inline> ParseInline(
        string text,
        int lineIndex,
        int lineOffset,
        Func<string, string, Hyperlink> createHyperlink,
        Func<MarkdownImage, Inline>? createImage,
        bool darkMode, int depth = 0)
    {
        if (depth >= 16 || text.Length > 8192)
        {
            yield return new Run(text);
            yield break;
        }
        int pos = 0;
        while (pos < text.Length)
        {
            var next = FindNextInlineMarker(text, pos);
            if (next > pos)
            {
                foreach (var inline in ParsePlainLinks(text[pos..next], createHyperlink))
                    yield return inline;
                pos = next;
                continue;
            }

            if (TryGetEscapedMarkdownChar(text, pos, out var escapedChar))
            {
                yield return new Run(escapedChar.ToString());
                pos += 2;
                continue;
            }

            if (text[pos] == '`')
            {
                var end = FindUnescaped(text, "`", pos + 1);
                if (end > pos)
                {
                    yield return new Run(text[(pos + 1)..end])
                    {
                        FontFamily = CodeFontFamily,
                        Background = GetCodeBackground(darkMode),
                    };
                    pos = end + 1;
                    continue;
                }
            }

            if (TryGetDelimitedText(text, pos, "**", out var boldText, out var boldLength) ||
                TryGetDelimitedText(text, pos, "__", out boldText, out boldLength))
            {
                var span = new Span { FontWeight = FontWeights.Bold };
                AddInlineContent(span.Inlines, boldText, lineIndex, lineOffset + pos + 2, createHyperlink, createImage, darkMode, depth + 1);
                yield return span;
                pos += boldLength;
                continue;
            }

            if (TryGetDelimitedText(text, pos, "~~", out var strikeText, out var strikeLength))
            {
                var span = new Span { TextDecorations = TextDecorations.Strikethrough };
                AddInlineContent(span.Inlines, strikeText, lineIndex, lineOffset + pos + 2, createHyperlink, createImage, darkMode, depth + 1);
                yield return span;
                pos += strikeLength;
                continue;
            }

            if (TryGetDelimitedText(text, pos, "*", out var italicText, out var italicLength) ||
                TryGetDelimitedText(text, pos, "_", out italicText, out italicLength))
            {
                var span = new Span { FontStyle = FontStyles.Italic };
                AddInlineContent(span.Inlines, italicText, lineIndex, lineOffset + pos + 1, createHyperlink, createImage, darkMode, depth + 1);
                yield return span;
                pos += italicLength;
                continue;
            }

            if (TryGetMarkdownImage(text, pos, out var alt, out var imageTarget, out var imageLength, out var width, out var height))
            {
                if (createImage != null)
                    yield return createImage(new MarkdownImage(alt, imageTarget, lineIndex, lineOffset + pos, imageLength, width, height));
                else
                    yield return new Run(text.Substring(pos, imageLength));
                pos += imageLength;
                continue;
            }

            if (TryGetMarkdownLink(text, pos, out var label, out var target, out var length))
            {
                yield return createHyperlink(label, target);
                pos += length;
                continue;
            }

            if (TryGetAutolink(text, pos, out var autolinkTarget, out var autolinkLength))
            {
                yield return createHyperlink(autolinkTarget, autolinkTarget);
                pos += autolinkLength;
                continue;
            }

            foreach (var inline in ParsePlainLinks(text[pos].ToString(), createHyperlink))
                yield return inline;
            pos++;
        }
    }

    private static int FindNextInlineMarker(string text, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (TryGetEscapedMarkdownChar(text, i, out _))
                return i;

            if (text[i] == '`' && FindUnescaped(text, "`", i + 1) > i)
                return i;
            if (TryGetDelimitedText(text, i, "~~", out _, out _) ||
                TryGetDelimitedText(text, i, "**", out _, out _) ||
                TryGetDelimitedText(text, i, "__", out _, out _) ||
                TryGetDelimitedText(text, i, "*", out _, out _) ||
                TryGetDelimitedText(text, i, "_", out _, out _) ||
                text.AsSpan(i).StartsWith("![", StringComparison.Ordinal) ||
                text[i] == '[' ||
                text[i] == '<')
            {
                return i;
            }
        }

        return text.Length;
    }

    private static bool TryGetDelimitedText(string text, int start, string marker, out string innerText, out int length)
    {
        innerText = "";
        length = 0;
        if (!text.AsSpan(start).StartsWith(marker, StringComparison.Ordinal))
            return false;
        if ((marker == "_" || marker == "__") && IsWordChar(GetCharOrDefault(text, start - 1)))
            return false;

        var end = FindUnescaped(text, marker, start + marker.Length);
        if (end <= start + marker.Length)
            return false;
        if ((marker == "_" || marker == "__") && IsWordChar(GetCharOrDefault(text, end + marker.Length)))
            return false;

        innerText = text[(start + marker.Length)..end];
        length = end - start + marker.Length;
        return true;
    }

    private static int FindUnescaped(string text, string marker, int start)
    {
        for (var i = start; i <= text.Length - marker.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i++;
                continue;
            }

            if (text.AsSpan(i).StartsWith(marker, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static bool TryGetEscapedMarkdownChar(string text, int start, out char escapedChar)
    {
        escapedChar = '\0';
        if (start + 1 >= text.Length || text[start] != '\\' || !IsEscapableMarkdownChar(text[start + 1]))
            return false;

        escapedChar = text[start + 1];
        return true;
    }

    private static bool IsEscapableMarkdownChar(char ch)
        => ch is '\\' or '`' or '*' or '_' or '{' or '}' or '[' or ']' or '(' or ')' or '#' or '+' or '-' or '.' or '!' or '|' or '<' or '>' or '~';

    private static bool IsWordChar(char ch)
        => char.IsLetterOrDigit(ch) || ch == '_';

    private static char GetCharOrDefault(string text, int index)
        => index >= 0 && index < text.Length ? text[index] : '\0';

    internal static bool TryGetMarkdownLink(
        string text,
        int start,
        out string label,
        out string target,
        out int length)
    {
        label = "";
        target = "";
        length = 0;

        if (text[start] != '[') return false;

        var labelEnd = FindUnescaped(text, "]", start + 1);
        if (labelEnd <= start || labelEnd + 1 >= text.Length || text[labelEnd + 1] != '(')
            return false;

        if (!TryReadMarkdownTarget(text, labelEnd + 1, out target, out var targetLength))
            return false;

        label = UnescapeMarkdownText(text[(start + 1)..labelEnd]);
        length = labelEnd + 1 + targetLength - start;
        return LinkDetector.IsLink(target);
    }

    private static bool TryGetMarkdownImage(
        string text,
        int start,
        out string alt,
        out string target,
        out int length,
        out double? width,
        out double? height)
    {
        alt = "";
        target = "";
        length = 0;
        width = null;
        height = null;

        if (start + 1 >= text.Length || text[start] != '!' || text[start + 1] != '[')
            return false;

        var altEnd = text.IndexOf(']', start + 2);
        if (altEnd <= start + 1 || altEnd + 1 >= text.Length || text[altEnd + 1] != '(')
            return false;

        if (!TryReadMarkdownTarget(text, altEnd + 1, out target, out var targetLength))
            return false;

        alt = text[(start + 2)..altEnd];
        length = altEnd + 1 + targetLength - start;
        while (TryReadImageAttributes(text, start + length, out var attrLength, out var attrWidth, out var attrHeight))
        {
            length += attrLength;
            if (attrWidth.HasValue)
                width = attrWidth;
            if (attrHeight.HasValue)
                height = attrHeight;
        }

        return target.Length > 0;
    }

    private static bool TryReadMarkdownTarget(
        string text,
        int openParenIndex,
        out string target,
        out int length)
    {
        target = "";
        length = 0;
        if (openParenIndex >= text.Length || text[openParenIndex] != '(')
            return false;

        var start = openParenIndex + 1;
        if (start < text.Length && text[start] == '<')
        {
            var closeAngleIndex = text.IndexOf('>', start + 1);
            if (closeAngleIndex > start + 1 &&
                closeAngleIndex + 1 < text.Length &&
                text[closeAngleIndex + 1] == ')')
            {
                // <...> で囲まれたリンク先は、括弧やバックスラッシュを
                // Markdown エスケープとして扱わず、そのままURLとして渡す。
                // 前後の空白だけは落とす（付けたまま渡すと起動に失敗する）。
                var angleTarget = text[(start + 1)..closeAngleIndex].Trim();
                if (angleTarget.Length > 0)
                {
                    target = angleTarget;
                    length = closeAngleIndex + 2 - openParenIndex;
                    return true;
                }
            }
        }

        var depth = 0;
        var fallbackEnd = -1;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\\' && i + 1 < text.Length)
            {
                i++;
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }

            if (ch != ')')
                continue;

            if (fallbackEnd < 0)
                fallbackEnd = i; // 深さが0に戻らない不正な入力では、最初の ')' を採用する
            if (depth > 0)
            {
                depth--;
                continue;
            }

            return FinishMarkdownTarget(text, openParenIndex, i, out target, out length);
        }

        return fallbackEnd > start &&
               FinishMarkdownTarget(text, openParenIndex, fallbackEnd, out target, out length);
    }

    private static bool FinishMarkdownTarget(
        string text,
        int openParenIndex,
        int closeParenIndex,
        out string target,
        out int length)
    {
        target = UnescapeMarkdownText(StripOptionalMarkdownTitle(text[(openParenIndex + 1)..closeParenIndex]));
        length = closeParenIndex - openParenIndex + 1;
        return target.Length > 0;
    }

    private static string StripOptionalMarkdownTitle(string rawTarget)
    {
        var value = rawTarget.Trim();
        if (value.StartsWith("<", StringComparison.Ordinal) && value.IndexOf('>') is var angleEnd && angleEnd > 1)
            return value[1..angleEnd].Trim();

        var firstWhitespace = IndexOfWhitespace(value);
        if (firstWhitespace < 0)
            return value;

        var possibleTitle = value[firstWhitespace..].TrimStart();
        if (possibleTitle.Length >= 2 &&
            (possibleTitle[0] == '"' && possibleTitle[^1] == '"' ||
             possibleTitle[0] == '\'' && possibleTitle[^1] == '\'' ||
             possibleTitle[0] == '(' && possibleTitle[^1] == ')'))
        {
            return value[..firstWhitespace].Trim();
        }

        return value;
    }

    private static string UnescapeMarkdownText(string text)
    {
        var result = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && IsEscapableMarkdownChar(text[i + 1]))
            {
                result.Append(text[++i]);
                continue;
            }

            result.Append(text[i]);
        }

        return result.ToString();
    }

    private static int IndexOfWhitespace(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                return i;
        }

        return -1;
    }

    private static bool TryGetAutolink(string text, int start, out string target, out int length)
    {
        target = "";
        length = 0;
        if (text[start] != '<')
            return false;

        var end = text.IndexOf('>', start + 1);
        if (end <= start + 1)
            return false;

        target = text[(start + 1)..end].Trim();
        length = end - start + 1;
        return LinkDetector.IsExactLink(target);
    }

    private static bool TryReadImageAttributes(
        string text,
        int start,
        out int length,
        out double? width,
        out double? height)
    {
        length = 0;
        width = null;
        height = null;

        if (start >= text.Length || text[start] != '{')
            return false;

        var end = text.IndexOf('}', start + 1);
        if (end <= start + 1)
            return false;

        foreach (var part in text[(start + 1)..end].Split([' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 ||
                !double.TryParse(pair[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ||
                value < 0)
            {
                continue;
            }

            if (string.Equals(pair[0], "width", StringComparison.OrdinalIgnoreCase))
                width = value;
            else if (string.Equals(pair[0], "height", StringComparison.OrdinalIgnoreCase))
                height = value;
        }

        length = end - start + 1;
        return true;
    }

    private static IEnumerable<Inline> ParsePlainLinks(
        string text,
        Func<string, string, Hyperlink> createHyperlink)
    {
        foreach (var segment in LinkDetector.Parse(text))
        {
            yield return segment.IsLink
                ? createHyperlink(segment.Text, segment.Text)
                : new Run(segment.Text);
        }
    }
}
