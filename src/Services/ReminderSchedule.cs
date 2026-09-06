using ScreenPinNotes.Models;

namespace ScreenPinNotes.Services;

public static class ReminderSchedule
{
    // Local wall-clock recurrence; missed occurrences are coalesced into one notification.
    // A monthly date missing from a shorter month uses its final day.
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
                "Weekly" => reminder.WeekDays.Contains(date.DayOfWeek),
                "Monthly" => date.Day == Math.Min(Math.Clamp(reminder.MonthDay, 1, 31), DateTime.DaysInMonth(date.Year, date.Month)),
                _ => false,
            };
            if (matches) return candidate;
        }
        return null;
    }
}
