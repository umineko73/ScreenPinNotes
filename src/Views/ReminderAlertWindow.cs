// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Windows;
using System.Windows.Controls;
using ScreenPinNotes.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfPanel = System.Windows.Controls.Panel;

namespace ScreenPinNotes.Views;

public sealed class ReminderAlertWindow : Window
{
    private ReminderAlertResult _result = new(false, null);

    public ReminderAlertWindow(string title, DateTime dueAt)
    {
        Title = LocalizationService.T("ReminderDueTitle");
        Width = 360;
        MinWidth = 340;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent(title, dueAt);
    }

    public static ReminderAlertResult ShowFor(Window owner, string noteTitle, DateTime dueAt)
    {
        var dialog = new ReminderAlertWindow(noteTitle, dueAt)
        {
            Owner = owner,
            Topmost = true,
        };
        dialog.ShowDialog();
        return dialog._result;
    }

    private UIElement BuildContent(string title, DateTime dueAt)
    {
        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        root.Children.Add(new TextBlock
        {
            Text = string.Format(LocalizationService.T("ReminderDueMessage"), dueAt.ToString("yyyy/MM/dd HH:mm")),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        };
        AddSnoozeButton(buttons, LocalizationService.T("ReminderSnooze5"), TimeSpan.FromMinutes(5));
        AddSnoozeButton(buttons, LocalizationService.T("ReminderSnooze15"), TimeSpan.FromMinutes(15));
        AddSnoozeButton(buttons, LocalizationService.T("ReminderSnooze60"), TimeSpan.FromHours(1));

        var doneButton = BuildButton(LocalizationService.T("ReminderDismiss"));
        doneButton.IsDefault = true;
        doneButton.Click += (_, _) =>
        {
            _result = new ReminderAlertResult(false, null);
            DialogResult = true;
        };
        buttons.Children.Add(doneButton);
        root.Children.Add(buttons);
        return root;
    }

    private void AddSnoozeButton(WpfPanel panel, string label, TimeSpan delay)
    {
        var button = BuildButton(label);
        button.Click += (_, _) =>
        {
            _result = new ReminderAlertResult(true, delay);
            DialogResult = true;
        };
        panel.Children.Add(button);
    }

    private static WpfButton BuildButton(string text)
        => new()
        {
            Content = text,
            MinWidth = 64,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(8, 3, 8, 3),
        };
}

public sealed record ReminderAlertResult(bool Snoozed, TimeSpan? SnoozeDelay);
