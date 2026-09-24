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

namespace ScreenPinNotes.Services;

/// <summary>通知対象の選択、繰り返し日時の更新、モーダル表示中の再入を管理する。</summary>
public sealed class ReminderDelivery
{
    private readonly HashSet<string> _activeNoteIds = [];

    public bool IsDue(StickyNote note, DateTime now) =>
        note.Reminder?.NextAt is DateTime nextAt && nextAt <= now && !_activeNoteIds.Contains(note.Id);

    public void Deliver(StickyNote note, DateTime dueAt, DateTime now, Action<ReminderSettings> present)
    {
        if (!_activeNoteIds.Add(note.Id)) return;
        try
        {
            var reminder = note.Reminder ??= new ReminderSettings();
            reminder.LastTriggeredAt = now;
            note.UpdatedAt = now;
            reminder.TimeOfDay ??= dueAt.TimeOfDay;
            reminder.NextAt = ReminderSchedule.Next(reminder, now);
            present(reminder);
        }
        finally
        {
            _activeNoteIds.Remove(note.Id);
        }
    }

    public static string FormatNotification(IEnumerable<(DateTime? NextAt, string Title)> reminders)
    {
        var message = string.Join("\n", reminders.Select(r => $"{r.NextAt:HH:mm}  {r.Title}"));
        if (message.Length <= 240) return message;
        var cut = 237;
        if (char.IsHighSurrogate(message[cut - 1])) cut--;
        return message[..cut] + "…";
    }
}
