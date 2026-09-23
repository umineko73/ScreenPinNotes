namespace ScreenPinNotes.Services;

public readonly record struct MarkdownSourceSpan(int Line, int Start, int Length);
public sealed record MarkdownImageSyntax(string Alt, string Target, MarkdownSourceSpan Source, double? Width, double? Height);
public sealed record MarkdownLinkSyntax(string Label, string Target, MarkdownSourceSpan Source);
public sealed record MarkdownTaskSyntax(bool IsChecked, MarkdownSourceSpan Source);

/// <summary>Source-preserving Markdown recognition, independent of WPF presentation.</summary>
public static class MarkdownSyntax
{
    public static MarkdownImageSyntax? ParseImage(string text, int start, int line = 0, int lineOffset = 0)
        => start >= 0 && start < text.Length &&
            TryGetMarkdownImage(text, start, out var alt, out var target, out var length, out var width, out var height)
            ? new(alt, target, new(line, lineOffset + start, length), width, height) : null;

    public static MarkdownLinkSyntax? ParseLink(string text, int start, int line = 0, int lineOffset = 0)
        => start >= 0 && start < text.Length &&
            TryGetMarkdownLink(text, start, out var label, out var target, out var length)
            ? new(label, target, new(line, lineOffset + start, length)) : null;

    public static MarkdownTaskSyntax? ParseTaskMarker(string text, int line, int lineOffset)
        => text.Length >= 4 && text[0] == '[' && text[2] == ']' && text[3] == ' ' && text[1] is ' ' or 'x' or 'X'
            ? new(text[1] is 'x' or 'X', new(line, lineOffset, 4)) : null;

    public static string? GetImageOnlyTarget(string text)
    {
        var source = text.Trim();
        var image = ParseImage(source, 0);
        return image?.Source.Length == source.Length ? image.Target : null;
    }

    internal static bool TryGetMarkdownLink(
        string text,
        int start,
        out string label,
        out string target,
        out int length)
    {
        label = "";
        target = "";
        length = 0;

        if (text[start] != '[') return false;

        var labelEnd = FindUnescaped(text, "]", start + 1);
        if (labelEnd <= start || labelEnd + 1 >= text.Length || text[labelEnd + 1] != '(')
            return false;

        if (!TryReadMarkdownTarget(text, labelEnd + 1, out target, out var targetLength))
            return false;

        label = UnescapeMarkdownText(text[(start + 1)..labelEnd]);
        length = labelEnd + 1 + targetLength - start;
        return LinkDetector.IsLink(target);
    }

    internal static bool TryGetWikiImage(string text, int start, out string target, out int length, out double? width, out double? height)
    {
        target = ""; length = 0; width = null; height = null;
        if (!text.AsSpan(start).StartsWith("![[", StringComparison.Ordinal)) return false;
        var end = text.IndexOf("]]", start + 3, StringComparison.Ordinal);
        if (end < 0) return false;
        var parts = text[(start + 3)..end].Split('|', 2);
        target = parts[0].Trim();
        if (!LinkDetector.IsRenderableImageTarget(target)) return false;
        if (parts.Length == 2)
        {
            var dimensions = parts[1].Split('x', 2);
            static double? Dimension(string value) => double.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0 && double.IsFinite(number) ? number : null;
            width = Dimension(dimensions[0]);
            if (dimensions.Length == 2) height = Dimension(dimensions[1]);
        }
        length = end + 2 - start;
        return true;
    }

    internal static bool TryGetMarkdownImage(
        string text,
        int start,
        out string alt,
        out string target,
        out int length,
        out double? width,
        out double? height)
    {
        alt = "";
        target = "";
        length = 0;
        width = null;
        height = null;

        if (TryGetWikiImage(text, start, out target, out length, out width, out height))
        { alt = target; return true; }

        if (start + 1 >= text.Length || text[start] != '!' || text[start + 1] != '[')
            return false;

        var altEnd = text.IndexOf(']', start + 2);
        if (altEnd <= start + 1 || altEnd + 1 >= text.Length || text[altEnd + 1] != '(')
            return false;

        if (!TryReadMarkdownTarget(text, altEnd + 1, out target, out var targetLength))
            return false;

        alt = text[(start + 2)..altEnd];
        length = altEnd + 1 + targetLength - start;
        while (TryReadImageAttributes(text, start + length, out var attrLength, out var attrWidth, out var attrHeight))
        {
            length += attrLength;
            if (attrWidth.HasValue)
                width = attrWidth;
            if (attrHeight.HasValue)
                height = attrHeight;
        }

        return target.Length > 0;
    }

