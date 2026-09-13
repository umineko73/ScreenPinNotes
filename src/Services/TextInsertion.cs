// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace ScreenPinNotes.Services;

public static class TextInsertion
{
    public static TextInsertionResult InsertAtSelection(
        string text,
        int selectionStart,
        int selectionLength,
        string insertText)
    {
        selectionStart = Math.Clamp(selectionStart, 0, text.Length);
        selectionLength = Math.Clamp(selectionLength, 0, text.Length - selectionStart);
        var result = text.Remove(selectionStart, selectionLength).Insert(selectionStart, insertText);
        return new TextInsertionResult(result, selectionStart + insertText.Length);
    }

    public static string BuildBlockInsertion(
        string text,
        int selectionStart,
        int selectionLength,
        string blockText)
    {
        selectionStart = Math.Clamp(selectionStart, 0, text.Length);
        selectionLength = Math.Clamp(selectionLength, 0, text.Length - selectionStart);
        var selectionEnd = selectionStart + selectionLength;
        var prefix = selectionStart > 0 && text[selectionStart - 1] != '\n' ? "\n" : "";
        var suffix = selectionEnd < text.Length && text[selectionEnd] != '\n' ? "\n" : "";
        return $"{prefix}{blockText}{suffix}";
    }

    /// <summary><paramref name="index"/> を含む行の末尾（改行の手前）の位置。</summary>
    public static int GetLineEnd(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        var end = text.IndexOfAny(['\r', '\n'], index);
        return end < 0 ? text.Length : end;
    }
}

public sealed record TextInsertionResult(string Text, int CaretIndex);
