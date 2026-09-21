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
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenPinNotes.Services;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace ScreenPinNotes.Views;

/// <summary>
/// 本文の編集中に文字を選ぶと、そのそばに出る書式ツールバー（文字の修飾・リスト・リンク・修飾の削除）。
/// 付箋の下に常に出ている編集ツールバーとは別の Popup で、選択が空になる・
/// 付箋から離れる・編集を終えると消える。
/// </summary>
public partial class StickyNoteWindow
{
    private bool _isFormatToolbarUpdateQueued;

    // 16x16 の線画アイコン。線は押せるボタンの文字色で描く（Fill の図形だけ塗る）。
    private const string BoldIcon = "M4.5,2.5 H9 A2.9,2.9 0 0 1 9,8.3 H4.5 Z M4.5,8.3 H9.8 A3.1,3.1 0 0 1 9.8,14.5 H4.5 Z";
    private const string ItalicIcon = "M7,2.5 H13 M3,13.5 H9 M10,2.5 L6,13.5";
    private const string StrikeIcon =
        "M2,8 H14 M11.6,4.4 C11,3 9.7,2.4 8,2.4 C5.9,2.4 4.6,3.5 4.6,5 C4.6,5.9 5,6.5 5.8,7 " +
        "M10.4,9.4 C11.1,9.9 11.5,10.5 11.5,11.3 C11.5,12.8 10.1,13.7 8,13.7 C6.2,13.7 4.9,13 4.3,11.7";
    private const string HighlightIcon = "M9.8,2.2 L13.8,6.2 L8,12 L4,8 Z M4,8 L2.4,12.6 L3.4,13.6 L8,12 M9.5,14.5 H14";
    private const string CodeIcon = "M5.5,4 L1.8,8 L5.5,12 M10.5,4 L14.2,8 L10.5,12 M9,2.8 L7,13.2";
    private const string BulletsIcon = "M6.5,3.5 H14 M6.5,8 H14 M6.5,12.5 H14";
    private const string BulletsDots = "M2.3,3.5 A1.1,1.1 0 1 0 4.5,3.5 A1.1,1.1 0 1 0 2.3,3.5 Z M2.3,8 A1.1,1.1 0 1 0 4.5,8 A1.1,1.1 0 1 0 2.3,8 Z M2.3,12.5 A1.1,1.1 0 1 0 4.5,12.5 A1.1,1.1 0 1 0 2.3,12.5 Z";
    private const string NumberedIcon =
        "M7,4 H14 M7,8 H14 M7,12 H14 M2.3,2.8 L3.5,2.2 V6.2 " +
        "M2.1,9.8 C2.3,9.2 2.8,8.9 3.4,8.9 C4.1,8.9 4.6,9.3 4.6,9.9 C4.6,10.8 2.1,11.7 2.1,13.2 H4.8";
    private const string TasksIcon =
        "M2,2.5 H6 V6.5 H2 Z M2.8,4.6 L3.8,5.6 L5.4,3.5 M2,9.5 H6 V13.5 H2 Z M8.5,4.5 H14 M8.5,11.5 H14";
    private const string LinkIcon =
        "M7,5 L8.6,3.4 A2.8,2.8 0 0 1 12.6,7.4 L11,9 M9,11 L7.4,12.6 A2.8,2.8 0 0 1 3.4,8.6 L5,7 M6.2,9.8 L9.8,6.2";
    // 「T」と「x」。書式のクリアでよく見る形。
    private const string ClearIcon = "M2.5,3 H10.5 M6.5,3 V13 M9.8,9.3 L14,13.5 M14,9.3 L9.8,13.5";

    private void InitializeFormatToolbar()
    {
        FormatBarButtons.Children.Clear();
        AddFormatButton(BoldIcon, "FormatBold", () => ApplyMarkdownFormat("**", line: false));
        AddFormatButton(ItalicIcon, "FormatItalic", () => ApplyMarkdownFormat("*", line: false));
        AddFormatButton(StrikeIcon, "FormatStrike", () => ApplyMarkdownFormat("~~", line: false));
        AddFormatButton(HighlightIcon, "FormatHighlight", () => ApplyMarkdownFormat("==", line: false));
        AddFormatButton(CodeIcon, "FormatCode", () => ApplyMarkdownFormat("`", line: false));
        AddFormatDivider();
        AddFormatButton(BulletsIcon, "FormatBullets", () => ApplyMarkdownFormat("- ", line: true), BulletsDots);
        AddFormatButton(NumberedIcon, "FormatNumbered", () => ApplyMarkdownFormat("1. ", line: true));
        AddFormatButton(TasksIcon, "FormatTasks", () => ApplyMarkdownFormat("- [ ] ", line: true));
        AddFormatDivider();
        AddFormatButton(LinkIcon, "FormatLink", InsertOrEditMarkdownLink);
        AddFormatButton(ClearIcon, "FormatClear", ClearMarkdownFormatting);

        BodyEditBox.SelectionChanged += (_, _) => QueueFormatToolbarUpdate();
        // マウスで選んでいる途中は出さず、離したところで出す。TextBox が
        // ボタンを離す操作を処理済みにするので、処理済みのものも受け取る。
        BodyEditBox.AddHandler(PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler((_, _) => QueueFormatToolbarUpdate()), handledEventsToo: true);
    }

