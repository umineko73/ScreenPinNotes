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
using System.Media;
using System.Windows.Threading;

namespace ScreenPinNotes.Services;

/// <summary>
/// リマインダーで鳴らす音。コントロールパネルの「サウンド」で選べるのと同じ
/// %WINDIR%\Media の .wav を候補にする（テーマ別のサブフォルダーは含めない）。
/// </summary>
public static class ReminderSound
{
    public const string DefaultSound = "Windows Notify Calendar.wav";

    // 再生中に回収されて音が途切れないよう、最後に鳴らしたものを持っておく。
    private static SoundPlayer? _player;
    // 繰り返している音を鳴らした付箋。同時に届いたリマインダーは後の方が音を引き継ぐので、
    // 前の付箋をクリックしても後の付箋の音は止めない。
    private static object? _owner;
    private static DispatcherTimer? _stopTimer;

    public static string MediaFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    /// <summary>選べる音のファイル名（拡張子付き）。名前順。</summary>
    public static IReadOnlyList<string> ListSystemSounds(string? folder = null)
    {
        try
        {
            return Directory.EnumerateFiles(folder ?? MediaFolder, "*.wav")
                .Select(Path.GetFileName)
                .OfType<string>()
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>一覧に出す名前。".wav" を外すだけで、コントロールパネルの表記とそろう。</summary>
    public static string DisplayName(string sound) => Path.GetFileNameWithoutExtension(sound);

    /// <summary>設定値を実際のファイルに直す。ファイル名だけなら Windows のサウンドとして探す。</summary>
    public static string? Resolve(string? sound, string? folder = null)
    {
        if (string.IsNullOrWhiteSpace(sound)) return null;
        var path = Path.IsPathRooted(sound) ? sound : Path.Combine(folder ?? MediaFolder, sound);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// 音を鳴らす（呼び出し元は待たない）。ファイルが見つからない・読めないときは
    /// 無音にせず、Windows の既定の警告音で代える。
    /// </summary>
    public static void Play(string? sound)
    {
        Stop();
        var path = Resolve(sound);
        if (path != null)
        {
            try
            {
                _player = new SoundPlayer(path);
                _player.Play();
                return;
            }
            catch (Exception ex)
            {
                ErrorReporter.ReportNonFatal("Play reminder sound", ex);
            }
        }
        SystemSounds.Exclamation.Play();
    }

    /// <summary>
    /// リマインダーの音を <paramref name="duration"/> のあいだ繰り返す。<paramref name="owner"/>
    /// （知らせている付箋）を <see cref="StopFor"/> に渡すと途中で止まる。UI スレッドから呼ぶ。
    /// </summary>
    public static void PlayForReminder(string? sound, TimeSpan duration, object owner)
    {
        Stop();
        var path = Resolve(sound);
        if (path == null)
        {
            SystemSounds.Exclamation.Play();
            return;
        }
        try
        {
            _player = new SoundPlayer(path);
            _player.PlayLooping();
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Play reminder sound", ex);
            SystemSounds.Exclamation.Play();
            return;
        }
        _owner = owner;
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => Stop();
        _stopTimer = timer;
        timer.Start();
    }

    /// <summary><paramref name="owner"/> のリマインダーの音が鳴っているか。</summary>
    public static bool IsPlayingFor(object owner) => _owner != null && ReferenceEquals(_owner, owner);

    /// <summary><paramref name="owner"/> が鳴らしている音なら止める。</summary>
    public static void StopFor(object owner)
    {
        if (IsPlayingFor(owner)) Stop();
    }

    /// <summary>鳴っている音を止める。</summary>
    public static void Stop()
    {
        _stopTimer?.Stop();
        _stopTimer = null;
        _owner = null;
        try { _player?.Stop(); }
        catch (Exception ex) { ErrorReporter.ReportNonFatal("Stop reminder sound", ex); }
        _player = null;
    }
}
