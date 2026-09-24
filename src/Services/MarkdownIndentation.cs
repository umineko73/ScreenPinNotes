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

using System.Text;
using System.Text.RegularExpressions;

namespace ScreenPinNotes.Services;

/// <summary>
/// 編集欄の Tab / Shift+Tab で行を字下げする・戻す。
/// 字下げはタブ1つ。戻すときはタブ1つ、スペース4つまで、または全角スペース1つを外す。
/// </summary>
public static partial class MarkdownIndentation
{
    /// <summary>
    /// 本文の <paramref name="Start"/> から <paramref name="Length"/> 文字を
    /// <paramref name="Replacement"/> に置き換え、選択を指定の範囲にする。
    /// </summary>
    public readonly record struct Edit(int Start, int Length, string Replacement, int SelectionStart, int SelectionLength);

    /// <summary>
    /// Tab で行を字下げする。複数行を選んでいるか、リストの行にいるときだけ字下げし、
    /// それ以外は null（ふつうにタブ文字を入れる）を返す。
    /// </summary>
    public static Edit? Indent(string text, int selectionStart, int selectionLength)
    {
        var (start, end) = LineRange(text, selectionStart, selectionLength);
        var multiLine = text.AsSpan(start, end - start).Contains('\n');
        if (!multiLine && !ListItemPattern().IsMatch(text[start..end]))
            return null;

        return Rewrite(text, start, end, selectionStart, selectionLength, multiLine,
            line => line.Trim().Length == 0 ? (line, 0) : ("\t" + line, 1));
    }

    /// <summary>Shift+Tab で行の字下げを1段戻す。戻すものが無ければ null。</summary>
    public static Edit? Outdent(string text, int selectionStart, int selectionLength)
    {
        var (start, end) = LineRange(text, selectionStart, selectionLength);
        var multiLine = text.AsSpan(start, end - start).Contains('\n');
        var edit = Rewrite(text, start, end, selectionStart, selectionLength, multiLine, line =>
        {
            var remove = OutdentLength(line);
            return (line[remove..], -remove);
        });
        return edit.Replacement == text[start..end] ? null : edit;
    }

    private static int OutdentLength(string line)
    {
        if (line.StartsWith('\t') || line.StartsWith('\u3000'))
            return 1;
        var spaces = 0;
        while (spaces < line.Length && spaces < 4 && line[spaces] == ' ')
            spaces++;
        return spaces;
    }

    /// <summary>選択が掛かっている行の範囲（最後の行の改行は含めない）。</summary>
    private static (int Start, int End) LineRange(string text, int selectionStart, int selectionLength)
    {
        selectionStart = Math.Clamp(selectionStart, 0, text.Length);
        var selectionEnd = Math.Clamp(selectionStart + selectionLength, selectionStart, text.Length);
        // 次の行の先頭までの選択（行を丸ごと選んだとき）は、その次の行を含めない。
        if (selectionEnd > selectionStart && text[selectionEnd - 1] == '\n')
            selectionEnd--;

        var start = selectionStart == 0 ? 0 : text.LastIndexOf('\n', selectionStart - 1) + 1;
        var end = text.IndexOf('\n', selectionEnd);
        if (end < 0) end = text.Length;
        if (end > start && text[end - 1] == '\r') end--;
        return (start, Math.Max(start, end));
    }

    private static Edit Rewrite(string text, int start, int end, int selectionStart, int selectionLength,
        bool multiLine, Func<string, (string Line, int Delta)> change)
    {
        var builder = new StringBuilder();
        var firstDelta = 0;
        var first = true;
        foreach (var raw in text[start..end].Split('\n'))
        {
            var hasCr = raw.EndsWith('\r');
            var (line, delta) = change(hasCr ? raw[..^1] : raw);
            if (!first) builder.Append('\n');
            builder.Append(line);
            if (hasCr) builder.Append('\r');
            if (first) firstDelta = delta;
            first = false;
        }

        var replacement = builder.ToString();
        if (multiLine)
            return new Edit(start, end - start, replacement, start, replacement.Length);

        // 1行だけのときはカーソル（選択）を文字と一緒にずらす。字下げを戻して
        // 行頭より前に出るときは行頭で止める。
        var caret = Math.Max(start, selectionStart + firstDelta);
        return new Edit(start, end - start, replacement, caret, selectionLength);
    }

    /// <summary>
    /// \u30ea\u30b9\u30c8\u306e\u884c\u3067 Enter \u3092\u62bc\u3057\u305f\u3068\u304d\u306e\u7de8\u96c6\u3002\u6b21\u306e\u884c\u306b\u540c\u3058\u5b57\u4e0b\u3052\u3067\u5370\uff08\u6b21\u306e\u756a\u53f7\u30fb\u672a\u5b8c\u4e86\u306e
    /// \u30c1\u30a7\u30c3\u30af\u30dc\u30c3\u30af\u30b9\uff09\u3092\u5165\u308c\u308b\u3002\u4e2d\u8eab\u306e\u7121\u3044\u9805\u76ee\u3067\u306f\u3001\u5b57\u4e0b\u3052\u30921\u6bb5\u623b\u3059\u304b\u3001\u5370\u3092\u6d88\u3057\u3066\u30ea\u30b9\u30c8\u3092\u629c\u3051\u308b\u3002
    /// \u30ea\u30b9\u30c8\u306e\u884c\u3067\u306a\u3044\u3001\u5370\u3088\u308a\u524d\u306b\u30ab\u30fc\u30bd\u30eb\u304c\u3042\u308b\u3001\u9078\u629e\u3057\u3066\u3044\u308b\u3068\u304d\u306f null\uff08\u3075\u3064\u3046\u306e\u6539\u884c\uff09\u3002
    /// </summary>
    public static Edit? ContinueList(string text, int caret, string newLine)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var (start, end) = LineRange(text, caret, 0);
        var line = text[start..end];
        var match = ListMarkerPattern().Match(line);
        if (!match.Success || caret - start < match.Length)
            return null;

        var indent = match.Groups["indent"].Value;
        if (line[match.Length..].Trim().Length == 0)
        {
            // \u4e2d\u8eab\u306e\u7121\u3044\u9805\u76ee\u3002\u5b57\u4e0b\u3052\u3057\u3066\u3044\u308c\u30701\u6bb5\u623b\u3057\u3001\u3057\u3066\u3044\u306a\u3051\u308c\u3070\u5370\u3092\u6d88\u3057\u3066\u629c\u3051\u308b\u3002
            var remove = OutdentLength(line);
            var replacement = remove > 0 ? line[remove..] : "";
            var caretAfter = start + (remove > 0 ? caret - start - remove : 0);
            return new Edit(start, end - start, replacement, caretAfter, 0);
        }

        string marker;
        if (match.Groups["number"].Success)
        {
            var number = long.Parse(match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture);
            marker = (number + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + match.Groups["delimiter"].Value;
        }
        else
        {
            marker = match.Groups["bullet"].Value;
        }
        var task = match.Groups["task"].Success ? "[ ] " : "";
        var inserted = newLine + indent + marker + " " + task;
        return new Edit(caret, 0, inserted, caret + inserted.Length, 0);
    }

    [GeneratedRegex(@"^[ \t\u3000]*(?:[-*+]|\d{1,9}[.)])[ \t]")]
    private static partial Regex ListItemPattern();

    [GeneratedRegex(@"^(?<indent>[ \t\u3000]*)(?:(?<bullet>[-*+])|(?<number>\d{1,9})(?<delimiter>[.)]))[ \t](?<task>\[[ xX]\] )?")]
    private static partial Regex ListMarkerPattern();
}
