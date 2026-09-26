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

using System.IO;
using System.Windows.Threading;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public sealed class ReminderSoundTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ScreenPinNotesSound_" + Guid.NewGuid().ToString("N"));

    public ReminderSoundTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    // テーマ別のサブフォルダーや .wav 以外は候補に入れない。
    [Fact]
    public void ListSystemSounds_ListsOnlyTopLevelWavFilesByName()
    {
        File.WriteAllText(Path.Combine(_folder, "b.wav"), "");
        File.WriteAllText(Path.Combine(_folder, "A.wav"), "");
        File.WriteAllText(Path.Combine(_folder, "flourish.mid"), "");
        Directory.CreateDirectory(Path.Combine(_folder, "Garden"));
        File.WriteAllText(Path.Combine(_folder, "Garden", "c.wav"), "");

        Assert.Equal(["A.wav", "b.wav"], ReminderSound.ListSystemSounds(_folder));
        Assert.Empty(ReminderSound.ListSystemSounds(Path.Combine(_folder, "missing")));
    }

    [Fact]
    public void Resolve_FindsNamesInTheFolderAndAcceptsFullPaths()
    {
        var path = Path.Combine(_folder, "Ding.wav");
        File.WriteAllText(path, "");

        Assert.Equal(path, ReminderSound.Resolve("Ding.wav", _folder));
        Assert.Equal(path, ReminderSound.Resolve(path, Path.Combine(_folder, "other")));
        Assert.Null(ReminderSound.Resolve("Missing.wav", _folder));
        Assert.Null(ReminderSound.Resolve("", _folder));
    }

    [Fact]
    public void DisplayName_DropsTheExtension()
        => Assert.Equal("Windows Notify Calendar", ReminderSound.DisplayName(ReminderSound.DefaultSound));

    // Windows 標準の音が既定で、空に書き換えられても既定へ戻す。
    [Fact]
    public void Setting_DefaultsToAWindowsSound()
    {
        Assert.Equal(ReminderSound.DefaultSound, new AppSettings().ReminderSound);
        var settings = new AppSettings { ReminderSound = "  " };
        settings.Normalize();
        Assert.Equal(ReminderSound.DefaultSound, settings.ReminderSound);
    }

    // 音の項目が無い（旧版で保存した）リマインダーは、通知ウィンドウを出すときだけ鳴っていた。
    [Theory]
    [InlineData(null, null, true)]
    [InlineData(false, null, false)]
    [InlineData(true, null, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    public void PlaysSound_FallsBackToTheAlertWindow(bool? showAlert, bool? playSound, bool expected)
        => Assert.Equal(expected, new ReminderSettings { ShowAlert = showAlert, PlaySound = playSound }.PlaysSound);

    [Theory]
    [InlineData("ja", "リマインダーの音", "サウンドを30秒間繰り返す（音は設定画面で選択）", "付箋を30秒間点滅（非表示の付箋も表示）", "サウンド", "点滅・サウンドの長さ（秒）")]
    [InlineData("en", "Reminder sound", "Repeat a sound for 30 seconds (chosen in Settings)", "Flash the note for 30 seconds (shows hidden notes)", "Sound", "Flash and sound length (seconds)")]
    public void Strings_AreLocalized(string language, string setting, string option, string flash, string method, string length)
    {
        Assert.Equal(setting, LocalizationService.T("SettingsReminderSound", language));
        Assert.Equal(option, string.Format(LocalizationService.T("ReminderPlaySound", language), 30));
        Assert.Equal(flash, string.Format(LocalizationService.T("ReminderFlashNote", language), 30));
        Assert.Equal(method, LocalizationService.T("ReminderMethodSound", language));
        Assert.Equal(length, LocalizationService.T("SettingsReminderAlertSeconds", language));
        Assert.NotEqual("SettingsReminderAlertSecondsHint", LocalizationService.T("SettingsReminderAlertSecondsHint", language));
    }

    // 既定は30秒。0 や極端な値は、気付ける・鳴りっぱなしにならない範囲へ戻す。
    [Theory]
    [InlineData(30, 30)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(100_000, AppSettings.MaxReminderAlertSeconds)]
    public void AlertSeconds_DefaultsTo30AndIsClamped(int value, int expected)
    {
        Assert.Equal(30, new AppSettings().ReminderAlertSeconds);
        var settings = new AppSettings { ReminderAlertSeconds = value };
        settings.Normalize();
        Assert.Equal(expected, settings.ReminderAlertSeconds);
    }

    // 鳴らした付箋でだけ止まり、指定の長さが過ぎれば自然に止まる。
    [WpfFact]
    public void PlayForReminder_StopsForItsOwnerOrAfterTheDuration()
    {
        var sound = SilentWav(Path.Combine(_folder, "silent.wav"));
        var owner = new object();
        var other = new object();
        try
        {
            ReminderSound.PlayForReminder(sound, TimeSpan.FromMinutes(1), owner);
            Assert.True(ReminderSound.IsPlayingFor(owner));
            ReminderSound.StopFor(other);
            Assert.True(ReminderSound.IsPlayingFor(owner));
            ReminderSound.StopFor(owner);
            Assert.False(ReminderSound.IsPlayingFor(owner));

            ReminderSound.PlayForReminder(sound, TimeSpan.FromMilliseconds(100), owner);
            Assert.True(ReminderSound.IsPlayingFor(owner));
            PumpUntil(() => !ReminderSound.IsPlayingFor(owner), TimeSpan.FromSeconds(5));
            Assert.False(ReminderSound.IsPlayingFor(owner));
        }
        finally { ReminderSound.Stop(); }
    }

    /// <summary>無音の短い .wav を書く（テスト中に音を出さないため）。</summary>
    internal static string SilentWav(string path)
    {
        const int sampleRate = 8000, samples = 800;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + samples); writer.Write("WAVE"u8);
        writer.Write("fmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(sampleRate); writer.Write(sampleRate); writer.Write((short)1); writer.Write((short)8);
        writer.Write("data"u8); writer.Write(samples);
        for (var i = 0; i < samples; i++) writer.Write((byte)128);
        return path;
    }

    internal static void PumpUntil(Func<bool> done, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (!done() && DateTime.UtcNow < until)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
    }
}
