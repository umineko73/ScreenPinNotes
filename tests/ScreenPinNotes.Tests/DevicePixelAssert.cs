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

using System.Windows;
using System.Windows.Media;

namespace ScreenPinNotes.Tests;

/// <summary>
/// ウィンドウから読み戻した論理座標の比較。ウィンドウは物理ピクセル単位でしか置けないので、
/// 125% では 130 が 162.5px になり、読み戻すと 129.6 になる。付箋はその実際の位置を保存するため、
/// 100% / 150% / 200% でしか割り切れない値を完全一致で比べると、拡大率によって失敗する。
/// 物理ピクセルの半分までのずれは同じ位置とみなす。
/// </summary>
internal static class DevicePixelAssert
{
    public static void Near(double expected, double actual, Visual window)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var scale = Math.Min(dpi.DpiScaleX, dpi.DpiScaleY);
        Assert.InRange(Math.Abs(actual - expected), 0, 0.5 / scale + 1e-7);
    }

    public static void Near((double X, double Y) expected, (double X, double Y) actual, Visual window)
    {
        Near(expected.X, actual.X, window);
        Near(expected.Y, actual.Y, window);
    }
}
