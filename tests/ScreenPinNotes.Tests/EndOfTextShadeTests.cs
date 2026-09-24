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
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

/// <summary>
/// 編集中に本文の終わりから下を濃くする帯（EndOfTextShade）の出方。
/// どこまでが本文かを、編集中だけ地の色で示す。
/// </summary>
public class EndOfTextShadeTests
{
    [WpfFact]
    public void ShadeStartsBelowTheLastLineWhileEditing()
    {
        var window = CreateWindow("一行目\n二行目", out var temp);
        using (temp)
        {
            try
            {
                Edit(window);
                var shade = Find<Border>(window, "EndOfTextShade");
                var box = Find<TextBox>(window, "BodyEditBox");
                var lineHeight = box.GetRectFromCharacterIndex(0, true).Height;

                Assert.Equal(Visibility.Visible, shade.Visibility);
                // 2行ぶんの下から、欄の下端まで。
                Assert.InRange(shade.Margin.Top, lineHeight, box.ActualHeight - 1);
                Assert.Equal(0, shade.Margin.Bottom);
            }
            finally { window.Close(); }
        }
    }

    // 本文が欄の下まで届いているときは、示す空きが無いので出さない。
    [WpfFact]
    public void ShadeIsHiddenWhenTheTextFillsTheBox()
    {
        var window = CreateWindow(string.Join("\n", Enumerable.Repeat("本文", 200)), out var temp);
        using (temp)
        {
            try
            {
                Edit(window);
                Assert.Equal(Visibility.Collapsed, Find<Border>(window, "EndOfTextShade").Visibility);
            }
            finally { window.Close(); }
        }
    }

    // 閲覧中は出さない。編集していることの手掛かりも兼ねる。
    [WpfFact]
    public void ShadeIsHiddenInViewMode()
    {
        var window = CreateWindow("一行目", out var temp);
        using (temp)
        {
            try
            {
                var shade = Find<Border>(window, "EndOfTextShade");
                Assert.Equal(Visibility.Collapsed, shade.Visibility);

                Edit(window);
                Assert.Equal(Visibility.Visible, shade.Visibility);

                InvokePrivate(window, "EnterViewMode");
                window.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, shade.Visibility);
            }
            finally { window.Close(); }
        }
    }

    private static StickyNoteWindow CreateWindow(string content, out TempDataDirectory temp)
    {
        WpfApplicationFixture.Ensure();
        temp = new TempDataDirectory();
        var note = new StickyNote
        {
            Width = 400, Height = 300, EditWidth = 400, EditHeight = 300,
            Content = content,
        };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, App.Current.Settings), new StorageService(temp.Path));
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static void Edit(StickyNoteWindow window)
    {
        InvokePrivate(window, "EnterEditMode");
        window.UpdateLayout();
        // 帯の位置合わせは入力が片付いてからに回されるので、ここで直接呼ぶ。
        InvokePrivate(window, "UpdateEndOfTextShade");
    }

    private static T Find<T>(StickyNoteWindow window, string name) where T : class
        => Assert.IsType<T>(window.FindName(name));

    private static void InvokePrivate(object target, string methodName)
        => target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target, []);

    private sealed class TempDataDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
