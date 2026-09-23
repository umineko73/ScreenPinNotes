using static ScreenPinNotes.Services.MarkdownSyntax;
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

using System.Windows;
using System.Windows.Documents;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfSolidBrush = System.Windows.Media.SolidColorBrush;

namespace ScreenPinNotes.Services;

public static class MarkdownRenderer
{
    private static readonly WpfFontFamily CodeFontFamily = new("Consolas");

    public static string? GetImageOnlyTarget(string text) => MarkdownSyntax.GetImageOnlyTarget(text);

    public sealed record MarkdownImage(
        string Alt,
        string Target,
        int LineIndex,
        int Start,
        int Length,
        double? Width,
        double? Height);

    public static IEnumerable<Block> Render(
        string text,
        double baseFontSize,
        Func<string, string, Hyperlink> createHyperlink,
        Func<MarkdownImage, Inline>? createImage = null,
        Func<int, bool, WpfCheckBox>? createTaskCheckbox = null,
        bool darkMode = false,
        bool ignoreFirstLineHeadingSize = false,
        string language = "en",
        bool propertiesCollapsed = false,
        Action<bool>? propertiesCollapsedChanged = null,
        string? referenceSource = null,
        bool joinLines = false)
    {
        // Bound UI element creation and parser work without discarding source text.
        if (text.Length > 131072 || text.Count(ch => ch == '\n') > 2000)
        {
            yield return new Paragraph(new Run(text));
            yield break;
        }
        var lines = NormalizeLines(text);
        if (lines.Length == 0)
        {
            yield return CreateParagraph();
            yield break;
        }

        var startLine = 0;
        if (MarkdownProperties.TryRender(lines, darkMode, out var properties, out var afterProperties, language,
            propertiesCollapsed, propertiesCollapsedChanged))
        {
            yield return properties;
            startLine = afterProperties;
        }

        // [表示名][1] が参照する "[1]: URL" 定義行は表示しない。折りたたみ表示のように本文の
        // 一部だけを描くときは、referenceSource に本文全体を渡すと定義をそこから拾う。
        var references = CollectReferenceDefinitions(lines, out var definitionLines);
        if (referenceSource != null)
        {
            foreach (var (label, target) in CollectReferenceDefinitions(NormalizeLines(referenceSource), out _))
                references.TryAdd(label, target);
        }

        // 末尾にまとめて置かれた定義行と、その手前の空行は描かない（空の段落が残るだけになる）。
        var renderEnd = lines.Length;
        var tailHasDefinition = false;
        for (var j = lines.Length - 1; j >= 0 && (lines[j].Trim().Length == 0 || definitionLines.Contains(j)); j--)
        {
            tailHasDefinition |= definitionLines.Contains(j);
            if (tailHasDefinition)
                renderEnd = j;
        }

        var source = new SourceLine[lines.Length];
        for (var n = 0; n < lines.Length; n++)
            source[n] = new SourceLine(lines[n], n, 0);
        var context = new RenderContext(baseFontSize, createHyperlink, createImage, createTaskCheckbox,
            darkMode, ignoreFirstLineHeadingSize, references, definitionLines, joinLines);
        foreach (var block in RenderBlocks(source, startLine, renderEnd, context, quoteDepth: 0))
            yield return block;
    }

    /// <summary>
    /// 描く1行。引用の中身は先頭の ">" を外して描くので、元の本文の何行目・何文字目に
    /// あたるかを持ち歩く（チェックボックスや画像は元の本文を書き換えるときにこれを使う）。
    /// </summary>
    private readonly record struct SourceLine(string Text, int Index, int Offset);

    private sealed record RenderContext(
        double BaseFontSize,
        Func<string, string, Hyperlink> CreateHyperlink,
        Func<MarkdownImage, Inline>? CreateImage,
        Func<int, bool, WpfCheckBox>? CreateTaskCheckbox,
        bool DarkMode,
        bool IgnoreFirstLineHeadingSize,
        IReadOnlyDictionary<string, string> References,
        HashSet<int> DefinitionLines,
        bool JoinLines);

    /// <summary>
    /// 引用・リストの入れ子の上限。これより深い印は入れ子にしない（">" を何千も並べた行で
    /// 再帰が深くなりすぎ、スタックを使い果たさないように）。
    /// </summary>
    private const int MaxNestingDepth = 8;

