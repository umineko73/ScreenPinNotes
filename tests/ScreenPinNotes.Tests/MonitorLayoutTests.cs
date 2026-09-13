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
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

/// <summary>
/// モニタ構成が変わったときの付箋の置き場所。矩形はすべて物理ピクセル。
/// </summary>
public class MonitorLayoutTests
{
    private static MonitorInfo Primary(double width = 1920, double height = 1040, double scale = 1)
        => new(new Rect(0, 0, width, height), scale, true);

    // プライマリの右側に並ぶ2台目。拡大率は別々に持てる。
    private static MonitorInfo Secondary(double x = 1920, double width = 2560, double height = 1400,
        double scale = 1.5)
        => new(new Rect(x, 0, width, height), scale, false);

    [Fact]
    public void NoteInsideWorkAreaIsReachable()
        => Assert.True(MonitorLayout.IsReachable(new Rect(100, 100, 260, 220), [Primary()]));

    [Fact]
    public void NoteHangingOffTheBottomEdgeStaysWhereTheUserPutIt()
    {
        // 下にはみ出していてもタイトルバーは掴める。勝手に動かさない。
        Assert.True(MonitorLayout.IsReachable(new Rect(100, 1000, 260, 220), [Primary()]));
    }

    [Fact]
    public void NoteLeftBeyondTheShrunkScreenIsNotReachable()
    {
        // 3840x2160 のときに置いた付箋。解像度が 1920x1080 に変わると画面外に残る。
        Assert.False(MonitorLayout.IsReachable(new Rect(3000, 1500, 260, 220), [Primary()]));
    }

    [Fact]
    public void NoteOnDisconnectedMonitorIsNotReachable()
    {
        var note = new Rect(2200, 300, 260, 220);
        Assert.True(MonitorLayout.IsReachable(note, [Primary(), Secondary()]));
        Assert.False(MonitorLayout.IsReachable(note, [Primary()]));
    }

    [Fact]
    public void NoteWhoseTitleBarIsAboveTheScreenIsNotReachable()
        => Assert.False(MonitorLayout.IsReachable(new Rect(100, -200, 260, 220), [Primary()]));

    [Fact]
    public void NoteWithOnlyASliverVisibleIsNotReachable()
        => Assert.False(MonitorLayout.IsReachable(new Rect(1900, 100, 260, 220), [Primary()]));

    [Fact]
    public void MonitorsAreNotJudgedWhenTheyCannotBeRead()
        => Assert.True(MonitorLayout.IsReachable(new Rect(9000, 9000, 260, 220), []));

    [Fact]
    public void RescueKeepsTheSizeAndMovesIntoTheRemainingMonitor()
    {
        var rescued = MonitorLayout.Rescue(new Rect(2200, 300, 260, 220), [Primary()]);
        Assert.Equal(new Size(260, 220), rescued.Size);
        Assert.True(MonitorLayout.IsReachable(rescued, [Primary()]));
        Assert.Equal(new Rect(1920 - 260, 300, 260, 220), rescued);
    }

    [Fact]
    public void RescuePrefersTheMonitorItStillOverlaps()
    {
        // 2台目の解像度が縮み、付箋がその右端からはみ出した構成。
        var narrowed = Secondary(width: 1280);
        var rescued = MonitorLayout.Rescue(new Rect(3000, 200, 400, 300), [Primary(), narrowed]);
        Assert.Equal(new Rect(narrowed.WorkArea.Right - 400, 200, 400, 300), rescued);
    }

    [Fact]
    public void RescueAlignsNotesLargerThanTheWorkAreaToItsTopLeft()
    {
        var small = new MonitorInfo(new Rect(0, 0, 300, 200), 1, true);
        Assert.Equal(new Rect(0, 0, 600, 500), MonitorLayout.Rescue(new Rect(2000, 900, 600, 500), [small]));
    }

    [Fact]
    public void RescueIsStableOnceTheNoteIsVisible()
    {
        var monitors = new[] { Primary() };
        var once = MonitorLayout.Rescue(new Rect(3000, 1500, 260, 220), monitors);
        Assert.Equal(once, MonitorLayout.Rescue(once, monitors));
    }

    [Fact]
    public void SignatureDistinguishesResolutionAndScaleChanges()
    {
        var signature = MonitorLayout.Signature([Primary(), Secondary()]);
        Assert.NotEqual(signature, MonitorLayout.Signature([Primary(1280, 680), Secondary()]));
        Assert.NotEqual(signature, MonitorLayout.Signature([Primary(), Secondary(scale: 1.25)]));
        Assert.NotEqual(signature, MonitorLayout.Signature([Primary()]));
    }

    [Fact]
    public void SignatureDoesNotDependOnEnumerationOrder()
        => Assert.Equal(MonitorLayout.Signature([Primary(), Secondary()]),
                        MonitorLayout.Signature([Secondary(), Primary()]));

    [Fact]
    public void PrimaryScaleFallsBackToOneWithoutMonitors()
    {
        Assert.Equal(1.5, MonitorLayout.PrimaryScale([Primary(scale: 1.5), Secondary()]));
        Assert.Equal(1, MonitorLayout.PrimaryScale([]));
    }

    [Fact]
    public void CurrentReadsTheRealMonitorsOfThisMachine()
    {
        var monitors = MonitorLayout.Current();
        Assert.NotEmpty(monitors);
        Assert.Single(monitors, m => m.IsPrimary);
        Assert.All(monitors, m =>
        {
            Assert.True(m.WorkArea.Width > 0 && m.WorkArea.Height > 0);
            Assert.True(m.Scale >= 1);
        });
    }
}
