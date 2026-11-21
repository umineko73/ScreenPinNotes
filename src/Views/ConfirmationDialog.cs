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
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;
using WpfColor = System.Windows.Media.Color;

namespace ScreenPinNotes.Views;

/// <summary>ダイアログの種類。アイコンと、確定ボタンの色を決める。</summary>
public enum DialogKind
{
    Info,
    Question,
    Warning,
    Error,
    /// <summary>削除など元に戻しにくい操作。確定ボタンを赤くする。</summary>
    Danger,
}

/// <summary>
/// アプリのテーマに合わせた確認・お知らせのダイアログ。Windows 標準のメッセージボックスは
/// ダークテーマでも白い箱で出るので使わない。ボタンには「はい／いいえ」ではなく、
/// 何が起きるかを書く（「削除」「キャンセル」など）。付箋を所有者にすると、その近くに出る。
/// </summary>
public sealed class ConfirmationDialog : Window
{
    private ConfirmationDialog(string title, string message, DialogKind kind,
        string confirmLabel, string? cancelLabel)
    {
        Title = title;
        ControlTheme.Apply(this, App.Current.Settings.IsDarkTheme, dialog: true);
        Width = 420;
        MinWidth = 320;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI");
        FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var body = new Grid { Margin = new Thickness(20, 20, 20, 16) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var (glyph, color) = Glyph(kind);
        var icon = new TextBlock
        {
            Text = glyph, FontFamily = UiIcons.Font, FontSize = 22,
            Foreground = new SolidColorBrush(color),
            VerticalAlignment = WpfVerticalAlignment.Top, Margin = new Thickness(0, 1, 14, 0),
        };
        body.Children.Add(icon);
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        });
        text.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(text, 1);
        body.Children.Add(text);

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
        };
        var confirm = BuildButton(confirmLabel);
        confirm.Name = "ConfirmButton";
        confirm.SetResourceReference(StyleProperty, kind == DialogKind.Danger ? "DangerButton" : "AccentButton");
        confirm.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(confirm);
        WpfButton focus = confirm;
        if (cancelLabel != null)
        {
            var cancel = BuildButton(cancelLabel);
            cancel.Name = "CancelButton";
            cancel.Margin = new Thickness(8, 0, 0, 0);
            cancel.IsCancel = true;
            // 元に戻しにくい操作は、Enter で確定しないよう既定をキャンセルにする。
            if (kind is DialogKind.Danger or DialogKind.Warning) cancel.IsDefault = true;
            else confirm.IsDefault = true;
            cancel.Click += (_, _) => DialogResult = false;
            buttons.Children.Add(cancel);
            if (cancel.IsDefault) focus = cancel;
        }
        else
        {
            confirm.IsDefault = true;
            confirm.IsCancel = true;
        }

        var footer = new Border
        {
            Padding = new Thickness(20, 12, 20, 12), BorderThickness = new Thickness(0, 1, 0, 0),
            Child = buttons,
        };
        footer.SetResourceReference(Border.BackgroundProperty, "SettingsFooter");
        footer.SetResourceReference(Border.BorderBrushProperty, "SettingsDivider");

        var root = new DockPanel();
        root.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "SettingsBackground");
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = root;
        Loaded += (_, _) => focus.Focus();
    }

    private static (string Glyph, WpfColor Color) Glyph(DialogKind kind) => kind switch
    {
        DialogKind.Warning => ("", WpfColor.FromRgb(0xC1, 0x8A, 0x00)),
        DialogKind.Error => ("", WpfColor.FromRgb(0xC4, 0x2B, 0x1C)),
        DialogKind.Danger => ("", WpfColor.FromRgb(0xC4, 0x2B, 0x1C)),
        DialogKind.Question => ("", WpfColor.FromRgb(0x00, 0x5F, 0xB8)),
        _ => (UiIcons.Info, WpfColor.FromRgb(0x00, 0x5F, 0xB8)),
    };

    /// <summary>
    /// 確認ダイアログ。確定したら true。<paramref name="owner"/> が無いとき（トレイからの操作など）は
    /// 画面の中央に、ほかのウィンドウより前に出す。
    /// </summary>
    public static bool Confirm(Window? owner, string message, string title, string confirmLabel,
        DialogKind kind = DialogKind.Question, string? cancelLabel = null)
    {
        var dialog = new ConfirmationDialog(title, message, kind, confirmLabel,
            cancelLabel ?? LocalizationService.T("Cancel"));
        return Show(dialog, owner);
    }

    /// <summary>お知らせ（ボタンは「OK」だけ）。</summary>
    public static void Inform(Window? owner, string message, string title, DialogKind kind = DialogKind.Info)
        => Show(new ConfirmationDialog(title, message, kind, LocalizationService.T("OK"), null), owner);

    /// <summary>付箋や画像の削除の確認。確定ボタンは「削除」。</summary>
    public static bool ShowFor(Window owner, string message, string title)
        => Confirm(owner, message, title, LocalizationService.T("DeleteButton"), DialogKind.Danger);

    private static bool Show(ConfirmationDialog dialog, Window? owner)
    {
        if (owner is { IsLoaded: true })
        {
            dialog.Owner = owner;
            dialog.Topmost = owner.Topmost;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Topmost = true;
        }
        return dialog.ShowDialog() == true;
    }

    private static WpfButton BuildButton(string text) => new()
    {
        Content = text,
        MinWidth = 96,
        Height = 32,
        Padding = new Thickness(14, 0, 14, 0),
    };
}