    private void AddFormatDivider()
        => FormatBarButtons.Children.Add(new Border
        {
            Width = 1,
            Margin = new Thickness(3, 6, 3, 6),
            Background = PopupBorderBrush(),
        });

    private void AddFormatButton(string strokes, string tooltipKey, Action apply, string? fills = null)
    {
        var button = new Button
        {
            ToolTip = LocalizationService.T(tooltipKey),
            Style = (Style)FindResource("EditToolbarButton"),
            Width = 28,
            Height = 28,
        };
        var icon = new Grid { Width = 16, Height = 16, SnapsToDevicePixels = true };
        var foreground = new System.Windows.Data.Binding(nameof(Button.Foreground)) { Source = button };
        var stroke = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(strokes),
            StrokeThickness = 1.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        stroke.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, foreground);
        icon.Children.Add(stroke);
        if (fills != null)
        {
            var fill = new System.Windows.Shapes.Path { Data = Geometry.Parse(fills) };
            fill.SetBinding(System.Windows.Shapes.Shape.FillProperty, foreground);
            icon.Children.Add(fill);
        }
        button.Content = icon;
        button.Click += (_, _) => apply();
        FormatBarButtons.Children.Add(button);
    }

    /// <summary>
    /// 選択の変化は書式の適用中にも何度か起きるので、まとめて1回だけ見直す。
    /// </summary>
    private void QueueFormatToolbarUpdate()
    {
        if (_isFormatToolbarUpdateQueued) return;
        _isFormatToolbarUpdateQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            _isFormatToolbarUpdateQueued = false;
            UpdateFormatToolbar();
        });
    }

    private bool ShouldShowFormatToolbar()
        => !_isClosed &&
           IsActive &&
           IsBodyEditing() &&
           !IsContentReadOnly() &&
           !ViewModel.IsFolded &&
           !_isContentContextMenuOpen &&
           !_isLinkEditDialogOpen &&
           BodyEditBox.SelectionLength > 0 &&
           Mouse.LeftButton != MouseButtonState.Pressed;

    private void UpdateFormatToolbar()
    {
        if (!ShouldShowFormatToolbar())
        {
            HideFormatToolbar();
            return;
        }
        FormatBar.Background = ToolbarBackground;
        FormatBar.BorderBrush = PopupBorderBrush();
        var foreground = PopupForegroundBrush();
        foreach (var button in FormatBarButtons.Children.OfType<Button>())
            button.Foreground = foreground;
        foreach (var divider in FormatBarButtons.Children.OfType<Border>())
            divider.Background = FormatBar.BorderBrush;
        FormatToolbarPopup.CustomPopupPlacementCallback = PlaceFormatToolbar;
        FormatToolbarPopup.IsOpen = true;
        // 開いたままでは選択が動いても位置を計算し直さないので、ずらして促す。
        FormatToolbarPopup.HorizontalOffset = FormatToolbarPopup.HorizontalOffset == 0 ? 0.01 : 0;
    }

    private void HideFormatToolbar()
    {
        if (FormatToolbarPopup != null)
            FormatToolbarPopup.IsOpen = false;
    }

    private void FormatToolbarPopup_Opened(object? sender, EventArgs e)
        => SyncPopupZOrder(FormatBar);

    /// <summary>
    /// 選んだ文字の最初の行の上に置く。画面の上端で入らなければ最後の行の下に置く。
    /// 選択の一部が見えていないときは、見えている本文の端に寄せる。
    /// </summary>
    private CustomPopupPlacement[] PlaceFormatToolbar(Size popupSize, Size targetSize, Point offset)
    {
        const double Gap = 4;
        var (dpiX, dpiY) = GetDpi();
        var start = BodyEditBox.GetRectFromCharacterIndex(BodyEditBox.SelectionStart);
        var end = BodyEditBox.GetRectFromCharacterIndex(BodyEditBox.SelectionStart + BodyEditBox.SelectionLength);
        if (start.IsEmpty || end.IsEmpty)
            return [];
        var boxHeight = BodyEditBox.ActualHeight;
        var top = Math.Clamp(start.Top, 0, boxHeight) * dpiY;
        var bottom = Math.Clamp(end.Bottom, 0, boxHeight) * dpiY;
        var maxX = Math.Max(0, targetSize.Width - popupSize.Width);
        var x = Math.Clamp(start.Left * dpiX - popupSize.Width / 4, 0, maxX);
        return
        [
            new CustomPopupPlacement(new Point(x, top - popupSize.Height - Gap * dpiY), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(x, bottom + Gap * dpiY), PopupPrimaryAxis.Horizontal),
        ];
    }
}
