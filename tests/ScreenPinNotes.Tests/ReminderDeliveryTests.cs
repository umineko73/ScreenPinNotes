using ScreenPinNotes.Models;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class ReminderDeliveryTests
{
    [Fact]
    public void ReentrantDeliveryBlocksOnlyTheActiveNote()
    {
        var now = new DateTime(2026, 9, 10, 12, 0, 0);
        var first = new StickyNote { Reminder = new() { NextAt = now } };
        var second = new StickyNote { Reminder = new() { NextAt = now } };
        var delivery = new ReminderDelivery();
        var calls = 0;
        delivery.Deliver(first, now, now, reminder =>
        {
            calls++;
            reminder.NextAt = now; // モーダル表示中の再通知設定でも二重に開かない。
            Assert.False(delivery.IsDue(first, now));
            Assert.True(delivery.IsDue(second, now));
            delivery.Deliver(first, now, now, _ => calls++);
            delivery.Deliver(second, now, now, _ => calls++);
        });
        Assert.Equal(2, calls);
        Assert.True(delivery.IsDue(first, now));
        Assert.False(delivery.IsDue(second, now));
    }

    [Fact]
    public void DeliveryAdvancesScheduleBeforePresentationAndReleasesGuardOnFailure()
    {
        var now = new DateTime(2026, 9, 10, 12, 0, 0);
        var note = new StickyNote { Reminder = new() { NextAt = now, Recurrence = "Daily" } };
        var delivery = new ReminderDelivery();
        Assert.Throws<InvalidOperationException>(() => delivery.Deliver(note, now, now, reminder =>
        {
            Assert.Equal(now.AddDays(1), reminder.NextAt);
            Assert.Equal(now, reminder.LastTriggeredAt);
            Assert.Equal(now, note.UpdatedAt);
            throw new InvalidOperationException();
        }));
        Assert.True(delivery.IsDue(note, now.AddDays(1)));
    }

    [Fact]
    public void NotificationTruncationDoesNotSplitAnEmoji()
    {
        var title = new string('a', 229) + "🦊" + new string('b', 20);
        var text = ReminderDelivery.FormatNotification([(new DateTime(2026, 9, 10, 12, 0, 0), title)]);
        Assert.True(text.Length <= 240);
        Assert.EndsWith("…", text);
        Assert.False(char.IsHighSurrogate(text[^2]));
    }
}
