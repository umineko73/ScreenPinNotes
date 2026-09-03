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
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace ScreenPinNotes.Views;

/// <summary>付箋を所有者として、その近くに表示する確認ダイアログ。</summary>
public sealed class ConfirmationDialog : Window
{
    private ConfirmationDialog(string title, string message)
    {
        Title = title;
        Width = 380;
        MinWidth = 320;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
        };
        var yesButton = BuildButton(LocalizationService.T("Yes"));
        var noButton = BuildButton(LocalizationService.T("No"));
        noButton.Margin = new Thickness(0);
        noButton.IsCancel = true;
        noButton.IsDefault = true;
        noButton.Click += (_, _) => DialogResult = false;
        yesButton.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(yesButton);
        buttons.Children.Add(noButton);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => noButton.Focus();
    }

    public static bool ShowFor(Window owner, string message, string title)
    {
        var dialog = new ConfirmationDialog(title, message)
        {
            Owner = owner,
            Topmost = owner.Topmost,
        };
        return dialog.ShowDialog() == true;
    }

    private static WpfButton BuildButton(string text) => new()
    {
        Content = text,
        MinWidth = 76,
        Margin = new Thickness(0, 0, 8, 0),
        Padding = new Thickness(10, 3, 10, 3),
    };
}
