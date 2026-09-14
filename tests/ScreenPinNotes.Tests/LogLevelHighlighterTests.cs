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
    // Serilog の3文字略記（{Level:u3}）。
    [InlineData("09:12:01 [VRB] entering", "VRB", LogLevelHighlighter.Severity.Info)]
    [InlineData("09:12:01 [DBG] cached", "DBG", LogLevelHighlighter.Severity.Info)]
    [InlineData("09:12:01 [INF] started", "INF", LogLevelHighlighter.Severity.Info)]
    [InlineData("09:12:01 [WRN] slow", "WRN", LogLevelHighlighter.Severity.Warning)]
    [InlineData("09:12:01 [ERR] boom", "ERR", LogLevelHighlighter.Severity.Error)]
    [InlineData("09:12:01 [FTL] gone", "FTL", LogLevelHighlighter.Severity.Error)]
    // Microsoft.Extensions.Logging のコンソール。
    [InlineData("trce: Worker[0]", "trce", LogLevelHighlighter.Severity.Info)]
    [InlineData("dbug: Worker[0]", "dbug", LogLevelHighlighter.Severity.Info)]
    [InlineData("fail: Worker[0]", "fail", LogLevelHighlighter.Severity.Error)]
    [InlineData("crit: Worker[0]", "crit", LogLevelHighlighter.Severity.Error)]
    // Serilog の既定の {Level}、java.util.logging、Go、syslog。
    [InlineData("09:12:01 [Information] started", "Information", LogLevelHighlighter.Severity.Info)]
    [InlineData("09:12:01 [Verbose] entering", "Verbose", LogLevelHighlighter.Severity.Info)]
    [InlineData("09:12:01 SEVERE  disk full", "SEVERE", LogLevelHighlighter.Severity.Error)]
    [InlineData("09:12:01 panic: runtime error", "panic", LogLevelHighlighter.Severity.Error)]
    [InlineData("09:12:01 NOTICE reloaded", "NOTICE", LogLevelHighlighter.Severity.Info)]
    [InlineData("09:12:01 ALERT disk failing", "ALERT", LogLevelHighlighter.Severity.Error)]
    [InlineData("09:12:01 EMERG halted", "EMERG", LogLevelHighlighter.Severity.Error)]
    [InlineData("09:12:01 CRITICAL halted", "CRITICAL", LogLevelHighlighter.Severity.Error)]
    public void FindFirst_LocatesTheLevelWord(string line, string expected, LogLevelHighlighter.Severity severity)
    {
        var match = Assert.NotNull(LogLevelHighlighter.FindFirst(line));
        Assert.Equal(expected, line.Substring(match.Start, match.Length));
        Assert.Equal(severity, match.Severity);
    }

    // 表記のゆれを広く拾うぶん、語の一部で一致してしまわないことを押さえておく。
    [Theory]
    [InlineData("no level here")]
    [InlineData("informational text only")]
    [InlineData("2026-09-15 09:12:01 warnings are suppressed")]
    [InlineData("errors were resolved")]
    [InlineData("failed to resolve host")]
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
    public void FindHighlights_ReturnsTheLevelAndEveryNumberInOrder()
    {
        const string line = "2026-09-15 09:12:03.221 [DEBUG] cache warmed in 42 ms";

        var spans = LogLevelHighlighter.FindHighlights(line);

        Assert.Equal(
            ["2026-09-15", "09:12:03.221", "DEBUG", "42"],
            spans.Select(s => line.Substring(s.Start, s.Length)));
        Assert.Equal(
            [
                LogLevelHighlighter.SpanKind.Number, LogLevelHighlighter.SpanKind.Number,
                LogLevelHighlighter.SpanKind.Level, LogLevelHighlighter.SpanKind.Number,
            ],
            spans.Select(s => s.Kind));
    }

    // 日時は区切りで切らずひとつながりに扱う。細切れの色より読みやすく、
    // 描く要素も減る。
    [Theory]
    [InlineData("at 2026-09-15", "2026-09-15")]
    [InlineData("at 09:12:03.221", "09:12:03.221")]
    [InlineData("pool (16/16)", "16/16")]
    [InlineData("count 1,234 rows", "1,234")]
    [InlineData("took 42 ms", "42")]
    public void FindHighlights_KeepsAConnectedNumberTogether(string line, string expected)
    {
        var span = Assert.Single(LogLevelHighlighter.FindHighlights(line));

        Assert.Equal(LogLevelHighlighter.SpanKind.Number, span.Kind);
        Assert.Equal(expected, line.Substring(span.Start, span.Length));
    }

    [Fact]
    public void FindHighlights_DoesNotSwallowSeparatorsThatEndAValue()
    {
        // 末尾の記号や、数字が続かない区切りは値に含めない。
        const string line = "finished in 42. next";

        var span = Assert.Single(LogLevelHighlighter.FindHighlights(line));

        Assert.Equal("42", line.Substring(span.Start, span.Length));
    }

    [Fact]
    public void FindHighlights_OnALineWithoutNumbersOrLevels_ReturnsNothing()
        => Assert.Empty(LogLevelHighlighter.FindHighlights("just a plain sentence"));

    [Fact]
    public void ContainsError_OnlyCountsLinesWhoseLevelIsAnError()
    {
        Assert.True(LogLevelHighlighter.ContainsError("[INFO] ok\n[ERROR] boom\n"));
        Assert.True(LogLevelHighlighter.ContainsError("[FATAL] boom"));
        // 略記や別系統の語でも、赤く知らせる対象になる。
        Assert.True(LogLevelHighlighter.ContainsError("09:12:01 [ERR] boom"));
        Assert.True(LogLevelHighlighter.ContainsError("crit: Worker[0]"));
        Assert.True(LogLevelHighlighter.ContainsError("fail: Worker[0]"));
        Assert.True(LogLevelHighlighter.ContainsError("09:12:01 panic: runtime error"));
        Assert.False(LogLevelHighlighter.ContainsError("09:12:01 [WRN] slow"));
        Assert.False(LogLevelHighlighter.ContainsError("09:12:01 [INF] ok"));
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
