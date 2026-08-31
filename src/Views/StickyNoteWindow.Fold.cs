// ScreenStickyNotes - a desktop sticky notes app for Windows 11
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

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using ScreenStickyNotes.Models;
using ScreenStickyNotes.Services;
using ScreenStickyNotes.ViewModels;
using SkiaSharp;
using WpfBrushes     = System.Windows.Media.Brushes;
using WpfButton      = System.Windows.Controls.Button;
using WpfBitmapImage = System.Windows.Media.Imaging.BitmapImage;
using WpfCheckBox    = System.Windows.Controls.CheckBox;
using WpfColor       = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfCursors     = System.Windows.Input.Cursors;
using WpfDataFormats = System.Windows.DataFormats;
using WpfFontFamily  = System.Windows.Media.FontFamily;
using WpfImage       = System.Windows.Controls.Image;
using WpfListBox     = System.Windows.Controls.ListBox;
using WpfSolidBrush  = System.Windows.Media.SolidColorBrush;


namespace ScreenStickyNotes.Views;

public partial class StickyNoteWindow
{
    // ─── 折りたたみ ──────────────────────────────────────────────

    private void Fold_Click(object sender, RoutedEventArgs e) => ToggleFold();

    // onUnfolded: 展開アニメーション完了後に呼ぶコールバック（省略可）。
    // 畳んだ状態から「展開して編集モードに入る」ような、アニメーション完了を
    // 待ってから続けたい処理のために用意している。アニメーション実行中に
    // Height へ直接代入する処理（EnterEditMode 経由の GrowForStatusBar 等）を
    // 呼んでしまうと、進行中のアニメーションが中途半端な値で凍結されてしまう。
    private void ToggleFold(Action? onUnfolded = null)
    {
        if (ViewModel.IsFolded)
        {
            ViewModel.Model.FoldedX = Left;
            ViewModel.Model.FoldedY = Top;
            ViewModel.Model.FoldedWidth = Width;

            ContentBox.Visibility = Visibility.Visible;
            BodyEditBox.Visibility = Visibility.Collapsed;
            ViewModel.IsFolded = false;
            UpdateTitleBarButtonsVisibility();
            ScheduleTitlePreview();
            SuppressWindowBoundsSave(() =>
            {
                Width = ViewModel.Model.Width; // 展開時専用の幅に戻す
                Left = ViewModel.Model.X;
                Top = ViewModel.Model.Y;
                KeepInsideWorkArea(Width, _unfoldedHeight);
            });
            ViewModel.Model.X = Left;
            ViewModel.Model.Y = Top;
            SetResizeEnabled(true);
            RunFoldAnimation(FoldedHeight, _unfoldedHeight, () =>
            {
                ViewModel.Model.Height = _unfoldedHeight;
                onUnfolded?.Invoke();
            });
        }
        else
        {
            if (_isEditMode) EnterViewMode(); // 折りたたみ時は閲覧モードに戻す
            ViewModel.Model.X = Left;
            ViewModel.Model.Y = Top;
            ViewModel.Model.Width = Width;
            _unfoldedHeight = Height;
            // アニメーション中の SizeChanged で Model.Height が
            // 途中の値に上書きされないよう先にフラグを立てる
            ViewModel.IsFolded = true;
            UpdateTitleBarButtonsVisibility();
            ScheduleTitlePreview();
            HideEditToolbar();
            // 折りたたみ時専用の幅へスナップ（未設定なら現在の幅のまま）
            SuppressWindowBoundsSave(() =>
            {
                Left = ViewModel.Model.FoldedX ?? Left;
                Top = ViewModel.Model.FoldedY ?? Top;
                Width = ViewModel.Model.FoldedWidth ?? Width;
            });
            ViewModel.Model.FoldedX = Left;
            ViewModel.Model.FoldedY = Top;
            ViewModel.Model.FoldedWidth = Width;
            RunFoldAnimation(Height, FoldedHeight, () =>
            {
                ContentBox.Visibility = Visibility.Collapsed;
                BodyEditBox.Visibility = Visibility.Collapsed;
                ViewModel.Model.Height = _unfoldedHeight;
                SetResizeEnabled(false); // タイトルバーのみの時はリサイズ不可
            });
        }
        RequestSave();
    }

    // Completed は BeginAnimation の前に購読しないと発火しない。
    // BeginAnimation の時点で Timeline が凍結され AnimationClock が
    // 生成されるため、後から足したハンドラは呼ばれない。
    private void RunFoldAnimation(double from, double to, Action? completed = null)
    {
        _isFoldAnimationRunning = true;
        if (!Settings.EnableFoldAnimation || Settings.Timings.FoldAnimationMs <= 0)
        {
            BeginAnimation(HeightProperty, null);
            Height = to;
            _isFoldAnimationRunning = false;
            completed?.Invoke();
            return;
        }

        AnimateHeight(from, to, () =>
        {
            // アニメーション後も Height のベース値を最終値に固定する。
            // これをしないと、後続の BeginAnimation(..., null) で折りたたみ高さへ戻ることがある。
            Height = to;
            BeginAnimation(HeightProperty, null);
            _isFoldAnimationRunning = false;
            completed?.Invoke();
        });
    }

    private void AnimateHeight(double from, double to, Action? completed = null)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(Settings.Timings.FoldAnimationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        if (completed != null)
            anim.Completed += (_, _) => completed();
        BeginAnimation(HeightProperty, anim);
    }

}
