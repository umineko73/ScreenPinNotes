// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
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

namespace ScreenPinNotes.Tests;

public class ReminderScheduleTests
{
    [Fact]
    public void Daily_MissedDaysAndSnoozeKeepWallClockTime()
    {
        var reminder = new ReminderSettings { Recurrence = "Daily", TimeOfDay = TimeSpan.FromHours(9), NextAt = new DateTime(2026, 9, 1, 9, 15, 0) };
        Assert.Equal(new DateTime(2026, 9, 7, 9, 0, 0), ReminderSchedule.Next(reminder, new DateTime(2026, 9, 6, 10, 0, 0)));
        Assert.Equal(new DateTime(2026, 9, 6, 9, 0, 0), ReminderSchedule.Next(reminder, new DateTime(2026, 9, 6, 8, 0, 0)));
    }

    [Fact]
    public void Weekly_SelectsMultipleDaysAcrossYearBoundary()
    {
        var reminder = new ReminderSettings { Recurrence = "Weekly", TimeOfDay = TimeSpan.FromHours(9), WeekDays = [DayOfWeek.Monday, DayOfWeek.Friday] };
        Assert.Equal(new DateTime(2027, 1, 1, 9, 0, 0), ReminderSchedule.Next(reminder, new DateTime(2026, 12, 31, 10, 0, 0)));
        Assert.Equal(new DateTime(2027, 1, 4, 9, 0, 0), ReminderSchedule.Next(reminder, new DateTime(2027, 1, 1, 9, 0, 0)));
    }

    [Theory]
    [InlineData(2027, 28)]
    [InlineData(2028, 29)]
    public void Monthly_ClampsToLastDayWithoutLosingOriginalDay(int year, int day)
    {
        var reminder = new ReminderSettings { Recurrence = "Monthly", MonthDay = 31, TimeOfDay = TimeSpan.FromHours(9) };
        var february = ReminderSchedule.Next(reminder, new DateTime(year, 2, 1));
        Assert.Equal(new DateTime(year, 2, day, 9, 0, 0), february);
        Assert.Equal(new DateTime(year, 3, 31, 9, 0, 0), ReminderSchedule.Next(reminder, february!.Value));
    }

    [Fact]
    public void OneTime_DoesNotRepeatAndOptionsSurviveSerialization()
    {
        var reminder = new ReminderSettings { Recurrence = "None", WeekDays = [DayOfWeek.Monday], WindowsNotification = true, ShowAlert = true };
        Assert.Null(ReminderSchedule.Next(reminder, DateTime.Now));
        var copy = System.Text.Json.JsonSerializer.Deserialize<ReminderSettings>(System.Text.Json.JsonSerializer.Serialize(reminder))!;
        Assert.Equal(reminder.WeekDays, copy.WeekDays);
        Assert.True(copy.ShowAlert);
    }
}
