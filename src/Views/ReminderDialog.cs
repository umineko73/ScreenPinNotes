// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ScreenPinNotes.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfPanel = System.Windows.Controls.Panel;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace ScreenPinNotes.Views;

public sealed class ReminderDialog : Window
{
    private readonly WpfTextBox _dateBox = new();
    private readonly WpfTextBox _timeBox = new();
    private readonly TextBlock _errorText = new();
    private DateTime? _selectedAt;
    private bool _clearRequested;

    public ReminderDialog(DateTime? currentAt)
    {
        Title = LocalizationService.T("ReminderDialogTitle");
        Width = 360;
        MinWidth = 340;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var initial = currentAt ?? DateTime.Now.AddMinutes(15);
        _dateBox.Text = initial.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        _timeBox.Text = initial.ToString("HH:mm", CultureInfo.InvariantCulture);

        Content = BuildContent();
    }

    public ReminderDialogResult Result =>
        new(DialogResult == true, _clearRequested, _selectedAt);

    public static ReminderDialogResult ShowFor(Window owner, DateTime? currentAt)
    {
        var dialog = new ReminderDialog(currentAt)
        {
            Owner = owner,
            Topmost = owner.Topmost,
        };
        dialog.ShowDialog();
        return dialog.Result;
    }

    private UIElement BuildContent()
    {
        var root = new StackPanel { Margin = new Thickness(14) };

        root.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("ReminderDialogDescription"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        var inputGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddLabeledInput(inputGrid, 0, LocalizationService.T("ReminderDate"), _dateBox);
        AddLabeledInput(inputGrid, 1, LocalizationService.T("ReminderTime"), _timeBox);
        root.Children.Add(inputGrid);

        var presets = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        AddPresetButton(presets, LocalizationService.T("ReminderAfter5"), TimeSpan.FromMinutes(5));
        AddPresetButton(presets, LocalizationService.T("ReminderAfter15"), TimeSpan.FromMinutes(15));
        AddPresetButton(presets, LocalizationService.T("ReminderAfter60"), TimeSpan.FromHours(1));
        AddPresetButton(presets, LocalizationService.T("ReminderTomorrow"), DateTime.Now.Date.AddDays(1).AddHours(9));
        root.Children.Add(presets);

        _errorText.Foreground = System.Windows.Media.Brushes.Firebrick;
        _errorText.TextWrapping = TextWrapping.Wrap;
        _errorText.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(_errorText);

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        };
        var clearButton = BuildButton(LocalizationService.T("ReminderClear"));
        var cancelButton = BuildButton(LocalizationService.T("Cancel"));
        var okButton = BuildButton("OK");
        okButton.IsDefault = true;
        cancelButton.IsCancel = true;
        clearButton.Click += (_, _) =>
        {
            _clearRequested = true;
            _selectedAt = null;
            DialogResult = true;
        };
        cancelButton.Click += (_, _) => DialogResult = false;
        okButton.Click += (_, _) => Accept();
        buttons.Children.Add(clearButton);
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(okButton);
        root.Children.Add(buttons);

        Loaded += (_, _) =>
        {
            _dateBox.Focus();
            _dateBox.SelectAll();
        };
        return root;
    }

    private static void AddLabeledInput(Grid grid, int row, string label, WpfTextBox box)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 6),
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        box.Margin = new Thickness(0, 0, 0, 6);
        box.MinWidth = 180;
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
    }

    private void AddPresetButton(WpfPanel panel, string label, TimeSpan delay)
        => AddPresetButton(panel, label, DateTime.Now.Add(delay));

    private void AddPresetButton(WpfPanel panel, string label, DateTime dateTime)
    {
        var button = BuildButton(label);
        button.Click += (_, _) =>
        {
            _dateBox.Text = dateTime.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
            _timeBox.Text = dateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
            _errorText.Text = "";
        };
        panel.Children.Add(button);
    }

    private static WpfButton BuildButton(string text)
        => new()
        {
            Content = text,
            MinWidth = 72,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(8, 3, 8, 3),
        };

    private void Accept()
    {
        var raw = $"{_dateBox.Text.Trim()} {_timeBox.Text.Trim()}";
        var formats = new[]
        {
            "yyyy/M/d H:m",
            "yyyy/M/d HH:mm",
            "yyyy/MM/dd H:m",
            "yyyy/MM/dd HH:mm",
        };
        if (!DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var nextAt) &&
            !DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out nextAt))
        {
            _errorText.Text = LocalizationService.T("ReminderInvalid");
            return;
        }

        _selectedAt = nextAt;
        _clearRequested = false;
        DialogResult = true;
    }
}

public sealed record ReminderDialogResult(bool Accepted, bool ClearRequested, DateTime? NextAt);
