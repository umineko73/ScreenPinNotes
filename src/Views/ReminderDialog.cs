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
using ScreenPinNotes.Models;
using WpfButton = System.Windows.Controls.Button;
using WpfPanel = System.Windows.Controls.Panel;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace ScreenPinNotes.Views;

public sealed class ReminderDialog : Window
{
    private readonly DatePicker _dateBox = new() { SelectedDateFormat = DatePickerFormat.Short };
    private readonly WpfTextBox _timeBox = new();
    private readonly TextBlock _errorText = new();
    private DateTime? _selectedAt;
    private bool _clearRequested;
    private readonly System.Windows.Controls.ComboBox _repeat = new() { MinHeight = 32, Width = 220, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
    private readonly WrapPanel _days = new();
    private readonly System.Windows.Controls.ComboBox _monthDay = new() { Width = 100, MinHeight = 32, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
    private readonly System.Windows.Controls.CheckBox _windows = new() { IsChecked = true };
    private readonly System.Windows.Controls.CheckBox _alert = new();
    private readonly System.Windows.Controls.CheckBox _flash = new();
    private ReminderSettings? _resultSettings;

    public ReminderDialog(DateTime? currentAt) : this(currentAt, null) { }

    private ReminderDialog(DateTime? currentAt, ReminderSettings? current)
    {
        Title = LocalizationService.T("ReminderDialogTitle");
        Width = 520;
        MinWidth = 340;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        var registered = false;
        Loaded += (_, _) =>
        {
            if (registered) return;
            registered = true;
            App.Current.ReminderDialogOpened();
        };
        Closed += (_, _) =>
        {
            if (registered) App.Current.ReminderDialogClosed();
        };
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI");
        FontSize = 13;
        var dark = string.Equals(App.Current.Settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase);
        System.Windows.Media.Brush Color(string value) => new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));
        Background = Color(dark ? "#202020" : "#FFFFFF");
        Foreground = Color(dark ? "#EEEEEE" : "#242424");
        Resources["SettingsText"] = Foreground;
        Resources["SettingsSurface"] = Color(dark ? "#303030" : "#FFFFFF");
        Resources["SettingsBorder"] = Color(dark ? "#555555" : "#CCCCCC");
        Resources["SettingsHover"] = Color(dark ? "#444444" : "#EEEEEE");
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/ScreenPinNotes;component/Resources/SettingsStyles.xaml") });

        var initial = currentAt ?? DateTime.Now.AddMinutes(15);
        _dateBox.SelectedDate = initial.Date;
        _dateBox.Language = System.Windows.Markup.XmlLanguage.GetLanguage(LocalizationService.ResolveLanguage(App.Current.Settings.Language));
        _dateBox.DateValidationError += (_, e) => { e.ThrowException = false; _errorText.Text = LocalizationService.T("ReminderInvalid"); };
        _timeBox.Text = initial.ToString("HH:mm", CultureInfo.InvariantCulture);
        foreach (var mode in new[] { "None", "Daily", "Weekly", "Monthly" })
            _repeat.Items.Add(new ComboBoxItem { Content = LocalizationService.T("ReminderRepeat" + mode), Tag = mode });
        _repeat.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "None", "Daily", "Weekly", "Monthly" }, current?.Recurrence ?? "None"));
        for (int i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)((i + 1) % 7);
            _days.Children.Add(new System.Windows.Controls.CheckBox
            {
                Content = CultureInfo.GetCultureInfo(LocalizationService.ResolveLanguage(App.Current.Settings.Language)).DateTimeFormat.GetAbbreviatedDayName(day),
                Tag = day, Margin = new Thickness(0, 6, 14, 6),
                IsChecked = current?.WeekDays.Contains(day) ?? day == initial.DayOfWeek,
            });
        }
        for (int i = 1; i <= 31; i++) _monthDay.Items.Add(i);
        _monthDay.SelectedItem = current?.MonthDay ?? initial.Day;
        _windows.Content = LocalizationService.T("ReminderWindowsNotification");
        _windows.IsChecked = current?.WindowsNotification ?? true;
        _alert.Content = LocalizationService.T("ReminderShowAlert");
        _alert.IsChecked = current?.ShowAlert ?? false;
        _flash.Content = LocalizationService.T("ReminderFlashNote");
        _flash.IsChecked = current?.FlashNote ?? true;

        Content = BuildContent();
    }

    public ReminderDialogResult Result =>
        new(DialogResult == true, _clearRequested, _selectedAt, _resultSettings);

    public static ReminderDialogResult ShowFor(Window owner, ReminderSettings? current)
    {
        var dialog = new ReminderDialog(current?.NextAt, current)
        {
            Owner = owner,
        };
        dialog.ShowDialog();
        return dialog.Result;
    }

    private UIElement BuildContent()
    {
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });

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

        var zeroMinutes = BuildButton(LocalizationService.T("ReminderZeroMinutes"));
        zeroMinutes.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        zeroMinutes.Margin = new Thickness(104, 0, 0, 12);
        zeroMinutes.Click += (_, _) => SetZeroMinutes();
        root.Children.Add(zeroMinutes);

        root.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("ReminderAdjustHint"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        var presets = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        AddPresetButton(presets, LocalizationService.T("ReminderAdd5"), TimeSpan.FromMinutes(5));
        AddPresetButton(presets, LocalizationService.T("ReminderAdd10"), TimeSpan.FromMinutes(10));
        AddPresetButton(presets, LocalizationService.T("ReminderAdd60"), TimeSpan.FromHours(1));
        var reset = BuildButton(LocalizationService.T("ReminderResetNow"));
        reset.Margin = new Thickness(0, 0, 6, 6);
        reset.Click += (_, _) => ResetToNow();
        presets.Children.Add(reset);
        root.Children.Add(presets);
        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 16) });
        root.Children.Add(new TextBlock { Text = LocalizationService.T("ReminderRepeat"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        root.Children.Add(_repeat);
        root.Children.Add(_days);
        root.Children.Add(_monthDay);
        var hint = new TextBlock { Text = LocalizationService.T("ReminderMonthlyHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) };
        root.Children.Add(hint);
        void UpdateRepeat()
        {
            var mode = (_repeat.SelectedItem as ComboBoxItem)?.Tag as string;
            _days.Visibility = mode == "Weekly" ? Visibility.Visible : Visibility.Collapsed;
            _monthDay.Visibility = hint.Visibility = mode == "Monthly" ? Visibility.Visible : Visibility.Collapsed;
        }
        _repeat.SelectionChanged += (_, _) => UpdateRepeat();
        UpdateRepeat();
        root.Children.Add(new Separator { Margin = new Thickness(0, 16, 0, 16) });
        root.Children.Add(_windows);
        _alert.Margin = new Thickness(0, 10, 0, 10);
        root.Children.Add(_alert);
        _flash.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(_flash);
        root.Children.Add(new TextBlock { Text = LocalizationService.T("ReminderRunningHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16), Foreground = System.Windows.Media.Brushes.Gray });

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
        };
        return new Border { Padding = new Thickness(24), Background = Background, Child = root };
    }

    private static void AddLabeledInput(Grid grid, int row, string label, System.Windows.Controls.Control box)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 6),
            Width = 96,
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        box.Margin = new Thickness(0, 0, 0, 6);
        box.MinWidth = 180;
        box.Width = 220;
        box.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        box.MinHeight = 32;
        box.VerticalContentAlignment = VerticalAlignment.Center;
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
    }

    private void AddPresetButton(WpfPanel panel, string label, TimeSpan delay)
    {
        var button = BuildButton(label);
        button.Margin = new Thickness(0, 0, 6, 6);
        button.Click += (_, _) => AddToSelectedTime(delay);
        panel.Children.Add(button);
    }

    private void SetDateTime(DateTime value)
    {
        _dateBox.SelectedDate = value.Date;
        _timeBox.Text = value.ToString("HH:mm", CultureInfo.InvariantCulture);
        _errorText.Text = "";
    }

    private void ResetToNow() => SetDateTime(DateTime.Now);

    private void AddToSelectedTime(TimeSpan delay)
    {
        if (!TryReadDateTime(out var selected)) return;
        try { SetDateTime(selected.Add(delay)); }
        catch (ArgumentOutOfRangeException) { _errorText.Text = LocalizationService.T("ReminderInvalid"); }
    }

    private bool TryReadDateTime(out DateTime value)
    {
        value = default;
        if (!DateTime.TryParse(_dateBox.Text, CultureInfo.GetCultureInfo(_dateBox.Language.IetfLanguageTag), DateTimeStyles.None, out var date) ||
            !DateTime.TryParseExact(_timeBox.Text.Trim(), new[] { "H:m", "HH:mm", "H:mm", "HH:m" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            _errorText.Text = LocalizationService.T("ReminderInvalid");
            return false;
        }
        value = date.Date.Add(time.TimeOfDay);
        return true;
    }

    private static WpfButton BuildButton(string text)
        => new()
        {
            Content = text,
            MinWidth = 72,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(8, 3, 8, 3),
        };

    private void SetZeroMinutes()
    {
        if (!DateTime.TryParseExact(_timeBox.Text.Trim(), new[] { "H:m", "HH:mm", "H:mm", "HH:m" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            _errorText.Text = LocalizationService.T("ReminderInvalid");
            return;
        }
        _timeBox.Text = time.ToString("HH", CultureInfo.InvariantCulture) + ":00";
        _errorText.Text = "";
    }

    private void Accept()
    {
        if (!TryReadDateTime(out var nextAt)) return;

        var settings = new ReminderSettings
        {
            NextAt = nextAt,
            TimeOfDay = nextAt.TimeOfDay,
            Recurrence = (string)((ComboBoxItem)_repeat.SelectedItem).Tag,
            WeekDays = _days.Children.OfType<System.Windows.Controls.CheckBox>().Where(b => b.IsChecked == true).Select(b => (DayOfWeek)b.Tag).ToList(),
            MonthDay = (int)(_monthDay.SelectedItem ?? 1),
            WindowsNotification = _windows.IsChecked == true,
            ShowAlert = _alert.IsChecked == true,
            FlashNote = _flash.IsChecked == true,
        };
        if ((!settings.WindowsNotification && settings.ShowAlert != true && !settings.FlashNote) || (settings.Recurrence == "Weekly" && settings.WeekDays.Count == 0))
        {
            _errorText.Text = LocalizationService.T("ReminderChooseOptions");
            return;
        }
        if (settings.Recurrence != "None")
            settings.NextAt = ReminderSchedule.Next(settings, nextAt > DateTime.Now ? nextAt.AddTicks(-1) : DateTime.Now);
        if (settings.NextAt == null || settings.NextAt <= DateTime.Now)
        {
            _errorText.Text = LocalizationService.T("ReminderFutureRequired");
            return;
        }
        _resultSettings = settings;
        _selectedAt = settings.NextAt;
        _clearRequested = false;
        DialogResult = true;
    }
}

public sealed record ReminderDialogResult(bool Accepted, bool ClearRequested, DateTime? NextAt, ReminderSettings? Settings = null);
