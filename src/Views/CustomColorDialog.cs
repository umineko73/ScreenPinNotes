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
using System.Windows.Controls;
using System.Windows.Interop;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfSolidBrush = System.Windows.Media.SolidColorBrush;

namespace ScreenPinNotes.Views;

/// <summary>
/// カスタム配色の背景色とポイントカラーを選ぶダイアログ。
/// 2色を続けて選び、見本で確かめてから OK で付箋に反映する。
/// </summary>
public sealed class CustomColorDialog : Window
{
    private readonly AppSettings _settings;
    private readonly bool _titleBarHidden;
    private readonly Action _saveSettings;
    private readonly Border _preview = new() { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
    private readonly Border _previewTitleBar = new() { Height = 24, CornerRadius = new CornerRadius(3, 3, 0, 0) };
    private readonly TextBlock _previewTitle = new() { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _previewSpine = new() { Width = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(4, 6, 0, 6) };
    private readonly TextBlock _previewText = new() { Margin = new Thickness(10, 8, 10, 8), TextWrapping = TextWrapping.Wrap };

    /// <summary>選んだ背景色（"#RRGGBB"）。</summary>
    public string BackgroundColor { get; private set; }
    /// <summary>選んだポイントカラー（"#RRGGBB"）。</summary>
    public string AccentColor { get; private set; }

    public Border BackgroundSample { get; }
    public Border AccentSample { get; }
    public FrameworkElement AccentRow { get; }

    public CustomColorDialog(Window? owner, string backgroundColor, string accentColor, bool titleBarHidden,
        AppSettings settings, Action saveSettings)
    {
        if (owner != null)
        {
            Owner = owner;
            Topmost = owner.Topmost;
        }
        _settings = settings;
        _titleBarHidden = titleBarHidden;
        _saveSettings = saveSettings;
        BackgroundColor = NoteAppearance.NormalizeHex(backgroundColor) ?? NoteAppearance.Presets["yellow"].Bg;
        AccentColor = NoteAppearance.NormalizeHex(accentColor) ?? NoteAppearance.Presets["yellow"].Header;

        ControlTheme.Apply(this, settings.Theme == "Dark", dialog: true);
        Title = LocalizationService.T("CustomColorDialogTitle");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        var root = new StackPanel { Margin = new Thickness(16), Width = 320 };

        // 見本の付箋。タイトルバーの有無は元の付箋に合わせる。
        _previewTitle.Text = LocalizationService.T("CustomColorPreviewTitle");
        _previewTitleBar.Child = _previewTitle;
        _previewText.Text = LocalizationService.T("CustomColorPreviewText");
        var body = new DockPanel();
        if (titleBarHidden)
        {
            DockPanel.SetDock(_previewSpine, Dock.Left);
            body.Children.Add(_previewSpine);
        }
        body.Children.Add(_previewText);
        var previewStack = new StackPanel();
        if (!titleBarHidden) previewStack.Children.Add(_previewTitleBar);
        previewStack.Children.Add(body);
        _preview.Child = previewStack;
        _preview.Margin = new Thickness(0, 0, 0, 16);
        root.Children.Add(_preview);

        BackgroundSample = Sample();
        root.Children.Add(ColorRow("CustomBackgroundColor", BackgroundSample, background: true));
        AccentSample = Sample();
        var accentRow = ColorRow("CustomAccentColor", AccentSample, background: false);
        AccentRow = accentRow;
        root.Children.Add(accentRow);
        if (!titleBarHidden)
        {
            // ポイントカラーはタイトルバーを隠したときの帯にしか出ない。見えない色を選ばせないよう押せなくする。
            accentRow.IsEnabled = false;
            accentRow.Opacity = 0.4;
            root.Children.Add(new TextBlock
            {
                Text = LocalizationService.T("CustomAccentColorDisabledTooltip"),
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = LocalizationService.T("Cancel"), IsCancel = true, MinWidth = 80 });
        root.Children.Add(buttons);

        Content = root;
        Refresh();
    }

    private static Border Sample() => new()
    {
        Width = 22, Height = 22, CornerRadius = new CornerRadius(3),
        BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(0x99, 0x99, 0x99)), BorderThickness = new Thickness(1),
        Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
    };

    private DockPanel ColorRow(string labelKey, Border sample, bool background)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var change = new Button { Content = LocalizationService.T("CustomColorChange"), MinWidth = 80 };
        change.Click += (_, _) => ChangeColor(background);
        DockPanel.SetDock(change, Dock.Right);
        row.Children.Add(change);
        DockPanel.SetDock(sample, Dock.Left);
        row.Children.Add(sample);
        row.Children.Add(new TextBlock { Text = LocalizationService.T(labelKey), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    public void SetColors(string? background, string? accent)
    {
        BackgroundColor = NoteAppearance.NormalizeHex(background) ?? BackgroundColor;
        AccentColor = NoteAppearance.NormalizeHex(accent) ?? AccentColor;
        Refresh();
    }

    private void ChangeColor(bool background)
    {
        if (PickColor(this, background ? BackgroundColor : AccentColor, _settings, _saveSettings) is not { } picked) return;
        if (background) SetColors(picked, null);
        else SetColors(null, picked);
    }

    private void Refresh()
    {
        var appearance = new NoteAppearance(new StickyNote
        {
            ColorKey = NoteAppearance.CustomColorKey,
            CustomBackgroundColor = BackgroundColor,
            CustomAccentColor = AccentColor,
            IsTitleBarHidden = _titleBarHidden,
        }, _settings, forceOpaque: true);
        _preview.Background = appearance.BackgroundBrush;
        _preview.BorderBrush = BackgroundSample?.BorderBrush;
        _previewTitleBar.Background = appearance.TitleBarBrush;
        _previewTitle.Foreground = appearance.TitleBarForeground;
        _previewSpine.Background = appearance.HeaderBrush;
        _previewText.Foreground = appearance.TextForeground;
        if (BackgroundSample != null) BackgroundSample.Background = Brush(BackgroundColor);
        if (AccentSample != null) AccentSample.Background = Brush(AccentColor);
    }

    private static WpfSolidBrush Brush(string hex) => new((WpfColor)WpfColorConverter.ConvertFromString(hex)!);

    /// <summary>Windows の色の設定ダイアログで色を選ぶ。キャンセルなら null。</summary>
    internal static string? PickColor(Window owner, string? initialHex, AppSettings settings, Action saveSettings)
    {
        var initial = (WpfColor)WpfColorConverter.ConvertFromString(NoteAppearance.NormalizeHex(initialHex) ?? "#FFFFFF")!;
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(initial.R, initial.G, initial.B),
            // 「作成した色」は付箋をまたいで使い回せるよう設定に残す。
            CustomColors = settings.ColorDialogCustomColors.ToArray(),
        };
        var nativeOwner = new System.Windows.Forms.NativeWindow();
        nativeOwner.AssignHandle(new WindowInteropHelper(owner).Handle);
        try
        {
            var result = dialog.ShowDialog(nativeOwner);
            settings.ColorDialogCustomColors = dialog.CustomColors.ToList();
            saveSettings();
            if (result != System.Windows.Forms.DialogResult.OK) return null;
            return NoteAppearance.ToHex(WpfColor.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
        }
        finally { nativeOwner.ReleaseHandle(); }
    }
}
