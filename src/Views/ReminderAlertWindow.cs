// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3 of the License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenPinNotes.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfBrush = System.Windows.Media.Brush;
using WpfImage = System.Windows.Controls.Image;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace ScreenPinNotes.Views;

/// <summary>付箋の色・アイコンとタイトル。通知ウィンドウの見出しに使う。</summary>
public sealed record ReminderAlertNote(string Title, WpfBrush Background, WpfBrush Accent, WpfBrush Foreground, ImageSource? Icon)
{
    public static ReminderAlertNote From(StickyNoteWindow window)
    {
        var vm = window.ViewModel;
        var icon = string.IsNullOrEmpty(vm.Icon) ? null : EmojiRenderer.Render(vm.Icon, vm.Settings.MonochromeIcons);
        return new(vm.DisplayTitle, vm.TitleBarBrush, vm.HeaderBrush, vm.TitleBarForeground, icon);
    }
}

/// <summary>
/// リマインダーの時刻に出す通知ウィンドウ。見出しを付箋と同じ色・アイコンにして、
/// どの付箋の知らせかがひと目で分かるようにする。ボタンは「スヌーズ（5分・15分・1時間）」の
/// まとまりと、「付箋を開く」「完了」に分ける。
/// </summary>
public sealed class ReminderAlertWindow : Window
{
    private ReminderAlertResult _result = new(false, null);

    public ReminderAlertWindow(ReminderAlertNote note, DateTime dueAt)
    {
        Title = LocalizationService.T("ReminderDueTitle");
        ControlTheme.Apply(this, App.Current.Settings.IsDarkTheme, dialog: true);
        Width = 440;
        MinWidth = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI");
        FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent(note, dueAt);
    }

    public static ReminderAlertResult ShowFor(StickyNoteWindow owner, DateTime dueAt)
    {
        var dialog = new ReminderAlertWindow(ReminderAlertNote.From(owner), dueAt)
        {
            Owner = owner,
            Topmost = true,
        };
        dialog.ShowDialog();
        return dialog._result;
    }

    private UIElement BuildContent(ReminderAlertNote note, DateTime dueAt)
    {
        var root = new StackPanel();

        // 付箋のタイトルバーと同じ色の見出し。左にアクセント色の帯を引く。
        var titleRow = new StackPanel { Orientation = WpfOrientation.Horizontal };
        if (note.Icon != null)
            titleRow.Children.Add(new WpfImage { Source = note.Icon, Width = 22, Height = 22, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock
        {
            Text = note.Title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = note.Foreground,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 340, VerticalAlignment = VerticalAlignment.Center,
        });
        root.Children.Add(new Border
        {
            Name = "NoteHeader",
            Background = note.Background,
            BorderBrush = note.Accent,
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(16, 14, 16, 14),
            Child = titleRow,
        });

        var time = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(20, 14, 20, 16) };
        time.Children.Add(new TextBlock { Text = UiIcons.Clock, FontFamily = UiIcons.Font, FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        time.Children.Add(new TextBlock
        {
            Text = string.Format(LocalizationService.T("ReminderDueMessage"), dueAt.ToString("yyyy/MM/dd HH:mm")),
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
        });
        root.Children.Add(time);

        // 下の帯: 左にスヌーズのまとまり、右に「付箋を開く」「完了」。
        var footer = new DockPanel { LastChildFill = false };
        var snooze = new StackPanel { Orientation = WpfOrientation.Horizontal };
        snooze.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("ReminderSnoozeLabel"), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0), Opacity = 0.75,
        });
        AddSnoozeButton(snooze, LocalizationService.T("ReminderSnooze5"), TimeSpan.FromMinutes(5));
        AddSnoozeButton(snooze, LocalizationService.T("ReminderSnooze15"), TimeSpan.FromMinutes(15));
        AddSnoozeButton(snooze, LocalizationService.T("ReminderSnooze60"), TimeSpan.FromHours(1));
        DockPanel.SetDock(snooze, Dock.Left);
        footer.Children.Add(snooze);

        var done = BuildButton(LocalizationService.T("ReminderDismiss"), 80);
        done.Name = "DoneButton";
        done.IsDefault = true;
        done.SetResourceReference(StyleProperty, "AccentButton");
        done.Click += (_, _) =>
        {
            _result = new ReminderAlertResult(false, null);
            DialogResult = true;
        };
        var open = BuildButton(LocalizationService.T("ReminderOpenNote"), 80);
        open.Name = "OpenNoteButton";
        open.Margin = new Thickness(0, 0, 8, 0);
        open.Click += (_, _) =>
        {
            _result = new ReminderAlertResult(false, null, OpenNote: true);
            DialogResult = true;
        };
        var right = new StackPanel { Orientation = WpfOrientation.Horizontal };
        right.Children.Add(open);
        right.Children.Add(done);
        DockPanel.SetDock(right, Dock.Right);
        footer.Children.Add(right);

        var footerBorder = new Border
        {
            Padding = new Thickness(16, 12, 16, 12), BorderThickness = new Thickness(0, 1, 0, 0), Child = footer,
        };
        footerBorder.SetResourceReference(Border.BackgroundProperty, "SettingsFooter");
        footerBorder.SetResourceReference(Border.BorderBrushProperty, "SettingsDivider");
        root.Children.Add(footerBorder);
        root.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "SettingsBackground");
        return root;
    }

    private void AddSnoozeButton(System.Windows.Controls.Panel panel, string label, TimeSpan delay)
    {
        var button = BuildButton(label, 52);
        button.Margin = new Thickness(0, 0, 4, 0);
        button.Click += (_, _) =>
        {
            _result = new ReminderAlertResult(true, delay);
            DialogResult = true;
        };
        panel.Children.Add(button);
    }

    private static WpfButton BuildButton(string text, double minWidth)
        => new()
        {
            Content = text,
            MinWidth = minWidth,
            Height = 30,
            Padding = new Thickness(10, 0, 10, 0),
        };
}

/// <param name="OpenNote">「付箋を開く」を押した。完了として扱い、付箋を前に出して操作できるようにする。</param>
public sealed record ReminderAlertResult(bool Snoozed, TimeSpan? SnoozeDelay, bool OpenNote = false);
