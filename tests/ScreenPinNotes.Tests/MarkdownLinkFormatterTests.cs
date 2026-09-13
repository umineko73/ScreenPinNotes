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

using System.Windows.Documents;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class MarkdownLinkFormatterTests
{
    [Theory]
    [InlineData("Docs] [v2", "https://example.com/reports/(draft)")]
    [InlineData("Wikipedia", "https://en.wikipedia.org/wiki/Function_%28mathematics%29")]
    [InlineData("notes.txt", @"C:\\Users\\me\\My Notes\\notes.txt")]
    public void Build_RoundTripsLabelAndTargetThroughRenderer(string label, string target)
    {
        var markdown = MarkdownLinkFormatter.Build(label, target);
        string? renderedLabel = null;
        string? renderedTarget = null;

        Hyperlink Factory(string actualLabel, string actualTarget)
        {
            renderedLabel = actualLabel;
            renderedTarget = actualTarget;
            return new Hyperlink(new Run(actualLabel));
        }

        MarkdownRenderer.Render(markdown, 13, Factory).ToList();

        Assert.Equal(label, renderedLabel);
        Assert.Equal(target, renderedTarget);
    }

    [Fact]
    public void Build_UsesAngleWrappedTargetWithoutEscapingUrlCharacters()
    {
        var markdown = MarkdownLinkFormatter.Build("a[b]\\c", "https://example.com/a(b)\\c");

        Assert.Equal(@"[a\[b\]\\c](<https://example.com/a(b)\c>)", markdown);
    }

    [Fact]
    public void Build_PreservesPercentEncodedUrl()
    {
        var markdown = MarkdownLinkFormatter.Build(
            "Wikipedia",
            "https://en.wikipedia.org/wiki/Function_%28mathematics%29");

        Assert.Equal(
            "[Wikipedia](<https://en.wikipedia.org/wiki/Function_%28mathematics%29>)",
            markdown);
    }

    [Fact]
    public void Build_PastedOnNextLine_PreservesExistingMarkdownLink()
    {
        var first = MarkdownLinkFormatter.Build("First", "https://example.com/first");
        var second = MarkdownLinkFormatter.Build("Second", "https://example.com/second");
        var inserted = TextInsertion.InsertAtSelection(first + "\n", first.Length + 1, 0, second);
        var renderedLinks = new List<(string Label, string Target)>();

        Hyperlink Factory(string label, string target)
        {
            renderedLinks.Add((label, target));
            return new Hyperlink(new Run(label));
        }

        MarkdownRenderer.Render(inserted.Text, 13, Factory).ToList();

        Assert.Equal(first + "\n" + second, inserted.Text);
        Assert.Equal(
            [("First", "https://example.com/first"), ("Second", "https://example.com/second")],
            renderedLinks);
    }

    // 手書きの <...> は前後に空白が入りうる。そのまま渡すと
    // Process.Start が失敗してリンクを押しても何も起きない。
    [Theory]
    [InlineData("[docs](< https://example.com >)", "https://example.com")]
    [InlineData("[docs](<https://example.com>)", "https://example.com")]
    public void Render_AngleWrappedTarget_TrimsSurroundingWhitespace(string markdown, string expected)
    {
        string? renderedTarget = null;

        Hyperlink Factory(string label, string target)
        {
            renderedTarget = target;
            return new Hyperlink(new Run(label));
        }

        MarkdownRenderer.Render(markdown, 13, Factory).ToList();

        Assert.Equal(expected, renderedTarget);
    }

    // 空の <> はリンクにしない（従来の括弧経路へ委ねる）。
    [Fact]
    public void Render_AngleWrappedTargetWithOnlyWhitespace_IsNotRenderedAsLink()
    {
        var rendered = false;

        Hyperlink Factory(string label, string target)
        {
            rendered = true;
            return new Hyperlink(new Run(label));
        }

        MarkdownRenderer.Render("[docs](<   >)", 13, Factory).ToList();

        Assert.False(rendered);
    }
}
