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

using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class LogLevelHighlighterTests
{
    [Theory]
    [InlineData("2026-09-15 09:12:01 [INFO] started", "INFO", LogLevelHighlighter.Severity.Info)]
    [InlineData("2026-09-15 09:12:01 [DEBUG] cache warmed", "DEBUG", LogLevelHighlighter.Severity.Info)]
    [InlineData("2026-09-15 09:12:01 [TRACE] heartbeat", "TRACE", LogLevelHighlighter.Severity.Info)]
    [InlineData("2026-09-15 09:12:01 [WARN] slow", "WARN", LogLevelHighlighter.Severity.Warning)]
    [InlineData("2026-09-15 09:12:01 WARNING: slow", "WARNING", LogLevelHighlighter.Severity.Warning)]
    [InlineData("2026-09-15 09:12:01 [ERROR] boom", "ERROR", LogLevelHighlighter.Severity.Error)]
    [InlineData("2026-09-15 09:12:01 [FATAL] boom", "FATAL", LogLevelHighlighter.Severity.Error)]
    [InlineData("09:12:01 error connecting", "error", LogLevelHighlighter.Severity.Error)]
    public void FindFirst_LocatesTheLevelWord(string line, string expected, LogLevelHighlighter.Severity severity)
    {
        var match = Assert.NotNull(LogLevelHighlighter.FindFirst(line));
        Assert.Equal(expected, line.Substring(match.Start, match.Length));
        Assert.Equal(severity, match.Severity);
    }

    [Theory]
    [InlineData("no level here")]
    [InlineData("more information about the run")] // INFO must not match inside a word
    [InlineData("2026-09-15 09:12:01 warnings are suppressed")]
    [InlineData("")]
    public void FindFirst_IgnoresLinesWithoutALevelWord(string line)
        => Assert.Null(LogLevelHighlighter.FindFirst(line));

    [Fact]
    public void FindFirst_TakesOnlyTheFirstLevelOnTheLine()
    {
        const string line = "09:12:01 [INFO] retry after the previous error";

        var match = Assert.NotNull(LogLevelHighlighter.FindFirst(line));

        Assert.Equal("INFO", line.Substring(match.Start, match.Length));
    }

    [Fact]
    public void ContainsError_OnlyCountsLinesWhoseLevelIsAnError()
    {
        Assert.True(LogLevelHighlighter.ContainsError("[INFO] ok\n[ERROR] boom\n"));
        Assert.True(LogLevelHighlighter.ContainsError("[FATAL] boom"));
        Assert.False(LogLevelHighlighter.ContainsError("[INFO] ok\n[WARN] slow\n"));
        // 行のレベルは INFO なので、あとに続く語では赤くしない。
        Assert.False(LogLevelHighlighter.ContainsError("[INFO] recovered from an error"));
    }

    [Fact]
    public void GetAppendedText_ReturnsWhatGrewAtTheEnd()
    {
        Assert.Equal("line 3\n", LogLevelHighlighter.GetAppendedText("line 1\nline 2\n", "line 1\nline 2\nline 3\n"));
        Assert.Equal("everything", LogLevelHighlighter.GetAppendedText("", "everything"));
    }

    [Fact]
    public void GetAppendedText_HandlesTheTailWindowSlidingForward()
    {
        // tail は行数を保つので、増えた分だけ先頭が押し出される。
        var previous = "line 1\nline 2\nline 3";
        var current = "line 2\nline 3\nline 4";

        Assert.Equal("line 4", LogLevelHighlighter.GetAppendedText(previous, current));
    }

    [Fact]
    public void GetAppendedText_WhenNothingLinesUp_TreatsEverythingAsNew()
    {
        // ログのローテーションなどで中身が入れ替わった場合。
        Assert.Equal("fresh file", LogLevelHighlighter.GetAppendedText("old content", "fresh file"));
    }
}
