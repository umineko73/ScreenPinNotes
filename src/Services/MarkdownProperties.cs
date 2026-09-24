// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3 of the License.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.IO;
using System.Windows;
using System.Windows.Documents;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ScreenPinNotes.Services;

public static class MarkdownProperties
{
    public static bool TryRender(string[] lines, bool dark, out Section section, out int nextLine, string language = "en",
        bool collapsed = false, Action<bool>? collapsedChanged = null)
    {
        section = new Section();
        nextLine = 0;
        if (lines.Length < 3 || lines[0].TrimStart('\uFEFF').Trim() != "---") return false;
        var end = Array.FindIndex(lines, 1, line => line.Trim() is "---" or "...");
        if (end < 0 || end > 512) return false;
        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(string.Join('\n', lines[1..end])));
            if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode mapping) return false;
            var rows = new List<(string Key, string Value)>();
            void Visit(YamlNode node, string key, int depth)
            {
                if (depth > 16 || rows.Count >= 512) throw new InvalidDataException();
                switch (node)
                {
                    case YamlScalarNode scalar: rows.Add((key, scalar.Value ?? "")); break;
                    case YamlMappingNode map:
                        foreach (var pair in map.Children)
                        {
                            if (pair.Key is not YamlScalarNode name) throw new InvalidDataException();
                            Visit(pair.Value, key.Length == 0 ? name.Value ?? "" : key + " › " + name.Value, depth + 1);
                        }
                        if (map.Children.Count == 0) rows.Add((key, "{}"));
                        break;
                    case YamlSequenceNode sequence:
                        for (var i = 0; i < sequence.Children.Count; i++)
                            Visit(sequence.Children[i], key + " [" + (i + 1) + "]", depth + 1);
                        if (sequence.Children.Count == 0) rows.Add((key, "[]"));
                        break;
                    default: throw new InvalidDataException();
                }
            }
            Visit(mapping, "", 0);
            section.Margin = new Thickness(0, 4, 0, 12);
            var label = LocalizationService.T("MarkdownProperties", language);
            var heading = new Run((collapsed ? "▶ " : "▼ ") + label);
            var toggle = new Hyperlink(heading) { TextDecorations = null,
                Foreground = dark ? System.Windows.Media.Brushes.LightSkyBlue : System.Windows.Media.Brushes.RoyalBlue };
            System.Windows.Automation.AutomationProperties.SetName(toggle, label);
            section.Blocks.Add(new Paragraph(toggle)
                { FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
            var table = new Table { CellSpacing = 0 };
            table.Columns.Add(new TableColumn { Width = new GridLength(150) });
            table.Columns.Add(new TableColumn { Width = new GridLength(300) });
            var group = new TableRowGroup();
            table.RowGroups.Add(group);
            foreach (var (key, value) in rows)
            {
                var row = new TableRow();
                row.Cells.Add(new TableCell(new Paragraph(new Run(key)) { Margin = new Thickness(0) })
                    { Padding = new Thickness(6, 4, 8, 4), FontWeight = FontWeights.SemiBold });
                row.Cells.Add(new TableCell(new Paragraph(new Run(value)) { Margin = new Thickness(0) })
                    { Padding = new Thickness(6, 4, 6, 4) });
                if (group.Rows.Count % 2 == 0)
                    row.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, dark ? (byte)255 : (byte)0, dark ? (byte)255 : (byte)0, dark ? (byte)255 : (byte)0));
                group.Rows.Add(row);
            }
            var container = section;
            section.Tag = table; // Retain the table (and its column widths) while collapsed.
            if (!collapsed) section.Blocks.Add(table);
            toggle.Click += (_, e) =>
            {
                collapsed = !collapsed;
                if (collapsed) container.Blocks.Remove(table);
                else container.Blocks.Add(table);
                heading.Text = (collapsed ? "▶ " : "▼ ") + label;
                collapsedChanged?.Invoke(collapsed);
                e.Handled = true;
            };
            nextLine = end + 1;
            return true;
        }
        catch (Exception ex) when (ex is YamlException or InvalidDataException or ArgumentException)
        { return false; } // Invalid or excessive YAML stays visible as source Markdown.
    }
}
