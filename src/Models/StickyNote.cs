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

using System.Text.Json.Serialization;

namespace ScreenPinNotes.Models;

public class StickyNote
{
    private static readonly string[] JapaneseDayNames = ["日", "月", "火", "水", "木", "金", "土"];
    private static readonly string[] EnglishDayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonIgnore] // content.md に別途保存
    public string Content { get; set; } = "";
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 260;

    // 折りたたみ時の位置と直近の表示幅。
    public double? FoldedX { get; set; }
    public double? FoldedY { get; set; }
    public double? FoldedWidth { get; set; }
    // 手動リサイズで指定した幅。未指定なら内容に合わせて自動調整する。
    public double? ManualFoldedWidth { get; set; }
    public bool ArePropertiesCollapsed { get; set; }

    public double Height { get; set; } = 220;
    public double? EditWidth { get; set; }
    public double? EditHeight { get; set; }
    // タイトルバーに表示する文字列。空/未設定なら本文の1行目を表示する。
    public string? Title { get; set; }

    public string ColorKey { get; set; } = "yellow";
    // ColorKey が "custom" のときの背景色・アクセント色（"#RRGGBB"）。タイトルバーは背景色から作る。
    public string? CustomBackgroundColor { get; set; }
    public string? CustomAccentColor { get; set; }
    public string Icon { get; set; } = "";   // タイトルバーに表示する絵文字。空 = なし
    public string FontFamily { get; set; } = "Yu Gothic UI";
    public double FontSize { get; set; } = 13;
    public double TitleFontSize { get; set; } = 12;
    public int OpacityPercent { get; set; } = 100;
    public bool IsTopmost { get; set; } = false;
    public int LayerOrder { get; set; }
    public bool IsFolded { get; set; } = false;
    public bool IsHidden { get; set; } = false;
    public bool IsReadOnly { get; set; } = false;
    public bool IsPositionSeparated { get; set; } = false;

    /// <summary>
    /// X/Y（と FoldedX/FoldedY）を保存したときのモニタ構成。
    /// <see cref="Services.MonitorLayout.Signature"/> の文字列で、空は未記録
    /// （この機能より前に保存された付箋・作りたて）。構成が違うあいだは
    /// 位置を書き換えず、一時的に映せるモニタへ寄せるだけにする。
    /// </summary>
    public string PositionLayout { get; set; } = "";

    /// <summary>
    /// X/Y を保存したときの拡大率。論理ピクセルはそのウィンドウが居たモニタの
    /// 拡大率が基準なので、これが無いと拡大率の違うモニタへ戻せない。
    /// 0 は未記録（プライマリの拡大率として扱う）。
    /// </summary>
    public double PositionScale { get; set; }

    /// <summary>
    /// ほかのモニタ構成で置いた位置（新しい順、<see cref="Services.NoteGeometryState.MaxRememberedLayouts"/> 件まで）。
    /// ドッキングを外したノートPCで動かしても、ドッキングし直せばその構成の位置へ帰れるように、
    /// 構成ごとのホームを残しておく。今の構成（<see cref="PositionLayout"/>）の分は X/Y 側にあり、ここには入れない。
    /// </summary>
    public List<LayoutPosition> OtherLayoutPositions { get; set; } = [];

    /// <summary>
    /// タイトルバーを常時は出さず、右上にホバーで重ねる表示にするか。
    /// 本文だけの見た目にしたい人向け。このとき折りたたみは本文の1行目だけを残す。
    /// </summary>
    public bool IsTitleBarHidden { get; set; } = false;
    public string? ExternalContentPath { get; set; }
    /// <summary>
    /// 外部ファイルを tail 表示するか。true のときは全文ではなく末尾の
    /// 設定行数だけを読み込み、Markdown ではなくプレーンテキストとして表示し、
    /// 更新のたびに末尾へ自動スクロールする（ログ監視向け）。
    /// </summary>
    public bool ExternalTailMode { get; set; }
    public ReminderSettings? Reminder { get; set; }
    public Dictionary<string, double> ExternalImageWidthOverrides { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public bool IsExternalContent =>
        !string.IsNullOrWhiteSpace(ExternalContentPath);

    [JsonIgnore]
    public bool HasReminder =>
        Reminder?.NextAt != null;

    public static string CreateDefaultTitle(DateTime timestamp)
        => $"{timestamp:yyyy/MM/dd}({JapaneseDayNames[(int)timestamp.DayOfWeek]}) {timestamp:HH:mm:ss}";

    public static string CreateDefaultTitle(DateTime timestamp, bool english)
        => english
            ? $"{timestamp:yyyy/MM/dd}({EnglishDayNames[(int)timestamp.DayOfWeek]}) {timestamp:HH:mm:ss}"
            : CreateDefaultTitle(timestamp);
}

public sealed class ReminderSettings
{
    public List<DayOfWeek> WeekDays { get; set; } = [];
    // 毎週リマインダーで通知する「第n週」（1〜5、その曜日が月の何回目か）。空 = 毎週。
    public List<int> MonthWeeks { get; set; } = [];
    public int MonthDay { get; set; } = 1;
    // 毎月の最終日。MonthDay も 31 にしておくと、この項目を知らない旧版でも月末に通知される。
    public bool MonthLastDay { get; set; }
    public TimeSpan? TimeOfDay { get; set; }
    public bool WindowsNotification { get; set; } = true;
    // null = 未設定（この機能追加より前に保存されたリマインダー）。
    // 新規に作成されたリマインダーはダイアログが必ず true/false を明示的に書き込む。
    // TriggerReminder 側は null を「従来どおりアラートを出す」として扱う。
    public bool? ShowAlert { get; set; }
    public bool FlashNote { get; set; } = true;
    // null = 未設定（この機能追加より前に保存されたリマインダー）。当時は通知ウィンドウを
    // 出すときだけ音が鳴ったので、それに合わせて <see cref="PlaysSound"/> で読み替える。
    // 鳴らす音そのものは付箋ごとではなく AppSettings.ReminderSound で選ぶ。
    public bool? PlaySound { get; set; }
    public DateTime? NextAt { get; set; }
    public string Recurrence { get; set; } = "None";
    public DateTime? LastTriggeredAt { get; set; }

    /// <summary>届いたときに音を鳴らすか。未設定なら旧版と同じく通知ウィンドウの有無に従う。</summary>
    [JsonIgnore]
    public bool PlaysSound => PlaySound ?? ShowAlert != false;
}

/// <summary>ある1つのモニタ構成での位置。値の意味は <see cref="StickyNote"/> の同名の項目と同じ。</summary>
public sealed class LayoutPosition
{
    public string Layout { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double? FoldedX { get; set; }
    public double? FoldedY { get; set; }
    public double Scale { get; set; }
}
