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

public static class MarkdownFormatting
{
    public sealed record Edit(int Start, int Length, string Replacement, int SelectionStart, int SelectionLength);

    // 行頭のマーカー（見出し / 箇条書き / チェックリスト / 番号付き）。
    private static readonly Regex LineMarker = new(@"^(#{1,6} |[-*+] (?:\[[ xX]\] )?|\d{1,9}[.)] )");
    private static readonly Regex NumberMarker = new(@"^\d{1,9}[.)] ");
    // チェックリストは未チェックとチェック済みを同じ書式として扱う。
    private static readonly Regex TaskMarker = new(@"^[-*+] \[[ xX]\] ");

    public static bool IsRangeValid(string text, int start, int length)
        => start >= 0 && length >= 0 && start <= text.Length && length <= text.Length - start;

    private static Edit Unchanged(string text, int start, int length)
        => new(0, 0, "", Math.Clamp(start, 0, text.Length), 0);

    public static Edit Inline(string text, int start, int length, string marker)
    {
        if (!IsRangeValid(text, start, length) || marker is not ("**" or "*" or "~~" or "==" or "`"))
            return Unchanged(text, start, length);
        if (marker == "*")
            return Italic(text, start, length);
        var selected = text.Substring(start, length);
        var n = marker.Length;
        if (length >= n * 2 && selected.StartsWith(marker) && selected.EndsWith(marker))
        {
            // 前後のマーカーが1組の対になっているときだけ外側を外す。
            // "**a** and **b**" のように内側にも残る場合は対ではないので、
            // 外側だけ外すと本文が壊れる。選択範囲全体が既にその書式なので、
            // 範囲内のマーカーをすべて外して書式解除として扱う。
            var inner = selected[n..^n];
            var replacement = inner.Contains(marker) ? selected.Replace(marker, "") : inner;
            return new(start, length, replacement, start, replacement.Length);
        }
        if (start >= n && start + length + n <= text.Length &&
            text.Substring(start - n, n) == marker && text.Substring(start + length, n) == marker)
            return new(start - n, length + n * 2, selected, start - n, length);
        return new(start, length, marker + selected + marker, start + n, length);
    }

    // 斜体の * は太字の ** と字が同じなので、並んだ * の数で見分ける。
    // 1 個なら斜体、3 個なら太字斜体（外すと太字が残る）、2 個は太字なので斜体ではない。
    private static Edit Italic(string text, int start, int length)
    {
        var selected = text.Substring(start, length);
        var leading = CountStars(selected, 0, 1);
        var trailing = CountStars(selected, length - 1, -1);
        if (leading == trailing && leading is 1 or 3 && length > leading * 2)
        {
            var inner = selected[1..^1];
            return new(start, length, inner, start, inner.Length);
        }
        var before = start == 0 ? 0 : CountStars(text, start - 1, -1);
        var after = CountStars(text, start + length, 1);
        if (before == after && before is 1 or 3)
            return new(start - 1, length + 2, selected, start - 1, length);
        return new(start, length, "*" + selected + "*", start + 1, length);
    }

    private static int CountStars(string text, int from, int step)
    {
        var count = 0;
        for (var i = from; i >= 0 && i < text.Length && text[i] == '*'; i += step)
            count++;
        return count;
    }

    // 修飾を外す対象。本文を包む印と、その中身の位置 (グループ "c")。
    // 中身の前後に空白は置かない（描画側と同じく "a * b * c" を斜体にしない）。
    // リンクは表示名だけを残す。画像は外すと消えてしまうので対象にしない。
    private static readonly Regex[] InlineSpans =
    [
        new(@"`(?<c>[^`\n]+)`"),
        new(@"(?<!!)\[(?<c>[^\]\n]+)\]\([^)\n]*\)"),
        new(@"\*\*(?=\S)(?<c>[^\n]*?\S)\*\*"),
        new(@"__(?=\S)(?<c>[^\n]*?\S)__"),
        new(@"~~(?=\S)(?<c>[^\n]*?\S)~~"),
        new(@"==(?=\S)(?<c>[^\n]*?\S)=="),
        new(@"(?<!\*)\*(?<c>[^\s*](?:[^\n*]*[^\s*])?)\*(?!\*)"),
        new(@"(?<![\w_])_(?<c>[^\s_](?:[^\n_]*[^\s_])?)_(?![\w_])"),
    ];

    private static readonly Regex QuoteMarker = new(@"^>[ ]?");

    /// <summary>
    /// 選んだ範囲の修飾を外して素の文字にする。範囲に掛かっている太字・斜体・
    /// 取り消し線・蛍光ペン・コード・リンクを外し、範囲が掛かる行の
    /// 見出し・箇条書き・番号・チェック・引用の印も外す。範囲の一部だけに
    /// 掛かっている修飾は、範囲の外に残る部分だけを包み直す。
    /// </summary>
    public static Edit Clear(string text, int start, int length)
    {
        if (!IsRangeValid(text, start, length))
            return Unchanged(text, start, length);
        var first = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var lastSelected = length == 0 ? start : start + length - 1;
        var end = text.IndexOf('\n', lastSelected);
        if (end < 0) end = text.Length;
        if (end > first && text[end - 1] == '\r') end--;

        var block = new System.Text.StringBuilder();
        var selStart = start - first;
        var selEnd = selStart + length;

        // 行頭の印。引用の中の箇条書きのような重なりも、印が無くなるまで外す。
        // 印を外した行は字下げも落とす（残すと素の行にならない）。
        var lineStart = 0;
        foreach (var part in Regex.Split(text[first..end], "(\r?\n)"))
        {
            if (part is "\n" or "\r\n")
            {
                block.Append(part);
                lineStart += part.Length;
                continue;
            }
            var line = part;
            var removed = 0;
            while (true)
            {
                var body = line.TrimStart();
                var marker = QuoteMarker.Match(body);
                if (!marker.Success) marker = LineMarker.Match(body);
                if (!marker.Success) break;
                var cut = line.Length - body.Length + marker.Length;
                line = line[cut..];
                removed += cut;
            }
            selStart = MapDeletion(selStart, lineStart, removed);
            selEnd = MapDeletion(selEnd, lineStart, removed);
            block.Append(line);
            lineStart += line.Length;
        }

        // 文字の修飾。外側を外すと内側が見つかるようになるので、変化が無くなるまで繰り返す。
        var result = block.ToString();
        for (var guard = 0; guard < 10_000 && TryClearOneSpan(ref result, ref selStart, ref selEnd); guard++)
        {
        }

        return new(first, end - first, result, first + selStart, selEnd - selStart);
    }

