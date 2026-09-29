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

using Microsoft.Win32;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

/// <summary>
/// 常駐中に変えた環境変数を、付箋から開くアプリへ渡す。
/// </summary>
public class UserEnvironmentTests
{
    private static Dictionary<string, string> Vars(params (string Name, string Value)[] vars)
        => vars.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void AppliesOnlyWhatChangedSinceTheLastRead()
    {
        var before = Vars(("Path", @"C:\a"), ("KEEP", "1"), ("GONE", "x"));
        var after = Vars(("Path", @"C:\a;C:\b"), ("KEEP", "1"), ("NEW", "y"));
        var applied = new Dictionary<string, string?>();

        UserEnvironment.ApplyChanges(before, after, (name, value) => applied[name] = value);

        Assert.Equal(3, applied.Count);
        Assert.Equal(@"C:\a;C:\b", applied["Path"]);
        Assert.Equal("y", applied["NEW"]);
        Assert.Null(applied["GONE"]);
        // 変わっていない変数には触れない。起動した側が上書きして渡した値を消さないため。
        Assert.False(applied.ContainsKey("KEEP"));
    }

    [Fact]
    public void ReadsTheCurrentEnvironmentFromTheSystem()
    {
        var current = UserEnvironment.TryReadCurrent();
        Assert.NotNull(current);
        Assert.True(current!.ContainsKey("SystemRoot"));
        Assert.True(current.ContainsKey("Path"));
        Assert.DoesNotContain(current.Keys, name => name.StartsWith('='));
    }

    // レジストリのユーザー環境変数を実際に書き換えて確かめる。書いた変数は必ず消す。
    [Fact]
    public void RefreshPicksUpVariablesChangedAfterStartup()
    {
        var name = "SCREENPINNOTES_TEST_" + Guid.NewGuid().ToString("N");
        using var key = Registry.CurrentUser.OpenSubKey("Environment", writable: true)!;
        try
        {
            UserEnvironment.CaptureBaseline();
            Assert.Null(Environment.GetEnvironmentVariable(name));

            key.SetValue(name, "added");
            UserEnvironment.Refresh();
            Assert.Equal("added", Environment.GetEnvironmentVariable(name));

            key.SetValue(name, "changed");
            UserEnvironment.Refresh();
            Assert.Equal("changed", Environment.GetEnvironmentVariable(name));

            key.DeleteValue(name);
            UserEnvironment.Refresh();
            Assert.Null(Environment.GetEnvironmentVariable(name));
        }
        finally
        {
            key.DeleteValue(name, throwOnMissingValue: false);
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
