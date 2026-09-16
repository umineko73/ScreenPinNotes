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
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

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
    private static readonly SolidColorBrush UpdateFlashNormalBrush = FrozenBrush("#40C4FF");
    /// <summary>ERROR / FATAL が届いたときの枠の色。</summary>
    private static readonly SolidColorBrush UpdateFlashErrorBrush = FrozenBrush("#FF5252");

    /// <summary>エラーのときに付箋を点滅させる回数。</summary>
    private const int ErrorFlashCount = 3;
    /// <summary>エラーのときに背景色を赤へ寄せる割合。</summary>
    public const double ErrorTintRatio = 0.5;
    private static readonly Color ErrorTint = Colors.Red;
    /// <summary>ふつうの更新で枠が光る片道（消えた状態から最も明るいまで）の時間。</summary>
    private static readonly TimeSpan UpdateFlashHalfPeriod = TimeSpan.FromSeconds(0.4);
    /// <summary>エラーの点滅の片道（元の色から最も赤い色まで）の時間。気付きやすいよう速く点滅させる。</summary>
    public static readonly TimeSpan ErrorFlashHalfPeriod = TimeSpan.FromSeconds(0.2);
    /// <summary>片道を何段階で塗り替えるか。10段なら毎秒50回で、なめらかに見える。</summary>
    private const int ErrorTintSteps = 10;

    private static SolidColorBrush FrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// <paramref name="background"/> を <see cref="ErrorTintRatio"/> だけ赤へ寄せた色。
    /// 透明度は元の色のまま（付箋ごとの半透明を崩さない）。
    /// </summary>
    public static Color ErrorTintColor(Color background)
        => Blend(background, ErrorTint, ErrorTintRatio);

    private static Color Blend(Color from, Color to, double ratio)
    {
        static byte Mix(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);
        return Color.FromArgb(from.A, Mix(from.R, to.R, ratio), Mix(from.G, to.G, ratio), Mix(from.B, to.B, ratio));
    }

    /// <summary>
    /// 外部ファイルが更新されたことを枠1回の明滅で知らせる。ERROR / FATAL が
    /// 届いたときは枠を赤くし、付箋の背景色も赤へ寄せて数回点滅させる。見ている最中に
    /// 光らせても意味がないので、その付箋が非アクティブなときだけ光らせる。
    /// 隠してある付箋は、リマインダーと違って呼び出してまで知らせる用ではないので出さない。
    /// </summary>
    public void FlashForExternalUpdate(bool hasError = false)
    {
        if (_isClosed || !IsVisible || IsInForeground()) return;
        // 追記の速いログでは更新が立て続けに届く。そのたびに明滅を始めからやり直すと、
        // 光りきる前に振り出しへ戻って、かえって光って見えなくなる。走っている
        // 明滅は最後まで見せ、終わってから次の更新で光らせる。
        if (_isUpdateFlashRunning)
        {
            // ただしエラーが届いたときは、進行中の明滅を消さずに赤い知らせを足す。
            if (hasError && !_isErrorTintRunning)
            {
                UpdateFlashBorder.BorderBrush = UpdateFlashErrorBrush;
                StartErrorTint();
            }
            return;
        }

        UpdateFlashBorder.BorderBrush = hasError ? UpdateFlashErrorBrush : UpdateFlashNormalBrush;
        _isUpdateFlashRunning = true;
        var pulse = new DoubleAnimation(0, 1, hasError ? ErrorFlashHalfPeriod : UpdateFlashHalfPeriod)
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(hasError ? ErrorFlashCount : 1),
            FillBehavior = FillBehavior.Stop,
        };
        pulse.Completed += (_, _) => _isUpdateFlashRunning = false;
        UpdateFlashBorder.BeginAnimation(UIElement.OpacityProperty, pulse);
        if (hasError) StartErrorTint();
    }

    private bool _isUpdateFlashRunning;
    private bool _isErrorTintRunning;

    /// <summary>
    /// 見ている最中の付箋か。IsActive だけでは足りない。アプリが前面でないときに
    /// 活性化された付箋（起動直後など）は、スレッドの中では IsActive のままなのに
    /// 画面の前面には無く、他アプリが前面に出ても非活性化の通知が来ない。
    /// </summary>
    private bool IsInForeground()
        => IsActive && GetForegroundWindow() == new System.Windows.Interop.WindowInteropHelper(this).Handle;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>
    /// 本文の背景とタイトルバーの色を、今の色から赤寄りの色へ行き来させる。
    /// 上に色を重ねると文字まで覆ってしまうので、背景そのものを塗り替える。
    /// アニメーションはバインディングの上に乗るだけなので、止めれば元の色に戻る。
    /// </summary>
    private void StartErrorTint()
    {
        var root = ErrorTintAnimation(ViewModel.BackgroundBrush);
        var title = ErrorTintAnimation(ViewModel.TitleBarBrush);
        if (root == null && title == null) return;
        _isErrorTintRunning = true;
        var completion = root ?? title!;
        completion.Completed += (_, _) => _isErrorTintRunning = false;
        if (root != null) RootBorder.BeginAnimation(System.Windows.Controls.Border.BackgroundProperty, root);
        if (title != null) TitleBar.BeginAnimation(System.Windows.Controls.Panel.BackgroundProperty, title);
    }

    // ブラシの補間アニメーションは無いので、色を少しずつ変えたブラシを順に差し替える。
    private static ObjectAnimationUsingKeyFrames? ErrorTintAnimation(Brush? brush)
    {
        if (brush is not SolidColorBrush { Color: var from }) return null;
        var to = ErrorTintColor(from);
        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = ErrorFlashHalfPeriod,
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(ErrorFlashCount),
            FillBehavior = FillBehavior.Stop,
        };
        for (var step = 0; step <= ErrorTintSteps; step++)
        {
            var tinted = new SolidColorBrush(Blend(from, to, (double)step / ErrorTintSteps));
            tinted.Freeze();
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(tinted,
                KeyTime.FromTimeSpan(ErrorFlashHalfPeriod * step / ErrorTintSteps)));
        }
        return animation;
    }

    private void StopFlashes()
    {
        ReminderFlashBorder.BeginAnimation(UIElement.OpacityProperty, null);
        UpdateFlashBorder.BeginAnimation(UIElement.OpacityProperty, null);
        RootBorder.BeginAnimation(System.Windows.Controls.Border.BackgroundProperty, null);
        TitleBar.BeginAnimation(System.Windows.Controls.Panel.BackgroundProperty, null);
        _isUpdateFlashRunning = false;
        _isErrorTintRunning = false;
    }
}
