using System.Text.RegularExpressions;

namespace ScreenStickyNotes.Services;

public static class LinkDetector
{
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

    public static bool IsFolder(string text)
    {
        var t = text.Trim();
        return (t.Length >= 3 && char.IsLetter(t[0]) && t[1] == ':' && t[2] == '\\')
            || t.StartsWith("\\\\");
    }
}
