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

public class MarkdownIndentationTests
{
    private static string Apply(string text, MarkdownIndentation.Edit edit)
        => text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);

    [Theory]
    [InlineData("- item", 3)]
    [InlineData("1. item", 0)]
    [InlineData("  2) item", 9)]
    public void Indent_OnAListLine_IndentsTheWholeLineAndKeepsTheCaretOnItsText(string line, int caret)
    {
        var text = "intro\n" + line + "\nafter";

        var edit = MarkdownIndentation.Indent(text, 6 + caret, 0)!.Value;

        Assert.Equal("intro\n\t" + line + "\nafter", Apply(text, edit));
        Assert.Equal(6 + caret + 1, edit.SelectionStart);
        Assert.Equal(0, edit.SelectionLength);
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("-not a list")]
    public void Indent_OnAnOrdinaryLine_LeavesTabInsertionToTheEditor(string text)
        => Assert.Null(MarkdownIndentation.Indent(text, 3, 0));

    [Fact]
    public void Indent_AcrossLines_IndentsEveryNonEmptyLineAndSelectsThem()
    {
        var text = "top\nfirst\n\nsecond\r\nlast";
        var start = text.IndexOf("irst", StringComparison.Ordinal);
        var end = text.IndexOf("cond", StringComparison.Ordinal);

        var edit = MarkdownIndentation.Indent(text, start, end - start)!.Value;
        var result = Apply(text, edit);

        Assert.Equal("top\n\tfirst\n\n\tsecond\r\nlast", result);
        Assert.Equal("\tfirst\n\n\tsecond", result.Substring(edit.SelectionStart, edit.SelectionLength));
    }

    [Fact]
    public void Indent_SelectionEndingAtTheNextLineStart_LeavesThatLineAlone()
    {
        var text = "one\ntwo\nthree";

        var edit = MarkdownIndentation.Indent(text, 0, "one\ntwo\n".Length)!.Value;

        Assert.Equal("\tone\n\ttwo\nthree", Apply(text, edit));
    }

    [Theory]
    [InlineData("\t- item", "- item")]
    [InlineData("      - item", "  - item")]
    [InlineData("  - item", "- item")]
    [InlineData("　全角", "全角")]
    public void Outdent_RemovesOneLevel(string line, string expected)
    {
        var edit = MarkdownIndentation.Outdent(line, line.Length, 0)!.Value;

        Assert.Equal(expected, Apply(line, edit));
        Assert.Equal(expected.Length, edit.SelectionStart);
    }

    [Fact]
    public void Outdent_KeepsTheCaretOnTheLineWhenItWasInsideTheIndent()
    {
        var text = "a\n\t\tb";

        var edit = MarkdownIndentation.Outdent(text, 3, 0)!.Value;

        Assert.Equal("a\n\tb", Apply(text, edit));
        Assert.Equal(2, edit.SelectionStart);
    }

    [Fact]
    public void Outdent_AcrossLines_OutdentsEachLine()
    {
        var text = "\t- a\n- b\n    - c";

        var edit = MarkdownIndentation.Outdent(text, 1, text.Length - 1)!.Value;

        Assert.Equal("- a\n- b\n- c", Apply(text, edit));
    }

    [Fact]
    public void Outdent_WithNothingToRemove_ReturnsNull()
        => Assert.Null(MarkdownIndentation.Outdent("- item", 2, 0));
}
