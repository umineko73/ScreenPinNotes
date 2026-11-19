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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using WpfBrushes    = System.Windows.Media.Brushes;
using WpfButton     = System.Windows.Controls.Button;
using WpfColor      = System.Windows.Media.Color;
using WpfCursors    = System.Windows.Input.Cursors;
using WpfImage      = System.Windows.Controls.Image;
using WpfSolidBrush = System.Windows.Media.SolidColorBrush;

namespace ScreenPinNotes.Views;

public partial class StickyNoteWindow
{
    // ─── アイコンピッカー ────────────────────────────────────────
    //
    // Teams のリアクションと同じ二段構え。開いた直後は「アイコンなし」と
    // よく使うアイコン（既定10個）の1列だけを出し、「その他」で全カテゴリーを
    // タブ付きの一覧として広げる。よく使うの入れ替えは一覧の「編集」から行う。

    /// <summary>よく使うの列と一覧の1行に並べる数。</summary>
    private const int IconColumns = 10;
    private IconPickerParts? _iconPicker;

    private sealed class IconPickerParts
    {
        public required WrapPanel Quick { get; init; }
        public required WpfButton None { get; init; }
        public required WpfButton More { get; init; }
        public required TextBlock MoreGlyph { get; init; }
        public required FrameworkElement Details { get; init; }
        public required StackPanel Tabs { get; init; }
        public required StackPanel Sections { get; init; }
        public required ScrollViewer Scroll { get; init; }
        public required WpfButton Edit { get; init; }
        public required TextBlock EditLabel { get; init; }
        public required TextBlock Hint { get; init; }
        public List<WpfButton> IconButtons { get; } = [];
        public List<(FrameworkElement Heading, WpfButton Tab)> SectionTabs { get; } = [];
        public bool Expanded { get; set; }
        public bool Editing { get; set; }
    }

    /// <summary>一覧のタブに出す絵文字。無いカテゴリーは先頭のアイコンを使う。</summary>
    private static readonly Dictionary<string, string> IconGroupTabIcons = new()
    {
        ["IconColors"] = "🔴", ["IconPriority"] = "🔥", ["IconStatus"] = "✅", ["IconNotes"] = "📌",
        ["IconIdeas"] = "💡", ["IconSchedule"] = "📅", ["IconWork"] = "💼", ["IconDaily"] = "🏠",
        ["IconAnimals"] = "🐱",
    };

    /// <summary>絵文字のセル。マウスが乗ると絵文字だけを少し大きくする（セルの地は動かさない）。</summary>
    private WpfButton CreateEmojiCell(string icon, string tooltip)
    {
        var scale = new ScaleTransform(1, 1);
        var image = new WpfImage
        {
            Source = RenderEmoji(icon), Width = 22, Height = 22, Stretch = Stretch.Uniform,
            RenderTransform = scale, RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
        };
        var button = CreatePickerCell(image, icon, tooltip);
        button.MouseEnter += (_, _) => AnimateIconScale(scale, 1.22);
        button.MouseLeave += (_, _) => AnimateIconScale(scale, 1.0);
        return button;
    }