    internal static bool TryReadMarkdownTarget(
        string text,
        int openParenIndex,
        out string target,
        out int length)
    {
        target = "";
        length = 0;
        if (openParenIndex >= text.Length || text[openParenIndex] != '(')
            return false;

        var start = openParenIndex + 1;
        if (start < text.Length && text[start] == '<')
        {
            var closeAngleIndex = text.IndexOf('>', start + 1);
            if (closeAngleIndex > start + 1 &&
                closeAngleIndex + 1 < text.Length &&
                text[closeAngleIndex + 1] == ')')
            {
                // <...> で囲まれたリンク先は、括弧やバックスラッシュを
                // Markdown エスケープとして扱わず、そのままURLとして渡す。
                // 前後の空白だけは落とす（付けたまま渡すと起動に失敗する）。
                var angleTarget = text[(start + 1)..closeAngleIndex].Trim();
                if (angleTarget.Length > 0)
                {
                    target = angleTarget;
                    length = closeAngleIndex + 2 - openParenIndex;
                    return true;
                }
            }
        }

        var depth = 0;
        var fallbackEnd = -1;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\\' && i + 1 < text.Length)
            {
                i++;
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }

            if (ch != ')')
                continue;

            if (fallbackEnd < 0)
                fallbackEnd = i; // 深さが0に戻らない不正な入力では、最初の ')' を採用する
            if (depth > 0)
            {
                depth--;
                continue;
            }

            return FinishMarkdownTarget(text, openParenIndex, i, out target, out length);
        }

        return fallbackEnd > start &&
               FinishMarkdownTarget(text, openParenIndex, fallbackEnd, out target, out length);
    }

    internal static bool FinishMarkdownTarget(
        string text,
        int openParenIndex,
        int closeParenIndex,
        out string target,
        out int length)
    {
        target = UnescapeMarkdownText(StripOptionalMarkdownTitle(text[(openParenIndex + 1)..closeParenIndex]));
        length = closeParenIndex - openParenIndex + 1;
        return target.Length > 0;
    }

    internal static string StripOptionalMarkdownTitle(string rawTarget)
    {
        var value = rawTarget.Trim();
        if (value.StartsWith("<", StringComparison.Ordinal) && value.IndexOf('>') is var angleEnd && angleEnd > 1)
            return value[1..angleEnd].Trim();

        var firstWhitespace = IndexOfWhitespace(value);
        if (firstWhitespace < 0)
            return value;

        var possibleTitle = value[firstWhitespace..].TrimStart();
        if (possibleTitle.Length >= 2 &&
            (possibleTitle[0] == '"' && possibleTitle[^1] == '"' ||
             possibleTitle[0] == '\'' && possibleTitle[^1] == '\'' ||
             possibleTitle[0] == '(' && possibleTitle[^1] == ')'))
        {
            return value[..firstWhitespace].Trim();
        }

        return value;
    }

    internal static string UnescapeMarkdownText(string text)
    {
        var result = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && IsEscapableMarkdownChar(text[i + 1]))
            {
                result.Append(text[++i]);
                continue;
            }

            result.Append(text[i]);
        }

        return result.ToString();
    }

    internal static int IndexOfWhitespace(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                return i;
        }

        return -1;
    }

    internal static bool TryGetAutolink(string text, int start, out string target, out int length)
    {
        target = "";
        length = 0;
        if (text[start] != '<')
            return false;

        var end = text.IndexOf('>', start + 1);
        if (end <= start + 1)
            return false;

        target = text[(start + 1)..end].Trim();
        length = end - start + 1;
        return LinkDetector.IsExactLink(target);
    }

    internal static bool TryReadImageAttributes(
        string text,
        int start,
        out int length,
        out double? width,
        out double? height)
    {
        length = 0;
        width = null;
        height = null;

        if (start >= text.Length || text[start] != '{')
            return false;

        var end = text.IndexOf('}', start + 1);
        if (end <= start + 1)
            return false;

        foreach (var part in text[(start + 1)..end].Split([' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 ||
                !double.TryParse(pair[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ||
                value < 0)
            {
                continue;
            }

            if (string.Equals(pair[0], "width", StringComparison.OrdinalIgnoreCase))
                width = value;
            else if (string.Equals(pair[0], "height", StringComparison.OrdinalIgnoreCase))
                height = value;
        }

        length = end - start + 1;
        return true;
    }

    internal static int FindUnescaped(string text, string marker, int start)
    {
        for (var i = start; i <= text.Length - marker.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i++;
                continue;
            }

            if (text.AsSpan(i).StartsWith(marker, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    internal static bool IsEscapableMarkdownChar(char ch)
        => ch is '\\' or '`' or '*' or '_' or '{' or '}' or '[' or ']' or '(' or ')' or '#' or '+' or '-' or '.' or '!' or '|' or '<' or '>' or '~' or '=';

}
