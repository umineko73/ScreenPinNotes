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

using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using ScreenStickyNotes.Models;
using ScreenStickyNotes.Services;
using ScreenStickyNotes.ViewModels;
using ScreenStickyNotes.Views;

namespace ScreenStickyNotes;

public partial class App : System.Windows.Application
{
    public new static App Current => (App)System.Windows.Application.Current;

    private readonly StorageService _storage = new();
    private readonly List<StickyNoteWindow> _windows = [];
    private AppSettings _settings = new();
    private NotifyIcon? _trayIcon;

    public IReadOnlyList<StickyNoteWindow> NoteWindows => _windows;
    public AppSettings Settings => _settings;

    // ─── 二重起動防止 ────────────────────────────────────────────
    // 複数インスタンスが同じ notes フォルダを読み書きすると、
    // 一方の保存が他方のノートを壊すため 1 プロセスに制限する。

    // 名前はデータフォルダごとに分ける。別フォルダを使うインスタンス
    // （テスト用など）は互いに独立して動いてよいため。
    private static readonly string InstanceKey =
        StorageService.DataRoot.ToLowerInvariant().Replace('\\', '_').Replace(':', '_');

    private static readonly string MutexName = "ScreenStickyNotes.SingleInstance." + InstanceKey;
    private static readonly int ShowAllMessage =
        RegisterWindowMessage("ScreenStickyNotes.ShowAll." + InstanceKey);
    private const int HWND_BROADCAST = 0xFFFF;

