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
using System.Windows.Controls;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

/// <summary>
/// リマインダーを設定してある付箋は、どこをホバーしても設定内容が読める。
/// </summary>
public class NoteReminderTooltipTests
{
    [WpfFact]
    public void HoveringAnywhereOnTheNoteShowsTheReminderUntilItIsCleared()
    {
        WpfApplicationFixture.Ensure();
        using var temp = new TempDataDirectory();
        var note = new StickyNote
        {
            Content = "本文",
            Reminder = new ReminderSettings
            {
                NextAt = new DateTime(2030, 5, 1, 9, 30, 0),
                Recurrence = "None",
                WindowsNotification = true,
                ShowAlert = false,
                FlashNote = true,
            },
        };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, App.Current.Settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var root = Assert.IsType<System.Windows.Controls.Border>(window.FindName("RootBorder"));
            var body = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var summary = window.ViewModel.ReminderTooltip;
            var hint = LocalizationService.T("EditBodyTooltip");

            Assert.NotNull(summary);
            // 付箋の地（タイトルバーや余白）と、本文そのもの。
            Assert.Equal(summary, root.ToolTip);
            var bodyTooltip = Assert.IsType<string>(body.ToolTip);
            Assert.StartsWith(summary, bodyTooltip);
            Assert.EndsWith(hint, bodyTooltip);

            // 解除したら消える。付箋を開き直さなくても切り替わること。
            window.ViewModel.SetReminder(null);
            window.UpdateLayout();
            Assert.Null(root.ToolTip);
            Assert.Equal(hint, body.ToolTip);
        }
        finally { window.Close(); }
    }

    // 「ダブルクリックして編集」は、WPF の既定のままだとマウスが本文の上にある間ずっと出ていて、
    // 読んでいる文字を隠す。数秒で消える。設定を変えれば開き直さなくても変わる。
    [WpfFact]
    public void BodyTooltipDisappearsAfterAFewSeconds()
    {
        WpfApplicationFixture.Ensure();
        using var temp = new TempDataDirectory();
        var settings = App.Current.Settings;
        var previous = settings.Timings.ContentTooltipDurationMs;
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(new StickyNote { Content = "本文" }, settings), new StorageService(temp.Path));
        try
        {
            var body = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            Assert.Equal(4000, ToolTipService.GetShowDuration(body));

            settings.Timings.ContentTooltipDurationMs = 2500;
            window.RefreshSettings();
            Assert.Equal(2500, ToolTipService.GetShowDuration(body));
        }
        finally
        {
            settings.Timings.ContentTooltipDurationMs = previous;
            window.Close();
        }
    }

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
