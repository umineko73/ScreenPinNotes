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

using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ScreenPinNotes.Services;

public sealed class GlobalNoteHotkey : IDisposable
{
    public const string DefaultGesture = "Ctrl+Alt+N";
    // Ctrl+Alt+V is Office's Paste Special; a global registration would take it away.
    public const string DefaultClipboardGesture = "Ctrl+Alt+Shift+N";
    private readonly HwndSource _source;
    private int _registeredId;
    public string Gesture { get; private set; } = "";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    public GlobalNoteHotkey(Action pressed)
    {
        _source = new HwndSource(new HwndSourceParameters("ScreenPinNotesHotkey")
        { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        _source.AddHook((IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (message == 0x0312 && _registeredId != 0 && w.ToInt32() == _registeredId)
            {
                handled = true;
                pressed();
            }
            return IntPtr.Zero;
        });
    }

    public static bool TryParse(string? text, out uint modifiers, out uint virtualKey, out string normalized)
    {
        modifiers = virtualKey = 0;
        normalized = "";
        if (string.IsNullOrWhiteSpace(text)) return true;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;
        foreach (var part in parts[..^1])
        {
            var flag = part.ToLowerInvariant() switch { "ctrl" => 2u, "alt" => 1u, "shift" => 4u, _ => 0u };
            if (flag == 0 || (modifiers & flag) != 0) return false;
            modifiers |= flag;
        }
        // Shift alone would intercept normal typing.
        if ((modifiers & 3) == 0) return false;
        var key = parts[^1].ToUpperInvariant();
        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9') virtualKey = key[0];
        else if (key.StartsWith('F') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24 && f != 12) virtualKey = (uint)(0x70 + f - 1);
        else return false;
        normalized = ((modifiers & 2) != 0 ? "Ctrl+" : "") + ((modifiers & 1) != 0 ? "Alt+" : "") + ((modifiers & 4) != 0 ? "Shift+" : "") + key;
        return true;
    }

    public bool TrySet(string text)
    {
        if (!TryParse(text, out var modifiers, out var key, out var normalized)) return false;
        if (normalized == Gesture) return true;
        var nextId = _registeredId == 1 ? 2 : 1;
        if (normalized.Length > 0 && !RegisterHotKey(_source.Handle, nextId, modifiers | 0x4000, key)) return false;
        if (_registeredId != 0) UnregisterHotKey(_source.Handle, _registeredId);
        _registeredId = normalized.Length == 0 ? 0 : nextId;
        Gesture = normalized;
        return true;
    }

    public void Dispose()
    {
        if (_registeredId != 0) UnregisterHotKey(_source.Handle, _registeredId);
        _registeredId = 0;
        _source.Dispose();
    }
}
