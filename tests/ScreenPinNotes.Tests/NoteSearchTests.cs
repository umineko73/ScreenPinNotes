using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class NoteSearchTests
{
    [Theory]
    [InlineData("a*b", NoteSearchMode.Plain, "A*B", true)]
    [InlineData("a*b", NoteSearchMode.Plain, "axb", false)]
    [InlineData("a*b?", NoteSearchMode.Wildcard, "prefix A\nB1 suffix", true)]
    [InlineData("[a]", NoteSearchMode.Wildcard, "[A]", true)]
    [InlineData("^task[0-9]+$", NoteSearchMode.Regex, "Task42", true)]
    [InlineData("^task[0-9]+$", NoteSearchMode.Regex, "Task", false)]
    public void MatchesExpectedText(string query, NoteSearchMode mode, string text, bool expected)
        => Assert.Equal(expected, NoteSearch.CreateMatcher(query, mode)(text));

    [Fact]
    public void InvalidRegexIsRejected()
        => Assert.ThrowsAny<ArgumentException>(() => NoteSearch.CreateMatcher("[", NoteSearchMode.Regex));
}
