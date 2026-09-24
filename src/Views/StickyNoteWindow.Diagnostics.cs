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

using System.Globalization;
using System.Runtime.InteropServices;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Views;

/// <summary>
/// 辺ドラッグと折りたたみの食い違いを、再現する環境で記録する（<see cref="DiagnosticTrace"/>）。
/// Windows のメッセージが届く順番と、表示の切り替えを呼んだ処理を同じ時系列に並べる。
/// </summary>
public partial class StickyNoteWindow
{
    private void Trace(string message)
    {
        if (!DiagnosticTrace.Enabled || DataContext is not ViewModels.StickyNoteViewModel) return;
        var id = ViewModel.Model.Id;
        DiagnosticTrace.Write(string.Create(CultureInfo.InvariantCulture,
            $"note={id[..Math.Min(8, id.Length)]} folded={ViewModel.IsFolded} edit={_isEditMode} " +
            $"sizing={_isSizingGesture}(startFolded={_sizingGestureStartedFolded}) anim={_isFoldAnimationRunning} " +
            $"button={IsMouseButtonPressed()} active={IsActive} bounds={Left:0.#},{Top:0.#} {Width:0.#}x{Height:0.#} | {message}"));
    }

    private void TraceMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        var w = wParam.ToInt64();
        var l = lParam.ToInt64();
        string? text = msg switch
        {
            0x0006 => $"WM_ACTIVATE state={w & 0xffff}",
            0x0021 => $"WM_MOUSEACTIVATE hit={l & 0xffff} mouseMessage=0x{(l >> 16) & 0xffff:X}",
            0x0112 => $"WM_SYSCOMMAND 0x{w & 0xffff:X}",
            0x00A1 => $"WM_NCLBUTTONDOWN hit={w}",
            0x00A2 => $"WM_NCLBUTTONUP hit={w}",
            0x00A3 => $"WM_NCLBUTTONDBLCLK hit={w}",
            0x0201 => "WM_LBUTTONDOWN",
            0x0202 => "WM_LBUTTONUP",
            0x0203 => "WM_LBUTTONDBLCLK",
            0x0215 => "WM_CAPTURECHANGED",
            0x0231 => "WM_ENTERSIZEMOVE",
            0x0232 => "WM_EXITSIZEMOVE",
            0x0214 => $"WM_SIZING edge={w} rect={FormatNativeRect(lParam)}",
            0x0005 => $"WM_SIZE type={w} {l & 0xffff}x{(l >> 16) & 0xffff}",
            0x0246 => "WM_POINTERDOWN",
            0x0247 => "WM_POINTERUP",
            _ => null,
        };
        if (text != null) Trace(text);
    }

    private static string FormatNativeRect(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero) return "-";
        var r = Marshal.PtrToStructure<RECT>(lParam);
        return $"{r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}";
    }
}
