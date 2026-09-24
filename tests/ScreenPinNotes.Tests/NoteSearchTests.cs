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
