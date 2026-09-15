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

using ScreenPinNotes.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace ScreenPinNotes.Views;

public partial class StickyNoteWindow
{
    /// <summary>タイトルバー右端のボタンを、リサイズ枠より1px内側へ離す幅。</summary>
    private double TitleBarEndGapWidth => Settings.Layout.ResizeBorder + 1;

    private double MeasureFoldedWidth()
    {
        if (ViewModel.Model.ManualFoldedWidth is > 0 and var manual && double.IsFinite(manual))
            return Math.Max(MinWidth, manual);
        var hidden = ViewModel.IsTitleBarHidden;
        var text = hidden ? _foldedPreviewSourceText : ViewModel.DisplayTitle;
        // Measure the unshortened path, not the text fitted to the previous width.
        var path = MarkdownRenderer.GetImageOnlyTarget(ViewModel.Content);
        if (path != null && (hidden || string.IsNullOrWhiteSpace(ViewModel.Title))) text = path;
        var font = hidden ? new System.Windows.Media.FontFamily(ViewModel.FontFamily) : TitleText.FontFamily;
        var measured = new System.Windows.Media.FormattedText(text,
            System.Globalization.CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
            new System.Windows.Media.Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            ViewModel.TitleFontSize, ViewModel.TextForeground,
            System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var icon = string.IsNullOrEmpty(ViewModel.Icon) ? 0 : ViewModel.TitleIconSize;
        var padding = ViewModel.NoteContentPadding;
        var chrome = hidden
            ? padding.Left + padding.Right + 10 + (icon == 0 ? 24 : icon + 18)
            : 10 + (icon == 0 ? 0 : icon + 6) + 26 * (Settings.ShowFoldButton ? 3 : 2) + TitleBarEndGapWidth;
        if (!hidden)
            foreach (var child in TitleBar.Children.OfType<TextBlock>().Where(c => Grid.GetColumn(c) is >= 2 and <= 4))
            {
                child.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                chrome += child.DesiredSize.Width;
            }
        return Math.Clamp(Math.Ceiling(measured.WidthIncludingTrailingWhitespace + chrome),
            MinWidth, Math.Max(MinWidth, Math.Min(480, ViewModel.Model.Width)));
    }

    /// <summary>
    /// ウィンドウを今の表示（開いた／閉じた）本来の大きさへ合わせ直す。保存値は変えない。
    /// </summary>
    private void RestorePresentationBounds()
        => SuppressWindowBoundsSave(() =>
        {
            Trace("RestorePresentationBounds");
            if (ViewModel.IsFolded)
            {
                SetResizeEnabled(false); // 1行分の高さとその上下限を当て直す
                Width = MeasureFoldedWidth();
            }
            else
            {
                Width = ViewModel.Model.Width;
                Height = _geometry.ExpandedHeight;
            }
        });

    private void FitFoldedWidth()
    {
        if (!ViewModel.IsFolded || _isEditMode || _isFoldAnimationRunning) return;
        SuppressWindowBoundsSave(() => Width = MeasureFoldedWidth());
    }

    internal bool IsTemporarilyRaised { get; private set; }

    private void SetTemporaryRaise(bool raised)
    {
        IsTemporarilyRaised = raised;
        Topmost = ViewModel.IsTopmost || raised;
        if (raised) ChangeZOrder(true);
    }

    // ─── 閉じた表示 / 開いた表示 ────────────────────────────────

    private void Fold_Click(object sender, RoutedEventArgs e) => ToggleFold();

    /// <summary>
    /// 折りたたみ中の本文の見せ方をそろえる。タイトルバーを隠しているときは
    /// 専用のプレビューだけを表示する。本文は再展開で再利用するため保持する。
    /// </summary>
    private void ApplyFoldedContentPresentation()
    {
        var foldedToFirstLine = ViewModel.IsFolded && ViewModel.IsTitleBarHidden;
        ContentBox.Visibility = ViewModel.IsFolded
            ? Visibility.Collapsed
            : Visibility.Visible;
        FoldedPreviewHost.Visibility = foldedToFirstLine ? Visibility.Visible : Visibility.Collapsed;
        if (foldedToFirstLine) UpdateFoldedPreview(ViewModel.Content);
    }

    // onUnfolded: 開いた表示へのアニメーション完了後に呼ぶコールバック（省略可）。
    // 閉じた表示から「開いた表示にして編集モードに入る」ような、アニメーション完了を
    // 待ってから続けたい処理のために用意している。アニメーション実行中に
    // Height へ直接代入する処理（編集サイズの適用等）を
    // 呼んでしまうと、進行中のアニメーションが中途半端な値で凍結されてしまう。
    private void ApplyFoldState(bool folded, Action? onUnfolded = null)
        => SuppressWindowBoundsSave(() => ApplyFoldStateCore(folded, onUnfolded));

    // Activation, bindings and resize constraints can synchronously dispatch layout
    // while IsFolded already describes the destination but bounds still describe
    // the source. Protect the entire transition, not only individual assignments.
    private void ApplyFoldStateCore(bool folded, Action? onUnfolded)
    {
        if (DiagnosticTrace.Enabled)
            Trace($"ApplyFoldState folded={folded} model={ViewModel.Model.Width:0.#}x{ViewModel.Model.Height:0.#} " +
                  $"foldedWidth={ViewModel.Model.FoldedWidth:0.#} manualFoldedWidth={ViewModel.Model.ManualFoldedWidth:0.#}");
        _resizeContentRefresh?.Abort();
        if (!folded)
        {
            var (dpiX, dpiY) = GetDpi();
            _geometry.CaptureFolded(Left, Top, Width, dpiX, dpiY);

            BodyEditBox.Visibility = Visibility.Collapsed;
            ViewModel.IsFolded = false;
            Activate();
            SetTemporaryRaise(true);
            UpdateTitleBarButtonsVisibility();
            ScheduleTitlePreview();
            SuppressWindowBoundsSave(() =>
            {
                Width = ViewModel.Model.Width; // 開いた表示専用の幅に戻す
                Left = ViewModel.Model.X;
                Top = ViewModel.Model.Y;
                KeepInsideWorkArea(Width, _geometry.ExpandedHeight);
                ReconcileScreenPlacement();
            });
            _geometry.CaptureExpanded(Left, Top, ViewModel.Model.Width, _geometry.ExpandedHeight, dpiX, dpiY);
            SetResizeEnabled(true);
            RunFoldAnimation(FoldedHeight, _geometry.ExpandedHeight, () =>
            {
                // Keep the retained document collapsed during height animation:
                // showing it earlier repeatedly lays out the entire long document.
                ApplyFoldedContentPresentation();
                if (!_isEditMode)
                    EnsureExpandedContent();
                ReconcileScreenPlacement();
                onUnfolded?.Invoke();
            });
        }
        else
        {
            SetTemporaryRaise(false);
            if (_isEditMode) EnterViewModeCore(); // 閉じた表示では閲覧モードに戻す
            if (_isEditMode) return; // 編集終了が抑止された場合は折りたたまない。
            _expandedScrollX = ContentBox.HorizontalOffset;
            _expandedScrollY = ContentBox.VerticalOffset;
            var (dpiX, dpiY) = GetDpi();
            _geometry.CaptureExpanded(Left, Top, Width, Height, dpiX, dpiY);
            // アニメーション中の SizeChanged で Model.Height が
            // 途中の値に上書きされないよう先にフラグを立てる
            ViewModel.IsFolded = true;
            // 先頭行だけ更新し、本文の表示要素と画像は保持する。
            if (ViewModel.IsTitleBarHidden)
            {
                ApplyFoldedContentPresentation();
            }
            UpdateTitleBarButtonsVisibility();
            ScheduleTitlePreview();
            HideEditToolbar();
            var foldedLeft = ViewModel.Model.FoldedX ?? Left;
            var foldedTop = ViewModel.Model.FoldedY ?? Top;
            // 本文の幅は保持し、閉じた表示は内容に合わせる。
            RunFoldAnimation(Height, FoldedHeight, () =>
            {
                ApplyFoldedContentPresentation();
                BodyEditBox.Visibility = Visibility.Collapsed;
                SuppressWindowBoundsSave(() =>
                {
                    Left = foldedLeft;
                    Top = foldedTop;
                    Width = MeasureFoldedWidth();
                    ReconcileScreenPlacement();
                });
                _geometry.CaptureFolded(Left, Top, Width, dpiX, dpiY);
                SetResizeEnabled(false); // タイトルバーのみの時はリサイズ不可
                UpdateLayout();
                UpdateImagePathPreview();
            });
        }
        RequestSave();
    }

    // Completed は BeginAnimation の前に購読しないと発火しない。
    // BeginAnimation の時点で Timeline が凍結され AnimationClock が
    // 生成されるため、後から足したハンドラは呼ばれない。
    private Action? _completeFoldAnimation;

    private void CompleteFoldAnimation() => _completeFoldAnimation?.Invoke();

    private void RunFoldAnimation(double from, double to, Action? completed = null)
    {
        _isFoldAnimationRunning = true;
        Action finish = null!;
        finish = () =>
        {
            // 取り外したアニメーションの完了通知が遅れて届いても再実行しない。
            if (_completeFoldAnimation != finish) return;
            _completeFoldAnimation = null;
            // アニメーション後も Height のベース値を最終値に固定する。
            // これをしないと、後続の BeginAnimation(..., null) で閉じた表示の高さへ戻ることがある。
            Height = to;
            BeginAnimation(HeightProperty, null);
            _isFoldAnimationRunning = false;
            if (DiagnosticTrace.Enabled) Trace($"FoldAnimation finished to={to:0.#}");
            completed?.Invoke();
        };
        _completeFoldAnimation = finish;
        if (!Settings.EnableFoldAnimation || Settings.Timings.FoldAnimationMs <= 0)
            finish();
        else
            AnimateHeight(from, to, finish);
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
