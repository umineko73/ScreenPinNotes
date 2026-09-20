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

using System.Globalization;
using ScreenPinNotes.Models;

namespace ScreenPinNotes.Services;

/// <summary>
/// リマインダーの設定を、人が読める数行にまとめる。付箋のツールチップと
/// リマインダー設定画面の「現在の設定」が同じ文面を使うので、設定したときに
/// 見た内容と、あとからホバーで確かめる内容が食い違わない。
/// </summary>
public static class ReminderSummary
{
    /// <summary>設定の要約。リマインダーが無ければ null。</summary>
    public static string? Describe(ReminderSettings? settings, string language)
    {
        if (settings?.NextAt is not DateTime nextAt) return null;

        var culture = CultureInfo.GetCultureInfo(LocalizationService.ResolveLanguage(language));
        var separator = T("ReminderSummarySeparator", language);
        var lines = new List<string>
        {
            $"{T("ReminderSummaryNext", language)}: {nextAt.ToString("yyyy/MM/dd HH:mm", culture)}",
            $"{T("ReminderSummaryRepeat", language)}: {DescribeRecurrence(settings, language, culture, separator)}",
        };
        // 新しい設定なら通知方法は必ず1つ以上選ばれている。古い設定で
        // 全部消えている場合に「通知:」だけの行を出さない。
        var methods = DescribeMethods(settings, language);
        if (methods.Count > 0)
            lines.Add($"{T("ReminderSummaryNotify", language)}: {string.Join(separator, methods)}");
        return string.Join("\n", lines);
    }

    private static string DescribeRecurrence(ReminderSettings settings, string language, CultureInfo culture, string separator)
    {
        switch (settings.Recurrence)
        {
            case "Daily":
                return T("ReminderRepeatDaily", language);

            case "Weekly":
                // 曜日の並びは設定画面と同じ月曜始まり。
                var days = settings.WeekDays.Distinct().OrderBy(day => ((int)day + 6) % 7)
                    .Select(culture.DateTimeFormat.GetAbbreviatedDayName);
                var weekly = Join(T("ReminderRepeatWeekly", language), string.Join(separator, days));
                if (settings.MonthWeeks.Count == 0) return weekly;   // 空 = 毎週
                var weeks = settings.MonthWeeks.Distinct().OrderBy(week => week)
                    .Select(week => T("ReminderWeek" + week, language));
                return weekly + string.Format(T("ReminderSummaryWeeksFormat", language), string.Join(separator, weeks));

            case "Monthly":
                var monthDay = settings.MonthLastDay
                    ? T("ReminderLastDay", language)
                    : string.Format(T("ReminderSummaryMonthDay", language), settings.MonthDay);
                return Join(T("ReminderRepeatMonthly", language), monthDay);

            default:
                return T("ReminderRepeatNone", language);
        }
    }

    private static List<string> DescribeMethods(ReminderSettings settings, string language)
    {
        var methods = new List<string>();
        if (settings.WindowsNotification) methods.Add(T("ReminderMethodNotification", language));
        // null = この項目より前に保存された設定。TriggerReminder と同じく「出す」として扱う。
        if (settings.ShowAlert != false) methods.Add(T("ReminderMethodAlert", language));
        if (settings.FlashNote) methods.Add(T("ReminderMethodFlash", language));
        return methods;
    }

    private static string Join(string label, string detail)
        => string.IsNullOrEmpty(detail) ? label : label + " " + detail;

    private static string T(string key, string language)
        => LocalizationService.T(key, language);
}
