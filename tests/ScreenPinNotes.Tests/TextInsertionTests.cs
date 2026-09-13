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

public class TextInsertionTests
{
    [Fact]
    public void InsertAtSelection_InsertsTextAtCaretAndReturnsCaretAfterInsertedText()
    {
        var result = TextInsertion.InsertAtSelection("abef", 2, 0, "cd");

        Assert.Equal("abcdef", result.Text);
        Assert.Equal(4, result.CaretIndex);
    }

    [Fact]
    public void InsertAtSelection_ReplacesSelectedText()
    {
        var result = TextInsertion.InsertAtSelection("abXYef", 2, 2, "cd");

        Assert.Equal("abcdef", result.Text);
        Assert.Equal(4, result.CaretIndex);
    }

    [Theory]
    [InlineData("beforeafter", 6, 0, "before\nBLOCK\nafter")]
    [InlineData("before\nafter", 7, 0, "before\nBLOCK\nafter")]
    [InlineData("before\n\nafter", 7, 0, "before\nBLOCK\nafter")]
    [InlineData("before", 6, 0, "before\nBLOCK")]
    [InlineData("after", 0, 0, "BLOCK\nafter")]
    public void BuildBlockInsertion_AddsOnlyNeededLineBreaks(
        string text,
        int selectionStart,
        int selectionLength,
        string expected)
    {
        var insertion = TextInsertion.BuildBlockInsertion(text, selectionStart, selectionLength, "BLOCK");
        var result = TextInsertion.InsertAtSelection(text, selectionStart, selectionLength, insertion);

        Assert.Equal(expected, result.Text);
    }

    // ドロップした行の途中で分けず、その行の直後に画像を入れるための位置。
    [Theory]
    [InlineData("first\nsecond", 2, 5)]
    [InlineData("first\nsecond", 5, 5)]
    [InlineData("first\nsecond", 6, 12)]
    [InlineData("first\r\nsecond", 1, 5)]
    [InlineData("first", 99, 5)]
    [InlineData("", 0, 0)]
    public void GetLineEnd_ReturnsTheEndOfTheLineHoldingTheIndex(string text, int index, int expected)
        => Assert.Equal(expected, TextInsertion.GetLineEnd(text, index));
}
