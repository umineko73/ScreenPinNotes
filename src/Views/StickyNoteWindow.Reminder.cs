using System.Windows;
using System.Windows.Media.Animation;

namespace ScreenPinNotes.Views;

public partial class StickyNoteWindow
{
    public void FlashForReminder()
    {
        if (_isClosed || !IsVisible) return;
        var pulse = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(10),
            FillBehavior = FillBehavior.Stop,
        };
        ReminderFlashBorder.BeginAnimation(UIElement.OpacityProperty, pulse);
    }

    private void StopReminderFlash() => ReminderFlashBorder.BeginAnimation(UIElement.OpacityProperty, null);
}
