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

namespace ScreenPinNotes.Services;

/// <summary>
/// ログ行に現れるレベル名（INFO / WARN / ERROR など）を見つける。
/// tail 表示の色分けと、更新を知らせる枠の色に使う。
/// </summary>
public static class LogLevelHighlighter
{
    public enum Severity { Info, Warning, Error }

    /// <summary>色を変える対象の種類。</summary>
    public enum SpanKind { Level, Number }

    /// <summary>行の中でのレベル名の位置と重さ。</summary>
    public readonly record struct LevelMatch(int Start, int Length, Severity Severity);

    /// <summary>色を変える範囲。<see cref="Severity"/> は <see cref="SpanKind.Level"/> のときだけ意味を持つ。</summary>
    public readonly record struct HighlightSpan(int Start, int Length, SpanKind Kind, Severity Severity);

    /// <summary>
    /// 出力側によってレベルの書き方が違うので、実際に見かける表記を並べておく。
    /// 3文字の略記は Serilog の既定（{Level:u3}）、dbug/trce/fail/crit は
    /// Microsoft.Extensions.Logging のコンソール、SEVERE は java.util.logging、
    /// PANIC は Go、ALERT/EMERG/NOTICE は syslog で使われる。
    /// 1文字表記（logcat の I/W/E など）は、本文の1文字と見分けが付かないので扱わない。
    /// </summary>
    private static readonly (string Word, Severity Severity)[] Levels =
    [
        ("TRACE", Severity.Info),
        ("TRC", Severity.Info),
        ("TRCE", Severity.Info),
        ("VERBOSE", Severity.Info),
        ("VRB", Severity.Info),
        ("DEBUG", Severity.Info),
        ("DBG", Severity.Info),
        ("DBUG", Severity.Info),
        ("INFO", Severity.Info),
        ("INF", Severity.Info),
        ("INFORMATION", Severity.Info),
        ("NOTICE", Severity.Info),

        ("WARN", Severity.Warning),
        ("WRN", Severity.Warning),
        ("WARNING", Severity.Warning),

        ("ERROR", Severity.Error),
        ("ERR", Severity.Error),
        ("FAIL", Severity.Error),
        ("FATAL", Severity.Error),
        ("FTL", Severity.Error),
        ("CRIT", Severity.Error),
        ("CRITICAL", Severity.Error),
        ("SEVERE", Severity.Error),
        ("PANIC", Severity.Error),
        ("ALERT", Severity.Error),
        ("EMERG", Severity.Error),
    ];

    // 語の長さで先に振り分け、文字を比べる回数を抑える。
    private static readonly int ShortestLevel = Levels.Min(level => level.Word.Length);
    private static readonly int LongestLevel = Levels.Max(level => level.Word.Length);

    /// <summary>
    /// 行の中で最初に現れるレベル名を返す。ログはたいてい行頭側にレベルを置くので、
    /// 1行につき1つだけ色を付ければ足りる（本文中の "error" まで塗らずに済む）。
    /// 語まるごとで見るため、"informational" のように語の一部が一致しても拾わない。
    /// </summary>
    public static LevelMatch? FindFirst(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (!char.IsLetter(line[i])) continue;

            var start = i;
            while (i < line.Length && char.IsLetter(line[i])) i++;
            var length = i - start;
            i--;
            if (length < ShortestLevel || length > LongestLevel) continue;
            if (MatchLevel(line, start, length) is { } severity)
                return new LevelMatch(start, length, severity);
        }

        return null;
    }

    private static bool IsNumberSeparator(char c) => c is ':' or '.' or '-' or '/' or ',';

    private static Severity? MatchLevel(string line, int start, int length)
    {
        foreach (var (word, severity) in Levels)
        {
            if (word.Length != length) continue;
            if (string.Compare(line, start, word, 0, length, StringComparison.OrdinalIgnoreCase) != 0) continue;
            return severity;
        }

        return null;
    }

    /// <summary>
    /// 1行の中で色を変える範囲を、前から順に返す。レベル名（行で最初の1つ）と、
    /// 数値のまとまりを1回の走査で拾う。
    /// </summary>
    public static List<HighlightSpan> FindHighlights(string line)
    {
        var spans = new List<HighlightSpan>();
        var levelFound = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (char.IsAsciiDigit(line[i]))
            {
                var start = i;
                var end = i;
                while (true)
                {
                    while (end < line.Length && char.IsAsciiDigit(line[end])) end++;
                    // 2026-09-15 や 09:12:01.004、16/16 のように数字どうしを繋ぐ
                    // 区切りは、ひとつながりの値として扱う。1文字ずつ切り分けると
                    // 日時が細切れに見えるうえ、描画する要素も倍以上に増える。
                    if (end + 1 < line.Length && IsNumberSeparator(line[end]) && char.IsAsciiDigit(line[end + 1]))
                        end++;
                    else
                        break;
                }
                spans.Add(new HighlightSpan(start, end - start, SpanKind.Number, default));
                i = end - 1;
                continue;
            }

            if (levelFound || !char.IsLetter(line[i])) continue;

            var wordStart = i;
            while (i < line.Length && char.IsLetter(line[i])) i++;
            var length = i - wordStart;
            i--;
            if (length < ShortestLevel || length > LongestLevel) continue;
            if (MatchLevel(line, wordStart, length) is not { } severity) continue;

            spans.Add(new HighlightSpan(wordStart, length, SpanKind.Level, severity));
            levelFound = true;
        }

        return spans;
    }

    /// <summary>エラー扱いのレベル（ERROR / FATAL / CRITICAL など）の行を含むか。</summary>
    public static bool ContainsError(string text)
    {
        foreach (var line in SplitLines(text))
            if (FindFirst(line) is { Severity: Severity.Error })
                return true;

        return false;
    }

    /// <summary>
    /// 前回表示していた内容から、今回増えた分だけを返す。tail は行数を保ったまま
    /// 窓がずれていくので単純な差分では取れない。直前の最終行を今回の内容の
    /// 末尾側から探し、その後ろを新着とみなす。見当たらなければ全体を新着として扱う。
    /// </summary>
    public static string GetAppendedText(string previous, string current)
    {
        if (string.IsNullOrEmpty(previous)) return current;
        // 窓がずれず、末尾に足されただけの場合。
        if (current.StartsWith(previous, StringComparison.Ordinal)) return current[previous.Length..];

        var anchor = SplitLines(previous).LastOrDefault(line => line.Length > 0);
        if (anchor == null) return current;

        var lines = SplitLines(current);
        for (var i = lines.Length - 1; i >= 0; i--)
            if (string.Equals(lines[i], anchor, StringComparison.Ordinal))
                return string.Join('\n', lines[(i + 1)..]);

        return current;
    }

    private static string[] SplitLines(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
}
