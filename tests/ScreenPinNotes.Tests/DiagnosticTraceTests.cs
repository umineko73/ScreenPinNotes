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

using System.IO;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

[Collection("DiagnosticTrace")]
public class DiagnosticTraceTests
{
    [Fact]
    public void Write_RecordsOnlyWhileEnabled()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ScreenPinNotes.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "logs", "trace.log");
        DiagnosticTrace.LogPath = path;
        try
        {
            DiagnosticTrace.Enabled = false;
            DiagnosticTrace.Write("while disabled");
            Assert.False(File.Exists(path));

            DiagnosticTrace.Enabled = true;
            DiagnosticTrace.Write("while enabled");
            Assert.Contains("while enabled", File.ReadAllText(path));
        }
        finally
        {
            DiagnosticTrace.Enabled = false;
            DiagnosticTrace.LogPath = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Caller_NamesTheAppFramesThatCalled()
        => Assert.Contains(nameof(Caller_NamesTheAppFramesThatCalled), DiagnosticTrace.Caller());

    [Fact]
    public void Settings_KeepTheTraceOffByDefault()
        => Assert.False(new Models.AppSettings().EnableDiagnosticTrace);
}
