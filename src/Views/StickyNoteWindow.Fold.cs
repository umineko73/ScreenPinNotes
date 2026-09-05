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
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
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


namespace ScreenPinNotes.Views;

public partial class StickyNoteWindow
{
    // ─── 閉じた表示 / 開いた表示 ────────────────────────────────

    private void Fold_Click(object sender, RoutedEventArgs e) => ToggleFold();

    /// <summary>
    /// 折りたたみ中の本文の見せ方をそろえる。タイトルバーを隠しているときは
    /// 本文を残して1行だけ見せるので、消さずに高さで切る。1行しか出ないところに
    /// スクロールバーが出ると畳んだ見た目が壊れるため、そのときだけ止める。
    /// </summary>
    private void ApplyFoldedContentPresentation()
    {
        var foldedToFirstLine = ViewModel.IsFolded && ViewModel.IsTitleBarHidden;
        ContentBox.Visibility = ViewModel.IsFolded && !ViewModel.IsTitleBarHidden
            ? Visibility.Collapsed
            : Visibility.Visible;
        ContentBox.VerticalScrollBarVisibility = foldedToFirstLine
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
        ContentBox.HorizontalScrollBarVisibility = foldedToFirstLine
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
    }

    // onUnfolded: 開いた表示へのアニメーション完了後に呼ぶコールバック（省略可）。
    // 閉じた表示から「開いた表示にして編集モードに入る」ような、アニメーション完了を
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

            BodyEditBox.Visibility = Visibility.Collapsed;
            ViewModel.IsFolded = false;
            UpdateTitleBarButtonsVisibility();
            ScheduleTitlePreview();
            SuppressWindowBoundsSave(() =>
            {
                Width = ViewModel.Model.Width; // 開いた表示専用の幅に戻す
                Left = ViewModel.Model.X;
                Top = ViewModel.Model.Y;
                KeepInsideWorkArea(Width, _unfoldedHeight);
            });
            ViewModel.Model.X = Left;
            ViewModel.Model.Y = Top;
            SetResizeEnabled(true);
            ApplyFoldedContentPresentation();
            RunFoldAnimation(FoldedHeight, _unfoldedHeight, () =>
            {
                ViewModel.Model.Height = _unfoldedHeight;
                if (!_isEditMode)
                    LoadContent(ViewModel.Content);
                onUnfolded?.Invoke();
            });
        }
        else
        {
            if (_isEditMode) EnterViewMode(); // 閉じた表示では閲覧モードに戻す
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
            var foldedLeft = ViewModel.Model.FoldedX ?? Left;
            var foldedTop = ViewModel.Model.FoldedY ?? Top;
            var foldedWidth = ViewModel.Model.FoldedWidth ?? Width;
            // 閉じた表示専用の幅へスナップ（未設定なら現在の幅のまま）
            RunFoldAnimation(Height, FoldedHeight, () =>
            {
                ApplyFoldedContentPresentation();
                BodyEditBox.Visibility = Visibility.Collapsed;
                SuppressWindowBoundsSave(() =>
                {
                    Left = foldedLeft;
                    Top = foldedTop;
                    Width = foldedWidth;
                });
                ViewModel.Model.FoldedX = Left;
                ViewModel.Model.FoldedY = Top;
                ViewModel.Model.FoldedWidth = Width;
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
            // これをしないと、後続の BeginAnimation(..., null) で閉じた表示の高さへ戻ることがある。
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