    private static IEnumerable<Block> RenderBlocks(SourceLine[] lines, int start, int end, RenderContext context, int quoteDepth)
    {
        var darkMode = context.DarkMode;
        var texts = Array.ConvertAll(lines, l => l.Text);
        for (int i = start; i < end;)
        {
            var line = lines[i];
            var trimmed = line.Text.Trim();

            if (IsFence(line.Text))
            {
                i++;
                var codeLines = new List<string>();
                while (i < lines.Length && !IsFence(lines[i].Text))
                    codeLines.Add(lines[i++].Text);
                if (i < lines.Length) i++;

                yield return CreateCodeBlock(string.Join("\n", codeLines), darkMode);
                continue;
            }

            if (context.DefinitionLines.Contains(line.Index))
            {
                i++;
                continue;
            }

            if (trimmed.Length == 0)
            {
                yield return CreateParagraph();
                i++;
                continue;
            }

            if (TryParseTable(texts, i, context.CreateHyperlink, context.CreateImage,
                    context.References, darkMode, out var table, out var nextIndex))
            {
                yield return table;
                i = nextIndex;
                continue;
            }

            if (TryGetHeading(line.Text, out var level, out var headingText))
            {
                var para = CreateParagraph();
                para.FontWeight = FontWeights.Bold;
                // タイトルバーを隠して畳んだ1行表示では、見出しの拡大を無視してタイトル文字サイズに揃える。
                para.FontSize = context.IgnoreFirstLineHeadingSize && line.Index == 0
                    ? context.BaseFontSize
                    : HeadingFontSize(context.BaseFontSize, level);
                AddInlineContent(para.Inlines, headingText, line.Index, line.Offset + level + 1, context);
                yield return para;
                i++;
                continue;
            }

            if (IsHorizontalRule(trimmed))
            {
                yield return CreateHorizontalRule(darkMode);
                i++;
                continue;
            }

            if (TryGetListItem(line.Text, out ListItemMarker _))
            {
                var (list, next) = RenderList(lines, i, 0, context);
                yield return list;
                i = next;
                continue;
            }

            if (quoteDepth < MaxNestingDepth && TryStripQuote(line, out _))
            {
                // 続く引用行を1つのまとまりとして、">" を1段外した中身を描く。中身も Markdown として
                // 描くので、">>" の入れ子や引用内のリスト・見出しもそのまま描ける。
                var inner = new List<SourceLine>();
                while (i < lines.Length && !context.DefinitionLines.Contains(lines[i].Index) &&
                       TryStripQuote(lines[i], out var stripped))
                {
                    inner.Add(stripped);
                    i++;
                }
                yield return CreateQuote(RenderBlocks([.. inner], 0, inner.Count, context, quoteDepth + 1), darkMode);
                continue;
            }

            var paragraph = CreateParagraph();
            // 行頭の字下げは余白にする。文字として残すと、折り返した2行目が行頭へ戻ってしまう。
            var indent = GetIndentWidth(line.Text);
            if (indent > 0)
                paragraph.Margin = new Thickness(IndentWidth(indent, context.BaseFontSize), 0, 0, 0);

            // 設定で有効なときは、続けて書いた行を同じ段落として折り返す。
            var last = i + 1;
            if (context.JoinLines)
            {
                while (last < end && GetIndentWidth(lines[last].Text) == indent &&
                       !StartsBlock(lines, texts, last, context, quoteDepth))
                    last++;
            }

            for (var k = i; k < last; k++)
            {
                var body = lines[k].Text.TrimStart(IndentChars);
                var bodyStart = lines[k].Offset + lines[k].Text.Length - body.Length;
                string? next = k + 1 < last ? lines[k + 1].Text.TrimStart(IndentChars) : null;
                // 行末のスペース2つか "\" は、そこで改行する印（一般的な Markdown と同じ）。
                var hardBreak = next != null && (body.EndsWith("  ", StringComparison.Ordinal) || body.EndsWith('\\'));
                if (next != null)
                    body = body.EndsWith('\\') ? body[..^1] : body.TrimEnd();
                AddInlineContent(paragraph.Inlines, body, lines[k].Index, bodyStart, context);
                if (next == null)
                    continue;
                if (hardBreak)
                    paragraph.Inlines.Add(new LineBreak());
                else if (body.Length > 0 && next.Length > 0 && !IsCjk(body[^1]) && !IsCjk(next[0]))
                    paragraph.Inlines.Add(new Run(" "));
            }
            yield return paragraph;
            i = last;
        }
    }

    /// <summary>その行から段落以外のまとまり（または空行）が始まるか。段落をつなげる範囲を決めるのに使う。</summary>
    private static bool StartsBlock(SourceLine[] lines, string[] texts, int index, RenderContext context, int quoteDepth)
    {
        var text = lines[index].Text;
        var trimmed = text.Trim();
        return trimmed.Length == 0 ||
               IsFence(text) ||
               context.DefinitionLines.Contains(lines[index].Index) ||
               IsTableStart(texts, index) ||
               TryGetHeading(text, out _, out _) ||
               IsHorizontalRule(trimmed) ||
               TryGetListItem(text, out ListItemMarker _) ||
               (quoteDepth < MaxNestingDepth && IsQuote(text));
    }

    private static bool IsTableStart(string[] lines, int start)
        => start + 1 < lines.Length &&
           TrySplitTableRow(lines[start], out var headers) &&
           TrySplitTableRow(lines[start + 1], out var separator) &&
           headers.Count > 0 && separator.Count == headers.Count &&
           TryGetTableAlignments(separator, out _);

    /// <summary>
    /// 日本語・中国語の文字。これらの間で行をつなぐときは空白を入れない
    /// （英語のように単語の間にスペースを置く書き方ではないため）。
    /// </summary>
    private static bool IsCjk(char ch)
        => ch is (>= '⺀' and <= '鿿') or (>= '豈' and <= '﫿') or (>= '＀' and <= '￯');

    private static readonly char[] IndentChars = [' ', '\t', '\u3000'];

    /// <summary>字下げ1桁ぶんの幅。タブ（4桁）でおよそ全角2文字になる。</summary>
    private static double IndentWidth(int columns, double baseFontSize)
        => columns * baseFontSize * 0.5;

