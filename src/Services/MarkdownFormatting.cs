using System.Text.RegularExpressions;

namespace ScreenPinNotes.Services;

public static class MarkdownFormatting
{
    public sealed record Edit(int Start, int Length, string Replacement, int SelectionStart, int SelectionLength);

    // 行頭のマーカー（見出し / 箇条書き / チェックリスト / 番号付き）。
    private static readonly Regex LineMarker = new(@"^(#{1,6} |[-*+] (?:\[[ xX]\] )?|\d+\. )");
    // チェックリストは未チェックとチェック済みを同じ書式として扱う。
    private static readonly Regex TaskMarker = new(@"^[-*+] \[[ xX]\] ");

    public static bool IsRangeValid(string text, int start, int length)
        => start >= 0 && length >= 0 && start <= text.Length && length <= text.Length - start;

    private static Edit Unchanged(string text, int start, int length)
        => new(0, 0, "", Math.Clamp(start, 0, text.Length), 0);

    public static Edit Inline(string text, int start, int length, string marker)
    {
        if (!IsRangeValid(text, start, length) || marker is not ("**" or "~~" or "`"))
            return Unchanged(text, start, length);
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

    public static Edit Lines(string text, int start, int length, string prefix)
    {
        if (!IsRangeValid(text, start, length) || prefix is not ("# " or "## " or "### " or "- " or "- [ ] "))
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
        foreach (var i in targets)
        {
            var indentLength = parts[i].Length - parts[i].TrimStart().Length;
            var indent = parts[i][..indentLength];
            var body = parts[i][indentLength..];
            body = body[LineMarker.Match(body).Length..];
            // 見出しは行頭になければ描画されないので、付けるときだけ字下げを落とす。
            parts[i] = remove
                ? indent + body
                : (prefix[0] == '#' ? "" : indent) + prefix + body;
        }
        var replacement = string.Concat(parts);
        return new(first, end - first, replacement, first, replacement.Length);
    }

    // その行が prefix と同じ書式かどうか。"- " は箇条書きだけに一致させ
    // （"- [ ] " は別書式）、"- [ ] " はチェック済みの行にも一致させる。
    private static bool HasPrefix(string line, string prefix)
    {
        var body = line.TrimStart();
        if (prefix == "- [ ] ") return TaskMarker.IsMatch(body);
        if (prefix == "- ") return body.StartsWith("- ") && !TaskMarker.IsMatch(body);
        return body.StartsWith(prefix);
    }
}