    private void AnimateIconScale(ScaleTransform scale, double to)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(Settings.Timings.ToolbarFadeMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private Popup BuildIconPopup()
    {
        var gridWidth = IconColumns * (PickerCell + PickerCellGap * 2);

        // 1段目: [アイコンなし] | よく使う | [その他]
        var none = CreatePickerCell(GlyphText(UiIcons.Cancel, 12), "", LocalizationService.T("NoIconTooltip"));
        none.Foreground = PopupForegroundBrush();
        ((TextBlock)none.Content).Opacity = 0.6;
        var quick = new WrapPanel { Width = gridWidth };
        var moreGlyph = GlyphText(UiIcons.ChevronDown, 12);
        var more = CreatePickerCell(moreGlyph, null, LocalizationService.T("IconMore"));
        var quickRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(none, Dock.Left);
        quickRow.Children.Add(none);
        var leftDivider = VerticalDivider();
        DockPanel.SetDock(leftDivider, Dock.Left);
        quickRow.Children.Add(leftDivider);
        DockPanel.SetDock(more, Dock.Right);
        more.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        quickRow.Children.Add(more);
        var rightDivider = VerticalDivider();
        DockPanel.SetDock(rightDivider, Dock.Right);
        rightDivider.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        rightDivider.Margin = new Thickness(4, PickerCellGap + 6, 4, 0);
        quickRow.Children.Add(rightDivider);
        quickRow.Children.Add(quick);

        // 2段目（詳細）: カテゴリーのタブと編集ボタン、説明、カテゴリーごとの一覧
        var tabs = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var editLabel = new TextBlock { FontSize = 12, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        var edit = CreatePickerCell(editLabel, null, "");
        edit.Width = double.NaN;
        edit.Padding = new Thickness(10, 0, 10, 0);
        edit.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        DockPanel.SetDock(edit, Dock.Right);
        header.Children.Add(edit);
        header.Children.Add(tabs);

        var hint = new TextBlock
        {
            Text = LocalizationService.T("IconFavoritesHint"), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 2, 6, 4),
            Foreground = PickerSubtleBrush(0.75), Visibility = Visibility.Collapsed,
        };
        // 縦のスクロールバーの分を除いた幅に、ちょうど12列が収まる。
        var sections = new StackPanel { Width = (IconColumns + 2) * (PickerCell + PickerCellGap * 2) };
        var scroll = new ScrollViewer
        {
            Height = 248, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = sections,
        };
        var detailsPanel = new StackPanel();
        detailsPanel.Children.Add(new Border
        {
            Height = 1, Background = PickerSubtleBrush(0.12), Margin = new Thickness(0, 6, 0, 6),
        });
        detailsPanel.Children.Add(header);
        detailsPanel.Children.Add(new Border
        {
            Height = 1, Background = PickerSubtleBrush(0.12), Margin = new Thickness(0, 0, 0, 2),
        });
        detailsPanel.Children.Add(hint);
        detailsPanel.Children.Add(scroll);
        detailsPanel.Visibility = Visibility.Collapsed;

        var parts = new IconPickerParts
        {
            Quick = quick, None = none, More = more, MoreGlyph = moreGlyph, Details = detailsPanel,
            Tabs = tabs, Sections = sections, Scroll = scroll, Edit = edit, EditLabel = editLabel, Hint = hint,
        };
        _iconPicker = parts;

        none.Click += (_, _) => SelectIcon("");
        more.Click += (_, _) => SetIconPickerExpanded(!parts.Expanded);
        edit.Click += (_, _) =>
        {
            parts.Editing = !parts.Editing;
            PopulateIconPicker();
        };
        scroll.ScrollChanged += (_, _) => UpdateIconTabs();

        var content = new StackPanel();
        content.Children.Add(quickRow);
        content.Children.Add(detailsPanel);

        var popup = CreatePickerPopup(content);
        popup.Opened += (_, _) =>
        {
            // 開くたびに1段目だけの状態から始める。よく使うは他の付箋と共有なので作り直す。
            parts.Editing = false;
            parts.Expanded = false;
            PopulateIconPicker();
        };
        PopulateIconPicker();
        return popup;
    }

    private void SetIconPickerExpanded(bool expanded)
    {
        if (_iconPicker is not { } parts) return;
        parts.Expanded = expanded;
        if (!expanded) parts.Editing = false;
        PopulateIconPicker();
        if (expanded) parts.Scroll.ScrollToTop();
    }

    private void SelectIcon(string icon)
    {
        ViewModel.Icon = icon;
        if (_iconPopup != null) _iconPopup.IsOpen = false;
        RequestSave();
    }

    private void ToggleFavoriteIcon(string icon)
    {
        if (!Settings.FavoriteIcons.Remove(icon)) Settings.FavoriteIcons.Add(icon);
        _storage.SaveSettings(Settings);
        PopulateIconPicker();
    }

    private void IconCell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: string icon } || icon.Length == 0) return;
        if (_iconPicker?.Editing == true) ToggleFavoriteIcon(icon);
        else SelectIcon(icon);
    }

    private void PopulateIconPicker()
    {
        if (_iconPicker is not { } parts) return;
        var editing = parts.Editing;
        parts.IconButtons.Clear();

        parts.Quick.Children.Clear();
        foreach (var icon in Settings.FavoriteIcons)
        {
            var cell = CreateEmojiCell(icon, editing ? LocalizationService.T("IconRemoveFavorite") : icon);
            cell.Click += IconCell_Click;
            parts.Quick.Children.Add(cell);
            parts.IconButtons.Add(cell);
        }
        parts.None.IsEnabled = !editing;
        parts.MoreGlyph.Text = parts.Expanded ? UiIcons.ChevronUp : UiIcons.ChevronDown;
        parts.More.ToolTip = LocalizationService.T(parts.Expanded ? "IconFewer" : "IconMore");
        parts.Details.Visibility = parts.Expanded ? Visibility.Visible : Visibility.Collapsed;

        parts.EditLabel.Text = LocalizationService.T(editing ? "IconEditDone" : "IconEditFavorites");
        parts.Edit.Foreground = editing ? new WpfSolidBrush(PickerAccent()) : PopupForegroundBrush();
        parts.EditLabel.FontWeight = editing ? FontWeights.SemiBold : FontWeights.Normal;
        parts.Hint.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

        // 一覧は開いたときだけ作る。閉じたままなら100個近い絵文字を毎回並べずに済む。
        parts.Sections.Children.Clear();
        parts.Tabs.Children.Clear();
        parts.SectionTabs.Clear();
        if (parts.Expanded)
        {
            var known = AppSettings.IconGroups.SelectMany(group => group.Icons).ToHashSet();
            var palette = Settings.IconPalette.Concat(Settings.FavoriteIcons).Where(icon => icon.Length > 0).Distinct().ToList();
            var available = palette.ToHashSet();
            var groups = AppSettings.IconGroups
                .Select(group => (Key: group.Key, Icons: group.Icons.Where(available.Contains).ToArray()))
                .Append((Key: "IconOther", Icons: palette.Where(icon => !known.Contains(icon)).ToArray()))
                .Where(group => group.Icons.Length > 0);
            foreach (var (key, icons) in groups)
                AddIconSection(parts, key, icons, editing);
        }

        UpdateIconSelection();
        UpdateIconTabs();
    }

    private void AddIconSection(IconPickerParts parts, string key, string[] icons, bool editing)
    {
        var title = LocalizationService.T(key);
        var heading = new TextBlock
        {
            Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(6, parts.Sections.Children.Count == 0 ? 4 : 10, 0, 2),
            Foreground = PickerSubtleBrush(0.7),
        };
        parts.Sections.Children.Add(heading);
        var grid = new WrapPanel();
        foreach (var icon in icons)
        {
            var tooltip = editing
                ? LocalizationService.T(Settings.FavoriteIcons.Contains(icon) ? "IconRemoveFavorite" : "IconAddFavorite")
                : icon;
            var cell = CreateEmojiCell(icon, tooltip);
            cell.Click += IconCell_Click;
            grid.Children.Add(cell);
            parts.IconButtons.Add(cell);
        }
        parts.Sections.Children.Add(grid);

        var tabIcon = IconGroupTabIcons.TryGetValue(key, out var mapped) ? mapped : icons[0];
        var tab = CreateEmojiCell(tabIcon, title);
        tab.Tag = null; // 選択中の印を付ける対象から外す
        var image = (WpfImage)tab.Content;
        image.Width = image.Height = 18;
        tab.Content = null; // 下線と重ねるため、いったん外して入れ直す
        tab.Content = new Grid
        {
            Children =
            {
                image,
                new Border
                {
                    Height = 2, CornerRadius = new CornerRadius(1), Margin = new Thickness(6, 0, 6, -6),
                    VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                    Background = new WpfSolidBrush(PickerAccent()), Visibility = Visibility.Hidden,
                },
            },
        };
        tab.Click += (_, _) =>
        {
            var y = heading.TranslatePoint(new System.Windows.Point(0, 0), parts.Sections).Y;
            parts.Scroll.ScrollToVerticalOffset(Math.Max(0, y - heading.Margin.Top));
        };
        parts.Tabs.Children.Add(tab);
        parts.SectionTabs.Add((heading, tab));
    }

    /// <summary>一覧のいま見えている位置に当たるカテゴリーのタブに下線を引く。</summary>
    private void UpdateIconTabs()
    {
        if (_iconPicker is not { } parts || parts.SectionTabs.Count == 0) return;
        var offset = parts.Scroll.VerticalOffset;
        var active = parts.SectionTabs[0].Tab;
        if (parts.Scroll.ScrollableHeight > 0 && offset >= parts.Scroll.ScrollableHeight - 1)
            active = parts.SectionTabs[^1].Tab;
        else
            foreach (var (heading, tab) in parts.SectionTabs)
            {
                if (!heading.IsArrangeValid) break;
                var y = heading.TranslatePoint(new System.Windows.Point(0, 0), parts.Sections).Y;
                if (y - heading.Margin.Top <= offset + 1) active = tab;
            }
        foreach (var (_, tab) in parts.SectionTabs)
            if (tab.Content is Grid { Children: [_, Border underline] })
                underline.Visibility = tab == active ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>
    /// 選んでいるアイコンに色を付ける。編集中は代わりに、よく使うに入っているアイコンへ付ける。
    /// </summary>
    private void UpdateIconSelection()
    {
        if (_iconPicker is not { } parts) return;
        var accent = PickerAccent();
        var fill = new WpfSolidBrush(WpfColor.FromArgb(0x2E, accent.R, accent.G, accent.B));
        var border = new WpfSolidBrush(accent);
        var favorites = Settings.FavoriteIcons.ToHashSet();
        foreach (var button in parts.IconButtons.Append(parts.None))
        {
            var icon = button.Tag as string;
            // 編集中の上の列は全部がよく使うなので、印は一覧の側にだけ付ける。
            var marked = parts.Editing
                ? icon is { Length: > 0 } && favorites.Contains(icon) && !parts.Quick.Children.Contains(button)
                : icon == ViewModel.Icon;
            button.Background = marked ? fill : WpfBrushes.Transparent;
            button.BorderBrush = marked ? border : WpfBrushes.Transparent;
        }
    }
}