    private static Section CreateQuote(IEnumerable<Block> blocks, bool darkMode)
    {
        var section = new Section
        {
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(8, 0, 0, 0),
            BorderBrush = GetBorderBrush(darkMode),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Foreground = darkMode ? WpfBrushes.LightGray : WpfBrushes.DimGray,
        };
        foreach (var block in blocks)
            section.Blocks.Add(block);
        return section;
    }

    /// <summary>
    /// <paramref name="start"/> の項目から始まるリストを描く。字下げの深い項目は、
    /// 直前の項目の中に入れ子のリストとして描く。字下げが浅い項目・種類の違う項目の手前で終わる。
    /// </summary>
    private static (System.Windows.Documents.List List, int Next) RenderList(
        SourceLine[] lines, int start, int depth, RenderContext context)
    {
        TryGetListItem(lines[start].Text, out ListItemMarker first);
        var list = new System.Windows.Documents.List
        {
            MarkerStyle = first.TaskState.HasValue
                ? TextMarkerStyle.None
                : first.Ordered
                    ? TextMarkerStyle.Decimal
                    : (depth % 3) switch { 0 => TextMarkerStyle.Disc, 1 => TextMarkerStyle.Circle, _ => TextMarkerStyle.Square },
            Margin = new Thickness(18, 0, 0, 0),
            Padding = new Thickness(0),
        };
        if (first.Ordered)
            list.StartIndex = Math.Max(1, first.Number);

        var i = start;
        while (i < lines.Length &&
               !context.DefinitionLines.Contains(lines[i].Index) &&
               TryGetListItem(lines[i].Text, out ListItemMarker item) &&
               item.Indent >= first.Indent &&
               (depth >= MaxNestingDepth || item.Indent < first.Indent + NestedListIndent) &&
               item.Ordered == first.Ordered &&
               // "1." の並びと "1)" の並びは別のリストにする。
               item.Delimiter == first.Delimiter &&
               item.TaskState.HasValue == first.TaskState.HasValue)
        {
            var para = CreateListItemParagraph(lines[i], item, context);
            var listItem = new ListItem(para) { Margin = new Thickness(0) };
            Paragraph? continued = para;
            i++;

            while (i < lines.Length && !context.DefinitionLines.Contains(lines[i].Index))
            {
                // 項目より深く字下げされた項目は、この項目の中の入れ子のリストにする。
                if (depth < MaxNestingDepth &&
                    TryGetListItem(lines[i].Text, out ListItemMarker child) &&
                    child.Indent >= item.Indent + NestedListIndent)
                {
                    var (nested, next) = RenderList(lines, i, depth + 1, context);
                    listItem.Blocks.Add(nested);
                    i = next;
                    continued = null;
                    continue;
                }

                // 項目より深く字下げされた続きの行は、改行を挟んで同じ項目の本文として並べる
                // (リストの外に出すと、2行目の先頭が項目の文字位置とそろわない)。
                if (!IsListContinuation(lines[i].Text, item.Indent))
                    break;
                var continuation = lines[i].Text.TrimStart(IndentChars);
                if (continued == null)
                {
                    continued = CreateParagraph();
                    listItem.Blocks.Add(continued);
                }
                else
                {
                    continued.Inlines.Add(new LineBreak());
                }
                AddInlineContent(continued.Inlines, continuation, lines[i].Index,
                    lines[i].Offset + lines[i].Text.Length - continuation.Length, context);
                i++;
            }

            list.ListItems.Add(listItem);
        }

        return (list, i);
    }

    /// <summary>入れ子とみなす字下げの深さ（桁）。スペース2つ、またはタブ1つから。</summary>
    private const int NestedListIndent = 2;

    private static Paragraph CreateListItemParagraph(SourceLine line, ListItemMarker item, RenderContext context)
    {
        var para = CreateParagraph();
        if (item.TaskState.HasValue)
        {
            var checkbox = context.CreateTaskCheckbox?.Invoke(line.Index, item.TaskState.Value) ??
                new WpfCheckBox { IsChecked = item.TaskState.Value };
            // チェックボックスの枠と同じ幅のぶら下げインデントを付け、折り返しや続きの行を
            // チェックボックスの左端ではなく項目の文字位置にそろえる。チェックボックスは
            // 文書に入るまでテンプレートが当たらず事前に測れないので、配置後の実際の幅を使う。
            // 本文との間には、以前の半角スペース1つぶんに近い空きを残す。
            var marker = new WpfBorder { Padding = new Thickness(0, 0, Math.Ceiling(context.BaseFontSize * 0.3), 0), Child = checkbox };
            marker.SizeChanged += (_, e) =>
            {
                para.Margin = new Thickness(e.NewSize.Width, 0, 0, 0);
                para.TextIndent = -e.NewSize.Width;
            };
            para.Inlines.Add(new InlineUIContainer(marker)
            {
                BaselineAlignment = BaselineAlignment.Center,
            });
        }
        AddInlineContent(para.Inlines, item.Text, line.Index, line.Offset + item.TextStart, context);
        return para;
    }

    /// <summary>
    /// 先頭行を描画するときのフォントサイズ。見出しは本文より大きく描かれるので、
    /// 折りたたんで1行だけ残す高さを出すときはこの値を使わないと文字の下が切れる。
    /// 大きさの決め方は Render 側と同じ式をここで共有する。
    /// <paramref name="ignoreHeadingSize"/> を立てると、見出しでも拡大せず
    /// baseFontSize をそのまま返す（タイトルバーを隠した1行表示向け）。
    /// </summary>
    public static double GetFirstLineFontSize(string text, double baseFontSize, bool ignoreHeadingSize = false)
    {
        if (ignoreHeadingSize) return baseFontSize;
        var lines = NormalizeLines(text);
        if (lines.Length == 0) return baseFontSize;
        return TryGetHeading(lines[0], out var level, out _)
            ? HeadingFontSize(baseFontSize, level)
            : baseFontSize;
    }

