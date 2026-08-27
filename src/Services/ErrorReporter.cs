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

using System.IO;
using System.Text;

namespace ScreenStickyNotes.Services;

public static class ErrorReporter
{
    private static readonly object Sync = new();

    public static string LogPath => Path.Combine(StorageService.DataRoot, "logs", "app.log");

    public static void ReportNonFatal(string operation, Exception exception)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, Format(operation, exception), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never become the reason the app exits.
        }
    }

    private static string Format(string operation, Exception exception)
        => $"""
           [{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {operation}
           {exception}

           """;
}
