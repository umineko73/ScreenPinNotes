using System.Text.RegularExpressions;

namespace ScreenPinNotes.Services;

public static class MarkdownFormatting
{
    public sealed record Edit(int Start, int Length, string Replacement, int SelectionStart, int SelectionLength);
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
            return new(start, length, selected[n..^n], start, length - n * 2);
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
        var remove = parts.Where((_, i) => i % 2 == 0).All(line => line.StartsWith(prefix));
        for (var i = 0; i < parts.Length; i += 2)
        {
            parts[i] = remove ? parts[i][prefix.Length..] :
                prefix + Regex.Replace(parts[i], @"^(#{1,6} |[-*+] (?:\[[ xX]\] )?|\d+\. )", "");
        }
        var replacement = string.Concat(parts);
        return new(first, end - first, replacement, first, replacement.Length);
    }
}