    private Mutex? _instanceMutex;
    private System.Windows.Interop.HwndSource? _ipcWindow;

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string message);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // 既に起動済み。既存インスタンスに全表示を依頼して自分は終了する
            PostMessage(HWND_BROADCAST, ShowAllMessage, 0, 0);
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        // ログオフ・シャットダウン時にデバウンス待ちの変更を取りこぼさない
        SessionEnding += (_, _) => FlushAndSave();

        _settings = _storage.LoadSettings();
        // スタートアップ登録は既存のレジストリが実体なので、起動時にJSONへ反映する。
        _settings.StartWithWindows = StartupService.IsRegistered;
        _storage.SaveSettings(_settings);

        InitIpcWindow();
        InitTrayIcon();

        var notes = _storage.Load();
        if (notes.Count == 0)
        {
            notes = SampleNoteFactory.CreateInitialNotes(_settings, _storage);
            _storage.Save(notes);
        }

        foreach (var note in notes)
            OpenNoteWindow(note);
    }

    // 2つ目のインスタンスからのブロードキャストを受け取るための隠しウィンドウ。
    // ブロードキャストはトップレベルウィンドウにしか届かないため
    // メッセージ専用ウィンドウ（HWND_MESSAGE）は使えない。
    private void InitIpcWindow()
    {
        var parameters = new System.Windows.Interop.HwndSourceParameters("ScreenStickyNotesIpc")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,   // WS_VISIBLE を立てない = 表示されない
        };
        _ipcWindow = new System.Windows.Interop.HwndSource(parameters);
        _ipcWindow.AddHook((nint hwnd, int msg, nint w, nint l, ref bool handled) =>
        {
            if (msg == ShowAllMessage)
            {
                Dispatcher.BeginInvoke(ShowAllNotes);
                handled = true;
            }
            return 0;
        });
    }

    private void InitTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "ScreenStickyNotes",
            Visible = true,
        };
        _trayIcon.ContextMenuStrip = BuildTrayMenu();

        // 左クリックで全表示トグル
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ToggleAllNotes();
        };
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var startupItem = new ToolStripMenuItem(LocalizationService.T("TrayStartup"))
        {
            Checked = _settings.StartWithWindows,
            CheckOnClick = false,
        };
        startupItem.Click += (_, _) =>
        {
            if (StartupService.IsRegistered)
            {
                StartupService.Unregister();
                startupItem.Checked = false;
                _settings.StartWithWindows = false;
            }
            else
            {
                StartupService.Register();
                startupItem.Checked = true;
                _settings.StartWithWindows = true;
            }
            _storage.SaveSettings(_settings);
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(LocalizationService.T("TrayShowAll"), null, (_, _) => ShowAllNotes());
        menu.Items.Add(LocalizationService.T("TrayHideAll"), null, (_, _) => HideAllNotes());
        menu.Items.Add("-");
        menu.Items.Add(LocalizationService.T("TrayNewNote"), null, (_, _) => AddNewNote());
        menu.Items.Add("-");
        menu.Items.Add(startupItem);
        menu.Items.Add(BuildTitlePreviewTooltipItem());
        menu.Items.Add(BuildFoldAnimationItem());
        menu.Items.Add(BuildFoldButtonItem());
        menu.Items.Add(BuildLanguageMenu());
        menu.Items.Add(BuildDarkModeItem());
        menu.Items.Add("-");
        menu.Items.Add(LocalizationService.T("TrayAbout"), null, (_, _) => ShowAboutWindow());
        menu.Items.Add(LocalizationService.T("TrayExit"), null, (_, _) => ExitApp());
        return menu;
    }

    private ToolStripMenuItem BuildTitlePreviewTooltipItem()
    {
        var item = new ToolStripMenuItem(LocalizationService.T("TrayTitlePreviewTooltip"))
        {
            Checked = _settings.ShowTitlePreviewTooltip,
            CheckOnClick = false,
        };
        item.Click += (_, _) => SetTitlePreviewTooltipEnabled(!_settings.ShowTitlePreviewTooltip);
        return item;
    }

    private ToolStripMenuItem BuildFoldAnimationItem()
    {
        var item = new ToolStripMenuItem(LocalizationService.T("TrayFoldAnimation"))
        {
            Checked = _settings.EnableFoldAnimation,
            CheckOnClick = false,
        };
        item.Click += (_, _) => SetFoldAnimationEnabled(!_settings.EnableFoldAnimation);
        return item;
    }

    private ToolStripMenuItem BuildFoldButtonItem()
    {
        var item = new ToolStripMenuItem(LocalizationService.T("TrayFoldButton"))
        {
            Checked = _settings.ShowFoldButton,
            CheckOnClick = false,
        };
        item.Click += (_, _) => SetFoldButtonVisible(!_settings.ShowFoldButton);
        return item;
    }

    private ToolStripMenuItem BuildLanguageMenu()
    {
        var languageItem = new ToolStripMenuItem(LocalizationService.T("TrayLanguage"));
        var japaneseItem = new ToolStripMenuItem(LocalizationService.T("TrayLanguageJapanese"))
        {
            Checked = !UsesEnglishLanguage(),
            CheckOnClick = false,
        };
        var englishItem = new ToolStripMenuItem(LocalizationService.T("TrayLanguageEnglish"))
        {
            Checked = UsesEnglishLanguage(),
            CheckOnClick = false,
        };

        japaneseItem.Click += (_, _) => SetLanguage("ja");
        englishItem.Click += (_, _) => SetLanguage("en");
        languageItem.DropDownItems.Add(japaneseItem);
        languageItem.DropDownItems.Add(englishItem);
        return languageItem;
    }

    private ToolStripMenuItem BuildDarkModeItem()
    {
        var item = new ToolStripMenuItem(LocalizationService.T("TrayDarkMode"))
        {
            Checked = IsDarkTheme(),
            CheckOnClick = false,
        };
        item.Click += (_, _) => SetTheme(IsDarkTheme() ? "Light" : "Dark");
        return item;
    }

    private void SetLanguage(string language)
    {
        if (string.Equals(_settings.Language, language, StringComparison.OrdinalIgnoreCase))
            return;

        _settings.Language = language;
        ApplySettingsChange();
    }

    private void SetTheme(string theme)
    {
        if (string.Equals(_settings.Theme, theme, StringComparison.OrdinalIgnoreCase))
            return;

        _settings.Theme = theme;
        ApplySettingsChange();
    }

    private void SetTitlePreviewTooltipEnabled(bool enabled)
    {
        if (_settings.ShowTitlePreviewTooltip == enabled)
            return;

        _settings.ShowTitlePreviewTooltip = enabled;
        ApplySettingsChange();
    }

    private void SetFoldAnimationEnabled(bool enabled)
    {
        if (_settings.EnableFoldAnimation == enabled)
            return;

        _settings.EnableFoldAnimation = enabled;
        ApplySettingsChange();
    }

    private void SetFoldButtonVisible(bool visible)
    {
        if (_settings.ShowFoldButton == visible)
            return;

        _settings.ShowFoldButton = visible;
        ApplySettingsChange();
    }

    private bool UsesEnglishLanguage()
        => string.Equals(_settings.Language, "en", StringComparison.OrdinalIgnoreCase);

    private bool IsDarkTheme()
        => string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase);

    private void ApplySettingsChange()
    {
        _settings.Normalize();
        _storage.SaveSettings(_settings);
        RefreshTrayMenu();
        foreach (var win in _windows)
            win.RefreshSettings();
    }

    private void RefreshTrayMenu()
    {
        if (_trayIcon == null) return;

        var oldMenu = _trayIcon.ContextMenuStrip;
        _trayIcon.ContextMenuStrip = BuildTrayMenu();
        oldMenu?.Dispose();
    }

    // ─── アイコン ────────────────────────────────────────────────

    /// <summary>
    /// タスクトレイ用のアイコンを app.ico から読む。
    /// exe のアイコンと同じファイルを使うことで、デザインの管理を1箇所にまとめている。
    /// app.ico は複数サイズを含むので、画面の DPI に応じた大きさが選ばれる。
    /// </summary>
    private static Icon LoadTrayIcon()
    {
        var uri = new Uri("pack://application:,,,/app.ico");
        using var stream = System.Windows.Application.GetResourceStream(uri)!.Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    // ─── 付箋表示制御 ────────────────────────────────────────────

    public void ShowAllNotes()
    {
        foreach (var win in _windows)
            win.Show();
    }

    public void HideAllNotes()
    {
        foreach (var win in _windows)
            win.Hide();
    }

    private void ToggleAllNotes()
    {
        bool anyVisible = _windows.Any(w => w.IsVisible);
        if (anyVisible) HideAllNotes(); else ShowAllNotes();
    }

    // ─── About ──────────────────────────────────────────────────

    private Views.AboutWindow? _aboutWindow;

    private void ShowAboutWindow()
    {
        if (_aboutWindow != null)
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new Views.AboutWindow();
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    // ─── 付箋追加・削除 ──────────────────────────────────────────

    /// <summary>
    /// 付箋を1枚追加する。
    /// <paramref name="template"/> を渡すと、その付箋の書式
    /// （色・アイコン・フォント）を引き継ぐ。タスクトレイからの
    /// 新規作成は引き継ぎ元が無いため既定の書式になる。
    /// </summary>
    public void AddNewNote(StickyNote? template = null, double? x = null, double? y = null)
    {
        var now = DateTime.Now;
        var layout = _settings.Layout;
        var note = new StickyNote
        {
            X = x ?? layout.NewNoteBaseX + _windows.Count * layout.NewNoteCascadeStep,
            Y = y ?? layout.NewNoteBaseY + _windows.Count * layout.NewNoteCascadeStep,
            Width = layout.DefaultNoteWidth,
            Height = layout.DefaultNoteHeight,
            Title = StickyNote.CreateDefaultTitle(now, UsesEnglishLanguage()),
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (template != null)
        {
            note.ColorKey      = template.ColorKey;
            note.Icon          = template.Icon;
            note.FontFamily    = template.FontFamily;
            note.FontSize      = template.FontSize;
            note.TitleFontSize = template.TitleFontSize;
            note.OpacityPercent = template.OpacityPercent;
        }

        OpenNoteWindow(note);
        SaveAll();
    }

    private void OpenNoteWindow(StickyNote note)
    {
        var vm  = new StickyNoteViewModel(note, _settings);
        var win = new StickyNoteWindow(vm, _storage);
        _windows.Add(win);
        win.Show();
    }

    public void RemoveNote(string id)
    {
        _windows.RemoveAll(w => w.ViewModel.Model.Id == id);
        _storage.DeleteNote(id);   // 削除はここだけで行う
        SaveAll();
    }

    public void SaveAll()
    {
        var notes = _windows.Select(w => w.ViewModel.Model).ToList();
        _storage.Save(notes);
    }

    /// 保留中の保存をすべて確定させてからディスクに書き出す
    public void FlushAndSave()
    {
        foreach (var win in _windows)
            win.FlushPendingSave();
        SaveAll();
    }

    private void ExitApp()
    {
        FlushAndSave();
        _trayIcon?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Mutex を持つ本来のインスタンスのときだけ保存する。
        // 二重起動をブロックされた側は _instanceMutex が null で、
        // 空の _windows で上書きしてしまわないようにする。
        if (_instanceMutex != null)
            FlushAndSave();

        _trayIcon?.Dispose();
        _ipcWindow?.Dispose();
        if (_instanceMutex != null)
        {
            try { _instanceMutex.ReleaseMutex(); } catch (ApplicationException) { /* 未所有 */ }
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
        base.OnExit(e);
    }
}
