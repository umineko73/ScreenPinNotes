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
}
