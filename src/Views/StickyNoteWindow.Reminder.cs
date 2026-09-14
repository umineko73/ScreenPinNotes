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

    /// <summary>ふつうの更新を知らせる枠の色。</summary>
    private static readonly System.Windows.Media.SolidColorBrush UpdateFlashNormalBrush = FrozenBrush("#40C4FF");
    /// <summary>ERROR / FATAL が届いたときの枠の色。</summary>
    private static readonly System.Windows.Media.SolidColorBrush UpdateFlashErrorBrush = FrozenBrush("#FF5252");

    private static System.Windows.Media.SolidColorBrush FrozenBrush(string hex)
    {
        var brush = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 外部ファイルが更新されたことを枠1回の明滅で知らせる。見ている最中に
    /// 光らせても意味がないので、その付箋が非アクティブなときだけ光らせる。
    /// 隠してある付箋は、リマインダーと違って呼び出してまで知らせる用ではないので出さない。
    /// </summary>
    public void FlashForExternalUpdate(bool hasError = false)
    {
        if (_isClosed || !IsVisible || IsActive) return;
        // 追記の速いログでは更新が立て続けに届く。そのたびに明滅を始めからやり直すと、
        // 光りきる前に振り出しへ戻って、かえって光って見えなくなる。走っている
        // 明滅は最後まで見せ、終わってから次の更新で光らせる。
        if (_isUpdateFlashRunning)
        {
            // ただしエラーが届いたときは、進行中の明滅を消さずに色だけ引き上げる。
            if (hasError) UpdateFlashBorder.BorderBrush = UpdateFlashErrorBrush;
            return;
        }

        UpdateFlashBorder.BorderBrush = hasError ? UpdateFlashErrorBrush : UpdateFlashNormalBrush;
        _isUpdateFlashRunning = true;
        var pulse = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4))
        {
            AutoReverse = true,
            FillBehavior = FillBehavior.Stop,
        };
        pulse.Completed += (_, _) => _isUpdateFlashRunning = false;
        UpdateFlashBorder.BeginAnimation(UIElement.OpacityProperty, pulse);
    }

    private bool _isUpdateFlashRunning;

    private void StopFlashes()
    {
        ReminderFlashBorder.BeginAnimation(UIElement.OpacityProperty, null);
        UpdateFlashBorder.BeginAnimation(UIElement.OpacityProperty, null);
        _isUpdateFlashRunning = false;
    }
}
