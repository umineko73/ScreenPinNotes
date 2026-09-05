namespace ScreenPinNotes.Services;

public static class MarkdownLinkEditor
{
    public sealed record Link(int Start, int Length, string Label, string Target);

    public static Link? FindAt(string text, int caret)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] != '[' || (i > 0 && text[i - 1] == '!')) continue;
            if (!MarkdownRenderer.TryGetMarkdownLink(text, i, out var label, out var target, out var length)) continue;
            if (caret >= i && caret <= i + length)
                return new Link(i, length, label, target);
            i += length - 1;
        }
        return null;
    }
}
