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
using System.Text;

namespace ScreenPinNotes.Services;

/// <summary>
/// 特定の環境でしか起きない不具合を追うための記録。既定では何も書かない。
/// settings.json の EnableDiagnosticTrace か、環境変数 SCREENPINNOTES_TRACE=1 で有効になる。
/// 付箋の本文やタイトルは書かない（職場の PC からそのまま送ってもらえるように）。
/// </summary>
public static class DiagnosticTrace
{
    public const string EnvVar = "SCREENPINNOTES_TRACE";
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _logPath;

    public static bool Enabled { get; set; }

    /// <summary>既定はデータフォルダの logs\trace.log。テストでは差し替える（null で既定に戻る）。</summary>
    public static string? LogPath
    {
        get => _logPath ?? Path.Combine(StorageService.DataRoot, "logs", "trace.log");
        set => _logPath = value;
    }

    public static void Write(string message)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                var path = LogPath!;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // 長く有効にしたままでも膨らみ続けないよう、1世代だけ残して切り替える。
                if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                    File.Move(path, Path.ChangeExtension(path, ".old.log"), overwrite: true);
                File.AppendAllText(path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // 記録の失敗で付箋の操作を妨げない。
        }
    }

    /// <summary>このアプリのどの処理から呼ばれたか（呼び出し元から順に最大8段）。</summary>
    public static string Caller()
    {
        var methods = new StackTrace(1, false).GetFrames()
            .Select(frame => frame.GetMethod())
            .Where(method => method?.DeclaringType?.Namespace?.StartsWith("ScreenPinNotes", StringComparison.Ordinal) == true)
            .Take(8)
            .Select(method => $"{method!.DeclaringType!.Name}.{method.Name}");
        return string.Join(" < ", methods);
    }
}