    private static double HeadingFontSize(double baseFontSize, int level)
        => Math.Max(baseFontSize, baseFontSize + 9 - level);

    private static string[] NormalizeLines(string text)
        => string.IsNullOrEmpty(text)
            ? []
            : text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

    private static Paragraph CreateParagraph()
        => new() { Margin = new Thickness(0) };

    private static Paragraph CreateCodeBlock(string text, bool darkMode)
    {
        var para = new Paragraph
        {
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(6, 3, 6, 3),
            FontFamily = CodeFontFamily,
            Background = GetCodeBackground(darkMode),
        };

        var lines = NormalizeLines(text);
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) para.Inlines.Add(new LineBreak());
            para.Inlines.Add(new Run(lines[i]));
        }
        return para;
    }

    private static Paragraph CreateHorizontalRule(bool darkMode)
    {
        var para = CreateParagraph();
        para.Margin = new Thickness(0, 4, 0, 4);
        para.BorderBrush = GetBorderBrush(darkMode);
        para.BorderThickness = new Thickness(0, 1, 0, 0);
        para.Inlines.Add(new Run(" "));
        return para;
    }

    private static WpfSolidBrush GetCodeBackground(bool darkMode)
        => darkMode
            ? new WpfSolidBrush(WpfColor.FromArgb(34, 255, 255, 255))
            : new WpfSolidBrush(WpfColor.FromArgb(24, 0, 0, 0));

    // インラインコードの地の色は Run.Background では塗らない。Background は
    // 実際に字を描いたフォントの高さで塗られるので、Consolas に無い字
    // (↶ ☑ 🐱、全角文字など) が代替フォントで描かれた所だけ帯の上端がずれ、
    // 段差になる。ベースライン基準・フォントサイズ単位の太い線なら、どの
    // フォントで描かれても同じ位置・同じ太さの帯になる。-0.35em / 1.15em は
    // Consolas の Background と同じ範囲を塗る値。線は字の上に重なるが、
    // 地の色はほぼ透明なので字の見え方は変わらない。
    private static TextDecorationCollection GetInlineCodeBand(bool darkMode)
        => darkMode ? DarkInlineCodeBand : LightInlineCodeBand;

    private static readonly TextDecorationCollection LightInlineCodeBand = CreateInlineCodeBand(false);
    private static readonly TextDecorationCollection DarkInlineCodeBand = CreateInlineCodeBand(true);

    private static TextDecorationCollection CreateInlineCodeBand(bool darkMode)
    {
        var pen = new System.Windows.Media.Pen(GetCodeBackground(darkMode), 1.15);
        var band = new TextDecorationCollection
        {
            new TextDecoration(TextDecorationLocation.Baseline, pen, -0.35,
                TextDecorationUnit.FontRenderingEmSize, TextDecorationUnit.FontRenderingEmSize),
        };
        band.Freeze();
        return band;
    }

    // コードの Run は自前の TextDecorations (地の帯) を持つので、外側の ~~ の
    // 取り消し線を継承しない。帯に取り消し線を足したものに差し替える。
    private static void StrikeThroughInlineCode(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Span span)
            {
                StrikeThroughInlineCode(span.Inlines);
            }
            else if (inline is Run run &&
                     (ReferenceEquals(run.TextDecorations, LightInlineCodeBand) ||
                      ReferenceEquals(run.TextDecorations, DarkInlineCodeBand)))
            {
                var decorations = run.TextDecorations.Clone();
                decorations.Add(TextDecorations.Strikethrough);
                decorations.Freeze();
                run.TextDecorations = decorations;
            }
        }
    }

    private static WpfSolidBrush GetBorderBrush(bool darkMode)
        => darkMode
            ? new WpfSolidBrush(WpfColor.FromArgb(95, 255, 255, 255))
            : new WpfSolidBrush(WpfColor.FromArgb(80, 0, 0, 0));

    private static WpfSolidBrush GetTableHeaderBackground(bool darkMode)
        => darkMode
            ? new WpfSolidBrush(WpfColor.FromArgb(26, 255, 255, 255))
            : new WpfSolidBrush(WpfColor.FromArgb(18, 0, 0, 0));

    private static bool TryParseTable(
        string[] lines,
        int start,
        Func<string, string, Hyperlink> createHyperlink,
        Func<MarkdownImage, Inline>? createImage,
        IReadOnlyDictionary<string, string> references,
        bool darkMode,
        out Table table,
        out int nextIndex)
    {
        table = new Table();
        nextIndex = start;

        if (start + 1 >= lines.Length ||
            !TrySplitTableRow(lines[start], out var headers) ||
            !TrySplitTableRow(lines[start + 1], out var separatorCells) ||
            !TryGetTableAlignments(separatorCells, out var alignments))
        {
            return false;
        }

        int columnCount = headers.Count;
        if (columnCount == 0 || separatorCells.Count != columnCount)
            return false;

        table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 3, 0, 3),
        };

        for (int i = 0; i < columnCount; i++)
            table.Columns.Add(new TableColumn());

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        var headerRow = new TableRow();
        group.Rows.Add(headerRow);
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            headerRow.Cells.Add(CreateTableCell(headers[columnIndex], createHyperlink, createImage, references, alignments[columnIndex], isHeader: true, darkMode));

        int rowIndex = start + 2;
        while (rowIndex < lines.Length && TrySplitTableRow(lines[rowIndex], out var cells))
        {
            if (cells.Count != columnCount)
                break;

            var row = new TableRow();
            group.Rows.Add(row);
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
                row.Cells.Add(CreateTableCell(cells[columnIndex], createHyperlink, createImage, references, alignments[columnIndex], isHeader: false, darkMode));

            rowIndex++;
        }

        nextIndex = rowIndex;
        return true;
    }

    private static TableCell CreateTableCell(
        string text,
        Func<string, string, Hyperlink> createHyperlink,
        Func<MarkdownImage, Inline>? createImage,
        IReadOnlyDictionary<string, string> references,
        TextAlignment textAlignment,
        bool isHeader,
        bool darkMode)
    {
        var paragraph = CreateParagraph();
        paragraph.TextAlignment = textAlignment;
        AddInlineContent(paragraph.Inlines, text.Trim(), -1, 0, createHyperlink, createImage, references, darkMode);
        if (isHeader)
            paragraph.FontWeight = FontWeights.Bold;

        return new TableCell(paragraph)
        {
            BorderBrush = darkMode ? GetBorderBrush(true) : new WpfSolidBrush(WpfColor.FromArgb(64, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5, 2, 5, 2),
            Background = isHeader ? GetTableHeaderBackground(darkMode) : null,
        };
    }

    private static bool TrySplitTableRow(string line, out List<string> cells)
    {
        cells = [];
        var trimmed = line.Trim();
        if (!trimmed.Contains('|'))
            return false;

        if (trimmed.StartsWith('|'))
            trimmed = trimmed[1..];
        if (trimmed.EndsWith('|'))
            trimmed = trimmed[..^1];

        cells = SplitUnescapedPipes(trimmed);
        return cells.Count >= 2;
    }

    private static List<string> SplitUnescapedPipes(string text)
    {
        var cells = new List<string>();
        var start = 0;
        var escaped = false;

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == '|')
            {
                cells.Add(UnescapeTableCell(text[start..i].Trim()));
                start = i + 1;
            }
        }

        cells.Add(UnescapeTableCell(text[start..].Trim()));
        return cells;
    }

    private static string UnescapeTableCell(string text)
        => text.Replace("\\|", "|");

    private static bool TryGetTableAlignments(IReadOnlyList<string> cells, out List<TextAlignment> alignments)
    {
        alignments = [];
        if (cells.Count < 2)
            return false;

        foreach (var cell in cells)
        {
            if (!TryGetTableAlignment(cell, out var alignment))
                return false;

            alignments.Add(alignment);
        }

        return true;
    }

    private static bool TryGetTableAlignment(string cell, out TextAlignment alignment)
    {
        alignment = TextAlignment.Left;
        var trimmed = cell.Trim();
        var leftAligned = trimmed.StartsWith(":", StringComparison.Ordinal);
        var rightAligned = trimmed.EndsWith(":", StringComparison.Ordinal);
        if (leftAligned)
            trimmed = trimmed[1..];
        if (rightAligned)
            trimmed = trimmed[..^1];

        if (trimmed.Length < 3 || !trimmed.All(c => c == '-'))
            return false;

        alignment = (leftAligned, rightAligned) switch
        {
            (true, true) => TextAlignment.Center,
            (false, true) => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
        return true;
    }

    private static bool IsFence(string line)
        => line.TrimStart().StartsWith("```", StringComparison.Ordinal);

    private static bool IsHorizontalRule(string trimmed)
        => trimmed.Length >= 3 &&
           (trimmed.All(c => c == '-') ||
            trimmed.All(c => c == '*') ||
            trimmed.All(c => c == '_'));

    private static bool TryGetHeading(string line, out int level, out string text)
    {
        level = 0;
        text = "";

        while (level < line.Length && level < 6 && line[level] == '#')
            level++;

        if (level == 0 || level >= line.Length || line[level] != ' ')
            return false;

        text = line[(level + 1)..].Trim();
        return true;
    }

    /// <summary>引用の印 ">" を1段外した行。印の後ろの空白1つも外す。</summary>
    private static bool TryStripQuote(SourceLine line, out SourceLine stripped)
    {
        var text = line.Text;
        var at = 0;
        while (at < text.Length && at < 4 && text[at] == ' ')
            at++;
        if (at >= text.Length || text[at] != '>')
        {
            stripped = default;
            return false;
        }

        at++;
        if (at < text.Length && text[at] is ' ' or '\t')
            at++;
        stripped = new SourceLine(text[at..], line.Index, line.Offset + at);
        return true;
    }

    private static bool IsQuote(string line)
        => TryStripQuote(new SourceLine(line, 0, 0), out _);

    /// <summary>リスト項目の印を読んだ結果。</summary>
    /// <param name="Indent">印の前の字下げ（桁。タブは4桁ごとの位置まで）。</param>
    /// <param name="TextStart">行の中で本文（チェックボックスの印の後ろ）が始まる位置。</param>
    /// <param name="Delimiter">番号の後ろの区切り（"." か ")"）。箇条書きでは 0。</param>
    private readonly record struct ListItemMarker(
        bool Ordered, int Number, string Text, int TextStart, bool? TaskState, int Indent, char Delimiter);

    private static bool TryGetListItem(string line, out ListItemMarker item)
    {
        item = default;
        var at = 0;
        while (at < line.Length && Array.IndexOf(IndentChars, line[at]) >= 0)
            at++;
        var indent = GetIndentWidth(line);

        bool ordered;
        var number = 0;
        var delimiter = '\0';
        if (at + 1 < line.Length && line[at] is '-' or '*' or '+' && line[at + 1] is ' ' or '\t')
        {
            ordered = false;
            at += 2;
        }
        else
        {
            var digits = at;
            while (digits < line.Length && digits - at < 9 && char.IsAsciiDigit(line[digits]))
                digits++;
            // "1." と "1)" のどちらも番号付きリストとして読む。
            if (digits == at || digits + 1 >= line.Length ||
                line[digits] is not ('.' or ')') || line[digits + 1] is not (' ' or '\t'))
                return false;
            ordered = true;
            number = int.Parse(line.AsSpan(at, digits - at), System.Globalization.CultureInfo.InvariantCulture);
            delimiter = line[digits];
            at = digits + 2;
        }

        var text = line[at..];
        var before = text.Length;
        ReadTaskState(ref text, out var taskState);
        item = new ListItemMarker(ordered, number, text, at + before - text.Length, taskState, indent, delimiter);
        return true;
    }

    private static bool IsListContinuation(string line, int itemIndent)
        => line.Trim().Length > 0 &&
           GetIndentWidth(line) > itemIndent &&
           !TryGetListItem(line, out ListItemMarker _) &&
           !IsFence(line) &&
           !IsQuote(line);

    private static int GetIndentWidth(string line)
    {
        var width = 0;
        foreach (var ch in line)
        {
            if (ch == ' ') width++;
            else if (ch == '\t') width += 4 - width % 4;
            else if (ch == '\u3000') width += 2;
            else break;
        }
        return width;
    }

    private static Dictionary<string, string> CollectReferenceDefinitions(string[] lines, out HashSet<int> definitionLines)
    {
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        definitionLines = [];
        for (var i = 0; i < lines.Length; i++)
        {
            // コードブロック内の "[1]: URL" は定義ではなく本文として残す（Render のフェンス判定と同じ）。
            if (IsFence(lines[i]))
            {
                for (i++; i < lines.Length && !IsFence(lines[i]); i++) { }
                continue;
            }

            if (!TryGetReferenceDefinition(lines[i], out var label, out var target))
                continue;

            references.TryAdd(label, target);
            definitionLines.Add(i);
        }
        return references;
    }

    private static bool TryGetReferenceDefinition(string line, out string label, out string target)
    {
        label = "";
        target = "";
        if (GetIndentWidth(line) > 3)
            return false;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith('['))
            return false;

        var labelEnd = FindUnescaped(trimmed, "]", 1);
        if (labelEnd <= 1 || labelEnd + 1 >= trimmed.Length || trimmed[labelEnd + 1] != ':')
            return false;

        var rawTarget = trimmed[(labelEnd + 2)..].Trim();
        label = NormalizeReferenceLabel(trimmed[1..labelEnd]);
        target = UnescapeMarkdownText(StripOptionalMarkdownTitle(rawTarget));
        // "[重要]: 明日までに提出" のような普通のメモを定義として隠さないよう、リンク先に見えるものだけを採る。
        return label.Length > 0 &&
               target.Length > 0 &&
               (rawTarget.StartsWith('<') || IndexOfWhitespace(target) < 0) &&
               LinkDetector.IsLink(target);
    }

    private static string NormalizeReferenceLabel(string label)
        => string.Join(' ', label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // [表示名][1]、[表示名][]、[表示名] の3形式。定義が無いものはリンクにしない。
    private static bool TryGetReferenceLink(
        string text,
        int start,
        IReadOnlyDictionary<string, string> references,
        out string label,
        out string target,
        out int length)
    {
        label = "";
        target = "";
        length = 0;

        // ']' の直後の '[' は [表示名][未定義] の後半なので、単独の [表示名] として読まない。
        if (references.Count == 0 || text[start] != '[' || GetCharOrDefault(text, start - 1) == ']')
            return false;

        var labelEnd = FindUnescaped(text, "]", start + 1);
        if (labelEnd <= start + 1)
            return false;

        var key = text[(start + 1)..labelEnd];
        var end = labelEnd;
        var after = GetCharOrDefault(text, labelEnd + 1);
        if (after == '[')
        {
            end = FindUnescaped(text, "]", labelEnd + 2);
            if (end < 0)
                return false;
            if (end > labelEnd + 2)
                key = text[(labelEnd + 2)..end];
        }
        else if (after is '(' or ':')
        {
            return false;
        }

        if (!references.TryGetValue(NormalizeReferenceLabel(key), out var found) || !LinkDetector.IsLink(found))
            return false;

        label = UnescapeMarkdownText(text[(start + 1)..labelEnd]);
        target = found;
        length = end + 1 - start;
        return true;
    }

    private static void ReadTaskState(ref string text, out bool? taskState)
    {
        var task = MarkdownSyntax.ParseTaskMarker(text, 0, 0);
        taskState = task?.IsChecked;
        if (task != null) text = text[task.Source.Length..];
    }

    private static void AddInlineContent(InlineCollection inlines, string text, int lineIndex, int lineOffset, RenderContext context)
        => AddInlineContent(inlines, text, lineIndex, lineOffset, context.CreateHyperlink, context.CreateImage,
            context.References, context.DarkMode);

    private static void AddInlineContent(
        InlineCollection inlines,
        string text,
        int lineIndex,
        int lineOffset,
        Func<string, string, Hyperlink> createHyperlink,
        Func<MarkdownImage, Inline>? createImage,
        IReadOnlyDictionary<string, string> references,
        bool darkMode, int depth = 0)
    {
        foreach (var inline in ParseInline(text, lineIndex, lineOffset, createHyperlink, createImage, references, darkMode, depth))
            inlines.Add(inline);
    }

    private static IEnumerable<Inline> ParseInline(
        string text,
        int lineIndex,
        int lineOffset,
        Func<string, string, Hyperlink> createHyperlink,
        Func<MarkdownImage, Inline>? createImage,
        IReadOnlyDictionary<string, string> references,
        bool darkMode, int depth = 0)
    {
        if (depth >= 16 || text.Length > 8192)
        {
            yield return new Run(text);
            yield break;
        }
        int pos = 0;
        // '[' で始まるリンク/画像は必ず後方に "](" を必要とする。行内に
        // それが無い位置では前方走査ごと省く。省かないと '[' が並ぶ行で
        // リンク探索が毎回行末まで走り、解析が O(n^2) になる。
        // 参照定義があるときは [表示名][1] / [表示名] も候補なので、最後の ']' まで探す。
        var lastLinkStart = references.Count > 0 ? text.LastIndexOf(']') : FindLastLinkCandidate(text);
        // 直近のリンク探索がラベル終端として見た ']' の次の位置。そこまでの
        // '[' は同じ ']' を見て同じ結果になるので、もう走査しない。
        var linkRetryFrom = 0;
        while (pos < text.Length)
        {
            var next = FindNextInlineMarker(text, pos, linkRetryFrom, lastLinkStart);
            if (next > pos)
            {
                foreach (var inline in ParsePlainLinks(text[pos..next], createHyperlink))
                    yield return inline;
                pos = next;
                continue;
            }

            if (TryGetEscapedMarkdownChar(text, pos, out var escapedChar))
            {
                yield return new Run(escapedChar.ToString());
                pos += 2;
                continue;
            }

            if (text[pos] == '`')
            {
                var end = FindUnescaped(text, "`", pos + 1);
                if (end > pos)
                {
                    yield return new Run(text[(pos + 1)..end])
                    {
                        FontFamily = CodeFontFamily,
                        TextDecorations = GetInlineCodeBand(darkMode),
                    };
                    pos = end + 1;
                    continue;
                }
            }

            if (TryGetDelimitedText(text, pos, "**", out var boldText, out var boldLength) ||
                TryGetDelimitedText(text, pos, "__", out boldText, out boldLength))
            {
                var span = new Span { FontWeight = FontWeights.Bold };
                AddInlineContent(span.Inlines, boldText, lineIndex, lineOffset + pos + 2, createHyperlink, createImage, references, darkMode, depth + 1);
                yield return span;
                pos += boldLength;
                continue;
            }

            if (TryGetHighlight(text, pos, out var highlightText, out var highlightLength))
            {
                var span = new Span { Background = GetHighlightBackground(darkMode) };
                AddInlineContent(span.Inlines, highlightText, lineIndex, lineOffset + pos + 2, createHyperlink, createImage, references, darkMode, depth + 1);
                yield return span;
                pos += highlightLength;
                continue;
            }

            if (TryGetDelimitedText(text, pos, "~~", out var strikeText, out var strikeLength))
            {
                var span = new Span { TextDecorations = TextDecorations.Strikethrough };
                AddInlineContent(span.Inlines, strikeText, lineIndex, lineOffset + pos + 2, createHyperlink, createImage, references, darkMode, depth + 1);
                StrikeThroughInlineCode(span.Inlines);
                yield return span;
                pos += strikeLength;
                continue;
            }

            if (TryGetDelimitedText(text, pos, "*", out var italicText, out var italicLength) ||
                TryGetDelimitedText(text, pos, "_", out italicText, out italicLength))
            {
                var span = new Span { FontStyle = FontStyles.Italic };
                AddInlineContent(span.Inlines, italicText, lineIndex, lineOffset + pos + 1, createHyperlink, createImage, references, darkMode, depth + 1);
                yield return span;
                pos += italicLength;
                continue;
            }

            if (TryGetWikiImage(text, pos, out var wikiTarget, out var wikiLength, out var wikiWidth, out var wikiHeight))
            {
                yield return createImage?.Invoke(new MarkdownImage(wikiTarget, wikiTarget, lineIndex, lineOffset + pos, wikiLength, wikiWidth, wikiHeight))
                    ?? new Run(text.Substring(pos, wikiLength));
                pos += wikiLength;
                continue;
            }
            if (IsLinkStart(text, pos) && pos >= linkRetryFrom && pos < lastLinkStart)
            {
                if (MarkdownSyntax.ParseImage(text, pos, lineIndex, lineOffset) is { } image)
                {
                    if (createImage != null)
                        yield return createImage(new MarkdownImage(image.Alt, image.Target, image.Source.Line, image.Source.Start, image.Source.Length, image.Width, image.Height));
                    else
                        yield return new Run(text.Substring(pos, image.Source.Length));
                    pos += image.Source.Length;
                    continue;
                }

                if (MarkdownSyntax.ParseLink(text, pos, lineIndex, lineOffset) is { } link)
                {
                    yield return createHyperlink(link.Label, link.Target);
                    pos += link.Source.Length;
                    continue;
                }

                if (TryGetReferenceLink(text, pos, references, out var referenceLabel, out var referenceTarget, out var referenceLength))
                {
                    yield return createHyperlink(referenceLabel, referenceTarget);
                    pos += referenceLength;
                    continue;
                }

                // この '[' はリンクにも画像にもならなかった。ラベル終端に
                // なり得る ']' までの '[' は同じ ']' を見て同じ結果になるので、
                // そこまでは探索を繰り返さない。
                var labelEnd = FindUnescaped(text, "]", pos + 1);
                linkRetryFrom = labelEnd < 0 ? text.Length : labelEnd + 1;
            }

            if (TryGetAutolink(text, pos, out var autolinkTarget, out var autolinkLength))
            {
                yield return createHyperlink(autolinkTarget, autolinkTarget);
                pos += autolinkLength;
                continue;
            }

            foreach (var inline in ParsePlainLinks(text[pos].ToString(), createHyperlink))
                yield return inline;
            pos++;
        }
    }

    private static bool IsLinkStart(string text, int index)
        => text[index] == '[' || text.AsSpan(index).StartsWith("![", StringComparison.Ordinal);

    // リンク/画像になり得る最後の "](" の位置。無ければ -1。
    private static int FindLastLinkCandidate(string text)
    {
        for (var i = text.Length - 2; i >= 0; i--)
            if (text[i] == ']' && text[i + 1] == '(')
                return i;
        return -1;
    }

    private static int FindNextInlineMarker(string text, int start, int linkRetryFrom, int lastLinkStart)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (TryGetEscapedMarkdownChar(text, i, out _))
                return i;

            if (text[i] == '`' && FindUnescaped(text, "`", i + 1) > i)
                return i;
            if (TryGetHighlight(text, i, out _, out _) ||
                TryGetDelimitedText(text, i, "~~", out _, out _) ||
                TryGetDelimitedText(text, i, "**", out _, out _) ||
                TryGetDelimitedText(text, i, "__", out _, out _) ||
                TryGetDelimitedText(text, i, "*", out _, out _) ||
                TryGetDelimitedText(text, i, "_", out _, out _) ||
                (i >= linkRetryFrom && i < lastLinkStart && IsLinkStart(text, i)) ||
                text[i] == '<')
            {
                return i;
            }
            if (text.AsSpan(i).StartsWith("![[", StringComparison.Ordinal)) return i;
        }

        return text.Length;
    }

    private static bool TryGetDelimitedText(string text, int start, string marker, out string innerText, out int length)
    {
        innerText = "";
        length = 0;
        if (!text.AsSpan(start).StartsWith(marker, StringComparison.Ordinal))
            return false;
        if ((marker == "_" || marker == "__") && IsWordChar(GetCharOrDefault(text, start - 1)))
            return false;

        var end = FindUnescaped(text, marker, start + marker.Length);
        if (end <= start + marker.Length)
            return false;
        if ((marker == "_" || marker == "__") && IsWordChar(GetCharOrDefault(text, end + marker.Length)))
            return false;

        innerText = text[(start + marker.Length)..end];
        length = end - start + marker.Length;
        return true;
    }

    /// <summary>
    /// "==文字==" のハイライト（Obsidian と同じ書き方）。"a == b == c" のような式を
    /// 塗らないよう、内側が空白で始まる・終わるものは対象にしない。
    /// </summary>
    private static bool TryGetHighlight(string text, int start, out string innerText, out int length)
        => TryGetDelimitedText(text, start, "==", out innerText, out length) &&
           !char.IsWhiteSpace(innerText[0]) && !char.IsWhiteSpace(innerText[^1]);

    private static WpfSolidBrush GetHighlightBackground(bool darkMode)
        => darkMode
            ? new WpfSolidBrush(WpfColor.FromArgb(110, 255, 193, 7))
            : new WpfSolidBrush(WpfColor.FromArgb(150, 255, 213, 79));

    private static bool TryGetEscapedMarkdownChar(string text, int start, out char escapedChar)
    {
        escapedChar = '\0';
        if (start + 1 >= text.Length || text[start] != '\\' || !IsEscapableMarkdownChar(text[start + 1]))
            return false;

        escapedChar = text[start + 1];
        return true;
    }

    private static bool IsWordChar(char ch)
        => char.IsLetterOrDigit(ch) || ch == '_';

    private static char GetCharOrDefault(string text, int index)
        => index >= 0 && index < text.Length ? text[index] : '\0';

    private static IEnumerable<Inline> ParsePlainLinks(
        string text,
        Func<string, string, Hyperlink> createHyperlink)
    {
        foreach (var segment in LinkDetector.Parse(text))
        {
            yield return segment.IsLink
                ? createHyperlink(segment.Text, segment.Text)
                : new Run(segment.Text);
        }
    }
}
