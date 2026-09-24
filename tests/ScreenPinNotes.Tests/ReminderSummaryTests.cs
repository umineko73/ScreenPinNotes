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

using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

public class ReminderSummaryTests
{
    private static ReminderSettings Settings() => new()
    {
        NextAt = new DateTime(2030, 5, 1, 9, 30, 0),
        Recurrence = "None",
        WindowsNotification = true,
        ShowAlert = false,
        FlashNote = true,
    };

    [Fact]
    public void Describe_WithoutAReminder_IsNothing()
    {
        Assert.Null(ReminderSummary.Describe(null, "ja"));
        Assert.Null(ReminderSummary.Describe(new ReminderSettings(), "ja"));
    }

    [Fact]
    public void Describe_Once_Japanese()
        => Assert.Equal(
            "次回: 2030/05/01 09:30\n繰り返し: 1回のみ\n通知: Windowsの通知・付箋を点滅",
            ReminderSummary.Describe(Settings(), "ja"));

    [Fact]
    public void Describe_Once_English()
        => Assert.Equal(
            "Next: 2030/05/01 09:30\nRepeat: Once\nNotify: Windows notification, Flash the note",
            ReminderSummary.Describe(Settings(), "en"));

    // 曜日は設定画面と同じ月曜始まりで並べ、第n週を選んでいればそれも添える。
    [Fact]
    public void Describe_Weekly_ListsDaysInWeekOrderAndTheChosenWeeks()
    {
        var settings = Settings();
        settings.Recurrence = "Weekly";
        settings.WeekDays = [DayOfWeek.Wednesday, DayOfWeek.Monday];
        settings.MonthWeeks = [4, 2];
        settings.WindowsNotification = false;
        settings.ShowAlert = true;
        settings.FlashNote = false;

        var summary = ReminderSummary.Describe(settings, "ja");

        Assert.Contains("繰り返し: 毎週 月・水（2週目・4週目）", summary);
        Assert.Contains("通知: 通知ウィンドウ", summary);
    }

    // 週を選んでいなければ毎週。設定画面の「毎週」チェックと同じ意味。
    [Fact]
    public void Describe_WeeklyWithoutWeeks_IsEveryWeek()
    {
        var settings = Settings();
        settings.Recurrence = "Weekly";
        settings.WeekDays = [DayOfWeek.Sunday];

        Assert.Contains("繰り返し: 毎週 日", ReminderSummary.Describe(settings, "ja"));
    }

    [Fact]
    public void Describe_Monthly_UsesTheDayOrTheLastDay()
    {
        var settings = Settings();
        settings.Recurrence = "Monthly";
        settings.MonthDay = 15;
        Assert.Contains("Repeat: Every month day 15", ReminderSummary.Describe(settings, "en"));

        settings.MonthLastDay = true;
        Assert.Contains("繰り返し: 毎月 最終日", ReminderSummary.Describe(settings, "ja"));
    }

    // 通知方法が1つも残っていない古い設定でも、見出しだけの行を作らない。
    [Fact]
    public void Describe_WithoutAnyMethod_LeavesOutTheNotifyLine()
    {
        var settings = Settings();
        settings.WindowsNotification = false;
        settings.ShowAlert = false;
        settings.FlashNote = false;

        Assert.DoesNotContain("通知:", ReminderSummary.Describe(settings, "ja"));
    }

    // 設定画面の「現在の設定」は、ON / OFF を見出しにして中身を続ける。
    [Fact]
    public void DialogStatus_ShowsOnOrOffWithTheDetails()
    {
        Assert.Equal("リマインダー: OFF", ReminderDialog.StatusHeading(null, "ja"));
        Assert.Equal("この付箋にはリマインダーが設定されていません。", ReminderDialog.StatusDetail(null, "ja"));

        var settings = Settings();
        Assert.Equal("リマインダー: ON", ReminderDialog.StatusHeading(settings, "ja"));
        Assert.Equal(ReminderSummary.Describe(settings, "ja"), ReminderDialog.StatusDetail(settings, "ja"));
        Assert.Equal("Reminder: ON", ReminderDialog.StatusHeading(settings, "en"));
    }

    // 付箋のツールチップは、ON の見出しに設定内容を続けたもの。
    [Fact]
    public void NoteTooltip_IsTheSameSummaryWithTheOnHeading()
    {
        var note = new StickyNote { Reminder = Settings() };
        var viewModel = new StickyNoteViewModel(note, new AppSettings { Language = "ja" });

        Assert.Equal(
            "リマインダー: ON\n" + ReminderSummary.Describe(note.Reminder, "ja"),
            viewModel.ReminderTooltip);

        note.Reminder = null;
        viewModel.RefreshReminder();
        Assert.Null(viewModel.ReminderTooltip);
    }
}
