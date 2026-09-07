using System.Text.RegularExpressions;

namespace ScreenPinNotes.Services;

public enum NoteSearchMode { Plain, Wildcard, Regex }

public static class NoteSearch
{
    public static Func<string, bool> CreateMatcher(string query, NoteSearchMode mode)
    {
        if (mode == NoteSearchMode.Plain)
            return text => text.Contains(query, StringComparison.OrdinalIgnoreCase);
        var pattern = mode == NoteSearchMode.Wildcard
            ? Regex.Escape(query).Replace("\\*", "[\\s\\S]*").Replace("\\?", "[\\s\\S]")
            : query;
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        return regex.IsMatch;
    }
}
