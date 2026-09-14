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

/// <summary>
/// ログ行に現れるレベル名（INFO / WARN / ERROR など）を見つける。
/// tail 表示の色分けと、更新を知らせる枠の色に使う。
/// </summary>
public static class LogLevelHighlighter
{
    public enum Severity { Info, Warning, Error }

    /// <summary>行の中でのレベル名の位置と重さ。</summary>
    public readonly record struct LevelMatch(int Start, int Length, Severity Severity);

    // 長さで先に振り分けられるよう、語はすべて4〜7文字。
    private static readonly (string Word, Severity Severity)[] Levels =
    [
        ("INFO", Severity.Info),
        ("WARN", Severity.Warning),
        ("TRACE", Severity.Info),
        ("DEBUG", Severity.Info),
        ("ERROR", Severity.Error),
        ("FATAL", Severity.Error),
        ("WARNING", Severity.Warning),
    ];

    private const int ShortestLevel = 4;
    private const int LongestLevel = 7;

    /// <summary>
    /// 行の中で最初に現れるレベル名を返す。ログはたいてい行頭側にレベルを置くので、
    /// 1行につき1つだけ色を付ければ足りる（本文中の "error" まで塗らずに済む）。
    /// 語の区切りで見るため、"information" が INFO として拾われることはない。
    /// </summary>
    public static LevelMatch? FindFirst(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (!char.IsLetter(line[i])) continue;

            var start = i;
            while (i < line.Length && char.IsLetter(line[i])) i++;
            var length = i - start;
            if (length is < ShortestLevel or > LongestLevel) continue;

            foreach (var (word, severity) in Levels)
            {
                if (word.Length != length) continue;
                if (string.Compare(line, start, word, 0, length, StringComparison.OrdinalIgnoreCase) != 0) continue;
                return new LevelMatch(start, length, severity);
            }
        }

        return null;
    }

    /// <summary>ERROR / FATAL の行を含むか。</summary>
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
