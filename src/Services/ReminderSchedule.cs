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

namespace ScreenPinNotes.Services;

public static class ReminderSchedule
{
    // Local wall-clock recurrence; missed occurrences are coalesced into one notification.
    // A monthly date missing from a shorter month uses its final day.
    // Weekly reminders may be limited to the nth occurrence of the weekday in its month (days 1-7 are week 1).
    public static DateTime? Next(ReminderSettings reminder, DateTime after)
    {
        if (reminder.Recurrence == "None") return null;
        var time = reminder.TimeOfDay ?? reminder.NextAt?.TimeOfDay ?? TimeSpan.Zero;
        if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1)) time = TimeSpan.Zero;
        for (var date = after.Date; date < after.Date.AddYears(2); date = date.AddDays(1))
        {
            var candidate = date.Add(time);
            if (candidate <= after) continue;
            var matches = reminder.Recurrence switch
            {
                "Daily" => true,
                "Weekly" => reminder.WeekDays.Contains(date.DayOfWeek) && IsInSelectedWeek(reminder, date),
                "Monthly" => date.Day == Math.Min(reminder.MonthLastDay ? 31 : Math.Clamp(reminder.MonthDay, 1, 31), DateTime.DaysInMonth(date.Year, date.Month)),
                _ => false,
            };
            if (matches) return candidate;
        }
        return null;
    }

    public static int WeekOfMonth(DateTime date) => (date.Day - 1) / 7 + 1;

    private static bool IsInSelectedWeek(ReminderSettings reminder, DateTime date)
        => reminder.MonthWeeks is not { Count: > 0 } weeks || weeks.Contains(WeekOfMonth(date));
}
