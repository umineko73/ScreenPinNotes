// ScreenStickyNotes - a desktop sticky notes app for Windows 11
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

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using ScreenStickyNotes.Models;
using ScreenStickyNotes.Services;
using ScreenStickyNotes.ViewModels;
using SkiaSharp;
using WpfBrushes     = System.Windows.Media.Brushes;
using WpfButton      = System.Windows.Controls.Button;
using WpfBitmapImage = System.Windows.Media.Imaging.BitmapImage;
using WpfCheckBox    = System.Windows.Controls.CheckBox;
using WpfColor       = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfCursors     = System.Windows.Input.Cursors;
using WpfDataFormats = System.Windows.DataFormats;
using WpfFontFamily  = System.Windows.Media.FontFamily;
using WpfImage       = System.Windows.Controls.Image;
using WpfListBox     = System.Windows.Controls.ListBox;
using WpfSolidBrush  = System.Windows.Media.SolidColorBrush;


namespace ScreenStickyNotes.Views;

public partial class StickyNoteWindow
{
    // ─── FlowDocument ↔ プレーンテキスト / Markdown ──────────────

    private void LoadContent(string text)
        => LoadMarkdownContent(text);

    private void LoadPlainContent(string text)
    {
        text = NormalizeLineEndings(text);
        _suppressTextChange = true;
        try
        {
            ContentBox.Document.Blocks.Clear();
            var lines = string.IsNullOrEmpty(text) ? [""] : text.Split('\n');
            var para = new Paragraph { Margin = new Thickness(0) };
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                    para.Inlines.Add(new LineBreak());

                foreach (var seg in LinkDetector.Parse(lines[i]))
                    para.Inlines.Add(seg.IsLink ? (Inline)CreateHyperlink(seg.Text) : new Run(seg.Text));
            }
            ContentBox.Document.Blocks.Add(para);
        }
        finally { _suppressTextChange = false; }
    }

    private void LoadMarkdownContent(string text)
    {
        text = NormalizeLineEndings(text);
        _suppressTextChange = true;
        try
        {
            ContentBox.Document.Blocks.Clear();
            foreach (var block in MarkdownRenderer.Render(
                text,
                ViewModel.FontSize,
                CreateHyperlink,
                CreateMarkdownImage,
                CreateTaskCheckbox,
                IsDarkTheme()))
            {
                ContentBox.Document.Blocks.Add(block);
            }
        }
        finally { _suppressTextChange = false; }
    }

    private string GetPlainText()
    {
        var sb    = new StringBuilder();
        bool first = true;
        foreach (Block block in ContentBox.Document.Blocks)
        {
            if (!first) sb.Append('\n');
            first = false;
            if (block is Paragraph para)
            {
                foreach (Inline inline in para.Inlines)
                {
                    sb.Append(inline switch
                    {
                        Run r                              => r.Text,
                        Hyperlink h when h.Tag is string t => t,
                        LineBreak                          => "\n",
                        _ => new TextRange(inline.ContentStart, inline.ContentEnd).Text,
                    });
                }
            }
        }
        return sb.ToString();
    }

    private WpfCheckBox CreateTaskCheckbox(int lineIndex, bool isChecked)
    {
        var checkbox = new WpfCheckBox
        {
            IsChecked = isChecked,
            Tag = lineIndex,
            Focusable = false,
            IsHitTestVisible = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0),
            Foreground = ViewModel.TextForeground,
        };
        checkbox.PreviewMouseLeftButtonDown += TaskCheckbox_PreviewMouseLeftButtonDown;
        return checkbox;
    }

    private void TaskCheckbox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfCheckBox { Tag: int lineIndex } checkbox)
            return;

        _isTaskCheckboxUpdatePending = true;
        var isChecked = checkbox.IsChecked != true;
        checkbox.IsChecked = isChecked;
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                ToggleMarkdownTask(lineIndex, isChecked);
            }
            finally
            {
                _isTaskCheckboxUpdatePending = false;
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
        e.Handled = true;
    }

    private void ToggleMarkdownTask(int lineIndex, bool isChecked)
    {
        var lines = ViewModel.Content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        if (lineIndex < 0 || lineIndex >= lines.Length)
            return;

        var line = lines[lineIndex];
        var uncheckedIndex = line.IndexOf("[ ]", StringComparison.Ordinal);
        var checkedIndex = line.IndexOf("[x]", StringComparison.OrdinalIgnoreCase);
        var markerIndex = uncheckedIndex >= 0 ? uncheckedIndex : checkedIndex;
        if (markerIndex < 0)
            return;

        lines[lineIndex] =
            line[..markerIndex] +
            (isChecked ? "[x]" : "[ ]") +
            line[(markerIndex + 3)..];

        ViewModel.Content = string.Join('\n', lines);
        RequestSave();
        LoadContent(ViewModel.Content);
    }

    private Inline CreateMarkdownImage(MarkdownRenderer.MarkdownImage markdownImage)
    {
        var imagePath = ResolveImagePath(markdownImage.Target);
        if (imagePath == null || !File.Exists(imagePath))
            return new Run(string.IsNullOrWhiteSpace(markdownImage.Alt)
                ? $"![image]({markdownImage.Target})"
                : $"![{markdownImage.Alt}]({markdownImage.Target})");

        var bitmap = new WpfBitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        var image = new WpfImage
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            MaxWidth = Math.Max(80, Width - 28),
            MaxHeight = 260,
            ToolTip = string.IsNullOrWhiteSpace(markdownImage.Alt) ? markdownImage.Target : markdownImage.Alt,
            Margin = new Thickness(0, 3, 0, 3),
        };

        if (markdownImage.Width is > 0)
            image.Width = markdownImage.Width.Value;
        if (markdownImage.Height is > 0)
            image.Height = markdownImage.Height.Value;

        var maxDisplayScale = Math.Min(1.0, Math.Min(image.MaxWidth / bitmap.Width, image.MaxHeight / bitmap.Height));
        var maxDisplayWidth = Math.Max(1, bitmap.Width * maxDisplayScale);
        if (markdownImage.LineIndex >= 0)
            image.ContextMenu = BuildImageContextMenu(new MarkdownImageContext(
                markdownImage.LineIndex,
                markdownImage.Start,
                markdownImage.Length,
                markdownImage.Alt,
                markdownImage.Target,
                maxDisplayWidth));

        return new InlineUIContainer(image)
        {
            BaselineAlignment = BaselineAlignment.Center,
        };
    }

    private sealed record MarkdownImageContext(
        int LineIndex,
        int Start,
        int Length,
        string Alt,
        string Target,
        double MaxDisplayWidth);

    private ContextMenu BuildImageContextMenu(MarkdownImageContext context)
    {
        var cm = new ContextMenu();
        for (var percent = 10; percent <= 100; percent += 10)
        {
            var percentItem = new MenuItem { Header = $"{percent}%" };
            var selectedPercent = percent;
            percentItem.Click += (_, _) => ResizeMarkdownImage(context, selectedPercent);
            cm.Items.Add(percentItem);
        }
        cm.Opened += (_, _) =>
        {
            _suppressViewMode = true;
            _isContentContextMenuOpen = true;
        };
        cm.Closed += ContentContextMenu_Closed;
        return cm;
    }

    private void ResizeMarkdownImage(MarkdownImageContext context, int percent)
    {
        var lines = ViewModel.Content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        if (context.LineIndex < 0 || context.LineIndex >= lines.Length)
            return;

        var line = lines[context.LineIndex];
        if (context.Start < 0 ||
            context.Start + context.Length > line.Length ||
            line[context.Start..(context.Start + context.Length)].IndexOf(context.Target, StringComparison.Ordinal) < 0)
        {
            return;
        }

        var width = Math.Clamp(Math.Round(context.MaxDisplayWidth * percent / 100.0), 40, 2000);
        var replacement = BuildMarkdownImageText(context, width);

        lines[context.LineIndex] =
            line[..context.Start] +
            replacement +
            line[(context.Start + context.Length)..];

        ViewModel.Content = string.Join('\n', lines);
        RequestSave();
        LoadContent(ViewModel.Content);
    }

    private static string BuildMarkdownImageText(MarkdownImageContext context, double? width)
        => width.HasValue
            ? FormattableString.Invariant($"![{context.Alt}]({context.Target}){{width={width.Value:0}}}")
            : $"![{context.Alt}]({context.Target})";

    private string? ResolveImagePath(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return null;

        if (Path.IsPathRooted(target))
            return Path.GetFullPath(target);

        var noteDir = StorageService.GetNoteDirectory(ViewModel.Model.Id);
        if ((target.Contains("://", StringComparison.Ordinal) ||
             target.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) &&
            Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return uri.IsFile ? uri.LocalPath : null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(noteDir, target.Replace('/', Path.DirectorySeparatorChar)));
        var noteRoot = Path.GetFullPath(noteDir) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(noteRoot, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }

    // ─── ハイパーリンク ──────────────────────────────────────────

    private Hyperlink CreateHyperlink(string target)
        => CreateHyperlink(target, target);

    private Hyperlink CreateHyperlink(string text, string target)
    {
        var link = new Hyperlink(new Run(text))
        {
            Foreground      = IsDarkTheme() ? WpfBrushes.LightSkyBlue : WpfBrushes.RoyalBlue,
            Cursor          = WpfCursors.Hand,
            Tag             = target,
            TextDecorations = TextDecorations.Underline,
        };
        link.Click += (_, _) => OpenTarget(target);
        return link;
    }

    private static void OpenTarget(string target)
    {
        try
        {
            if (LinkDetector.IsFolder(target))
                Process.Start("explorer.exe", $"\"{target}\"");
            else
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

}