    private static int MapDeletion(int position, int at, int removed)
        => position <= at ? position : position >= at + removed ? position - removed : at;

    private static bool TryClearOneSpan(ref string block, ref int selStart, ref int selEnd)
    {
        var caretOnly = selStart == selEnd;
        foreach (var pattern in InlineSpans)
        {
            foreach (Match match in pattern.Matches(block))
            {
                var ms = match.Index;
                var me = ms + match.Length;
                var content = match.Groups["c"];
                var cs = content.Index;
                var ce = cs + content.Length;
                // 範囲が空のときは、カーソルを含む修飾を丸ごと外す。
                var touches = caretOnly
                    ? cs <= selStart && selStart <= ce
                    : ms < selEnd && selStart < me;
                if (!touches) continue;

                // 範囲の外に残る部分は元の修飾で包み直す。リンクは分けられないので丸ごと外す。
                var splittable = !caretOnly && block[ms] != '[';
                var open = block[ms..cs];
                var close = block[ce..me];
                var inner = block[cs..ce];
                var keepLeft = splittable ? Math.Clamp(selStart - cs, 0, inner.Length) : 0;
                var keepRight = splittable ? Math.Clamp(ce - selEnd, 0, inner.Length - keepLeft) : 0;
                var left = keepLeft > 0 ? open + inner[..keepLeft] + close : "";
                var middle = inner[keepLeft..(inner.Length - keepRight)];
                var right = keepRight > 0 ? open + inner[^keepRight..] + close : "";
                var replacement = left + middle + right;

                int Map(int position) =>
                    position <= ms ? position
                    : position >= me ? position - match.Length + replacement.Length
                    : ms + left.Length + Math.Clamp(position - cs - keepLeft, 0, middle.Length);

                block = block[..ms] + replacement + block[me..];
                selStart = Map(selStart);
                selEnd = caretOnly ? selStart : Math.Max(selStart, Map(selEnd));
                return true;
            }
        }
        return false;
    }

    public static Edit Lines(string text, int start, int length, string prefix)
    {
        if (!IsRangeValid(text, start, length) || prefix is not ("# " or "## " or "### " or "- " or "- [ ] " or "1. " or "> "))
            return Unchanged(text, start, length);
        var first = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var lastSelected = length == 0 ? start : start + length - 1;
        var end = text.IndexOf('\n', lastSelected);
        if (end < 0) end = text.Length;
        if (end > first && text[end - 1] == '\r') end--;
        var parts = Regex.Split(text[first..end], "(\r?\n)");

        // 空行はトグル判定にも書き換えにも含めない。含めると、リストの
        // 途中に空行があるだけで「解除」が「空行へのマーカー追加」に化ける。
        // 選択が空行だけのときは、その行を対象にしてマーカーを付ける。
        var targets = new List<int>();
        for (var i = 0; i < parts.Length; i += 2)
            if (!string.IsNullOrWhiteSpace(parts[i]))
                targets.Add(i);
        if (targets.Count == 0)
            targets.Add(0);

        var remove = targets.All(i => HasPrefix(parts[i], prefix));
        var number = 0;
        foreach (var i in targets)
        {
            // 引用は行の中身（リストの印など）をそのまま包む。
            if (prefix == "> ")
            {
                parts[i] = remove ? RemoveQuoteMarker(parts[i]) : prefix + parts[i];
                continue;
            }
            var indentLength = parts[i].Length - parts[i].TrimStart().Length;
            var indent = parts[i][..indentLength];
            var body = parts[i][indentLength..];
            body = body[LineMarker.Match(body).Length..];
            // 見出しは行頭になければ描画されないので、付けるときだけ字下げを落とす。
            // 番号付きは選んだ行に上から 1, 2, 3... と振る。
            var marker = prefix == "1. " ? $"{++number}. " : prefix;
            parts[i] = remove
                ? indent + body
                : (prefix[0] == '#' ? "" : indent) + marker + body;
        }
        var replacement = string.Concat(parts);
        return new(first, end - first, replacement, first, replacement.Length);
    }

    private static string RemoveQuoteMarker(string line)
    {
        var at = line.Length - line.TrimStart().Length;
        var end = at + 1;
        if (end < line.Length && line[end] == ' ') end++;
        return line[..at] + line[end..];
    }

    // その行が prefix と同じ書式かどうか。"- " は箇条書きだけに一致させ
    // （"- [ ] " は別書式）、"- [ ] " はチェック済みの行にも一致させる。
    private static bool HasPrefix(string line, string prefix)
    {
        var body = line.TrimStart();
        if (prefix == "- [ ] ") return TaskMarker.IsMatch(body);
        if (prefix == "- ") return body.StartsWith("- ") && !TaskMarker.IsMatch(body);
        if (prefix == "1. ") return NumberMarker.IsMatch(body);
        if (prefix == "> ") return body.StartsWith('>');
        return body.StartsWith(prefix);
    }
}
