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
    // ─── タイトルバーボタン ──────────────────────────────────────

    // 押した付箋の書式を引き継いで新規作成する
    private void AddNote_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = GetNewNotePositionNearCursor();
        App.Current.AddNewNote(ViewModel.Model, x, y);
    }

    private (double x, double y) GetNewNotePositionNearCursor()
    {
        var layout = Settings.Layout;

        var cursor = System.Windows.Forms.Cursor.Position;
        var (dpiX, dpiY) = GetDpi();
        var screen = System.Windows.Forms.Screen.FromPoint(cursor);
        var wa = screen.WorkingArea;

        double left = cursor.X / dpiX + layout.NewNoteNearCursorOffset;
        double top  = cursor.Y / dpiY + layout.NewNoteNearCursorOffset;

        double minLeft = wa.Left / dpiX;
        double maxLeft = wa.Right / dpiX - layout.DefaultNoteWidth;
        double minTop = wa.Top / dpiY;
        double maxTop = wa.Bottom / dpiY - layout.DefaultNoteHeight;

        return (
            Math.Clamp(left, minLeft, Math.Max(minLeft, maxLeft)),
            Math.Clamp(top, minTop, Math.Max(minTop, maxTop))
        );
    }

    private void Pin_Changed(object sender, RoutedEventArgs e)
    {
        Topmost = ViewModel.IsTopmost;
        RequestSave();
    }

    private void FontSmaller_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.FontSize > 8) { ViewModel.FontSize -= 1; RequestSave(); }
        ShowSizeOverlay(string.Format(LocalizationService.T("BodySize"), ViewModel.FontSize));
        UpdateToolbarTooltips();
    }

    private void FontLarger_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.FontSize < 48) { ViewModel.FontSize += 1; RequestSave(); }
        ShowSizeOverlay(string.Format(LocalizationService.T("BodySize"), ViewModel.FontSize));
        UpdateToolbarTooltips();
    }

    private void TitleSmaller_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TitleFontSize > 8) SetTitleFontSize(ViewModel.TitleFontSize - 1);
        ShowSizeOverlay(string.Format(LocalizationService.T("TitleSize"), ViewModel.TitleFontSize));
        UpdateToolbarTooltips();
    }

    private void TitleLarger_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TitleFontSize < 28) SetTitleFontSize(ViewModel.TitleFontSize + 1);
        ShowSizeOverlay(string.Format(LocalizationService.T("TitleSize"), ViewModel.TitleFontSize));
        UpdateToolbarTooltips();
    }

    private void SetTitleFontSize(double size)
    {
        ViewModel.TitleFontSize = size;

        // 折りたたみ中はウィンドウ高さ＝タイトルバー高さなので追従させる
        if (ViewModel.IsFolded)
        {
            BeginAnimation(HeightProperty, null);   // 折りたたみアニメの保持を解除
            SetResizeEnabled(false);                // Min/Max を新しい高さで固定し直す
            Height = FoldedHeight;
        }
        RequestSave();
    }

    // ─── サイズ表示オーバーレイ ──────────────────────────────────
    //
    // ツールチップはクリックで閉じてしまい連打中に読めないため、
    // 音量 OSD のように一時表示してフェードアウトさせる。

    private void ShowSizeOverlay(string text)
    {
        SizeOverlayText.Text = text;

        SizeOverlay.BeginAnimation(OpacityProperty, null);   // 実行中のフェードを解除
        SizeOverlay.Opacity = 1;

        _overlayTimer.Stop();
        _overlayTimer.Start();                               // 連打のたびに表示時間を延長
    }

    private void FadeOutSizeOverlay()
    {
        _overlayTimer.Stop();
        SizeOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(Settings.Timings.SizeOverlayFadeMs)));
    }

    private WpfSolidBrush PopupBackgroundBrush()
        => IsDarkTheme()
            ? new WpfSolidBrush(WpfColor.FromRgb(31, 41, 55))
            : new WpfSolidBrush(WpfColor.FromRgb(255, 255, 255));

    private WpfSolidBrush PopupBorderBrush()
        => IsDarkTheme()
            ? new WpfSolidBrush(WpfColor.FromRgb(75, 85, 99))
            : new WpfSolidBrush(WpfColor.FromRgb(211, 211, 211));

    private bool IsDarkTheme()
        => string.Equals(Settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase);

    private void Font_Click(object sender, RoutedEventArgs e)
    {
        if (_fontPopup != null) { _fontPopup.PlacementTarget = (UIElement)sender; _fontPopup.IsOpen = true; }
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (_colorPopup == null) return;
        UpdateColorSelection();
        _colorPopup.PlacementTarget = (UIElement)sender;
        _colorPopup.IsOpen = true;
    }

    private void Icon_Click(object sender, RoutedEventArgs e)
    {
        if (_iconPopup == null) return;
        UpdateIconSelection();
        _iconPopup.PlacementTarget = (UIElement)sender;
        _iconPopup.IsOpen = true;
    }

    // 現在の色にだけチェックを表示する
    private void UpdateColorSelection()
    {
        if (_colorPanel == null) return;
        foreach (var child in _colorPanel.Children)
            if (child is WpfButton b)
                b.Content = (b.Tag as string) == ViewModel.ColorKey ? "✓" : null;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            LocalizationService.T("DeleteConfirmMessage"),
            LocalizationService.T("DeleteConfirmTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            App.Current.RemoveNote(ViewModel.Model.Id);
            Close();
        }
    }

}
