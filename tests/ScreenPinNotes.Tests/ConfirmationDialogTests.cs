// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3 of the License.

using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenPinNotes.Services;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

public class ConfirmationDialogTests
{
    private static Window Create(DialogKind kind, string confirm, string? cancel)
    {
        var ctor = typeof(ConfirmationDialog).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        return (Window)ctor.Invoke(["title", "message", kind, confirm, cancel]);
    }

    // 削除の確認は「削除」「キャンセル」。Enter で消えないよう、既定はキャンセル。
    [WpfFact]
    public void DangerDialog_NamesTheActionAndDefaultsToCancel()
    {
        WpfApplicationFixture.Ensure();
        var dialog = Create(DialogKind.Danger, LocalizationService.T("DeleteButton", "ja"), LocalizationService.T("Cancel", "ja"));
        try
        {
            dialog.Show();
            dialog.UpdateLayout();
            var buttons = Descendants<Button>(dialog).ToArray();
            var confirm = buttons.Single(b => b.Name == "ConfirmButton");
            var cancel = buttons.Single(b => b.Name == "CancelButton");
            Assert.Equal("削除", confirm.Content);
            Assert.Equal("キャンセル", cancel.Content);
            Assert.True(cancel.IsDefault);
            Assert.True(cancel.IsCancel);
            Assert.False(confirm.IsDefault);
        }
        finally { dialog.Close(); }
    }

    [WpfFact]
    public void InformDialog_HasOnlyAnOkButton()
    {
        WpfApplicationFixture.Ensure();
        var dialog = Create(DialogKind.Info, "OK", null);
        try
        {
            dialog.Show();
            dialog.UpdateLayout();
            var button = Assert.Single(Descendants<Button>(dialog));
            Assert.True(button.IsDefault);
            Assert.True(button.IsCancel);
        }
        finally { dialog.Close(); }
    }

    // Windows 標準のメッセージボックスはテーマに従わないので、アプリの中では使わない。
    [Fact]
    public void Sources_DoNotUseTheSystemMessageBox()
    {
        var src = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", "..", "src"));
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("MessageBox.Show"))
            .ToArray();
        Assert.Empty(offenders);
    }

    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
