using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class LocalizationServiceTests
{
    [Theory]
    [InlineData("ja", "はい")]
    [InlineData("ja-JP", "はい")]
    [InlineData("en-US", "Yes")]
    [InlineData("fr", "Yes")]
    public void TranslatesWithParentAndEnglishFallback(string culture, string expected)
        => Assert.Equal(expected, LocalizationService.T("Yes", culture));

    [Fact]
    public void DiscoversCompiledJapaneseResources()
    {
        Assert.Contains(LocalizationService.Languages, language => language.Code == "ja");
        Assert.Contains(LocalizationService.Languages, language => language.Code == "en");
        Assert.Equal("UnknownKey", LocalizationService.T("UnknownKey", "ja"));
    }

    // 見つからないキーはキー名がそのまま返るので、この比較が通れば
    // .resx に実際に載っていることの確認になる。
    [Theory]
    [InlineData("ja", "タイトルバーを隠す")]
    [InlineData("en", "Hide the title bar")]
    public void HideTitleBarIsInTheCatalog(string culture, string expected)
        => Assert.Equal(expected, LocalizationService.T("HideTitleBar", culture));

    [Fact]
    public void ResourcePlaceholdersRemainUsable()
    {
        Assert.Equal("本文 15pt", string.Format(LocalizationService.T("BodySize", "ja"), 15));
        Assert.Equal("Body 15pt", string.Format(LocalizationService.T("BodySize", "en"), 15));
    }

    // 繰り返しリマインダー機能で追加したキー。両方の .resx に実際の文言が
    // 載っていることの確認になる（見つからないキーはキー名がそのまま返る）。
    [Theory]
    [InlineData("ReminderRepeat", "Repeat", "繰り返し")]
    [InlineData("ReminderRepeatNone", "Once", "1回のみ")]
    [InlineData("ReminderRepeatDaily", "Every day", "毎日")]
    [InlineData("ReminderRepeatWeekly", "Every week", "毎週")]
    [InlineData("ReminderRepeatMonthly", "Every month", "毎月")]
    [InlineData("ReminderMonthlyHint", "Day of month. Shorter months use their final day.", "毎月の通知日。指定日がない月は月末に通知します。")]
    [InlineData("ReminderWindowsNotification", "Windows notification", "Windowsの通知を表示")]
    [InlineData("ReminderShowAlert", "Also open an alert with snooze options", "スヌーズできる通知ウィンドウも開く")]
    [InlineData("ReminderRunningHint",
        "Keep ScreenPinNotes running in the tray. Missed reminders appear once when it resumes. Windows notification settings control banner visibility.",
        "ScreenPinNotesをトレイで起動しておいてください。休止中の通知は復帰後に1回にまとめます。バナー表示はWindowsの通知設定に従います。")]
    [InlineData("ReminderChooseOptions",
        "Select a notification method and at least one weekday for weekly reminders.",
        "通知方法を選択してください。毎週の場合は曜日も1つ以上選択してください。")]
    [InlineData("ReminderFutureRequired", "Select a future date and time.", "未来の日時を指定してください。")]
    public void ReminderRecurrenceStringsAreInTheCatalog(string key, string english, string japanese)
    {
        Assert.Equal(english, LocalizationService.T(key, "en"));
        Assert.Equal(japanese, LocalizationService.T(key, "ja"));
    }

    // タイトルバー非表示の目印を設定画面から変えられるようにしたときのキー。
    [Theory]
    [InlineData("SettingsTitleBarSpine", "Hidden title bar marker", "タイトルバー非表示の目印")]
    [InlineData("SettingsShowTitleBarSpine", "Show a spine on the left edge", "左端に帯を出す")]
    [InlineData("SettingsTitleBarSpineWidth", "Spine width", "帯の太さ")]
    [InlineData("SettingsNoteCorner", "Corner radius", "角の丸み")]
    [InlineData("SettingsNoteCornerSquare", "Square", "丸めない")]
    [InlineData("SettingsNoteBorder", "Border color", "枠の色")]
    [InlineData("SettingsNoteBorderNone", "None", "なし")]
    [InlineData("SettingsNoteBorderGray", "Gray", "グレー")]
    [InlineData("SettingsNoteBorderNoteColor", "Note color", "付箋の色")]
    [InlineData("SettingsIconColor", "Icon color", "アイコンの色")]
    [InlineData("SettingsMonochromeIcons", "Show in monochrome", "モノクロで表示")]
    public void TitleBarSpineStringsAreInTheCatalog(string key, string english, string japanese)
    {
        Assert.Equal(english, LocalizationService.T(key, "en"));
        Assert.Equal(japanese, LocalizationService.T(key, "ja"));
    }
}
