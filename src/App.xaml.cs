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
using System.IO;
using System.Threading.Tasks;
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

    private StorageService _storage = new();
    private readonly List<StickyNoteWindow> _windows = [];
    private AppSettings _settings = new();
    private NotifyIcon? _trayIcon;

    public IReadOnlyList<StickyNoteWindow> NoteWindows => _windows;
    public AppSettings Settings => _settings;

    // ─── 二重起動防止 ────────────────────────────────────────────
    // 複数インスタンスが同じ notes フォルダを読み書きすると、
    // 一方の保存が他方のノートを壊すため 1 プロセスに制限する。

    // キーは実際に使われる notes フォルダ（StorageRoot、または移行前の旧
    // NotesRoot）ごとに分ける。DataRoot だけをキーにしていた頃は、
    // タスクトレイの「保存フォルダを選択...」で notes だけを DataRoot と無関係な
    // 場所へ移動できるようになったことで、DataRoot（＝settings.jsonの置き場所）が
    // 異なる2インスタンスが同じ notes フォルダを指せてしまい、二重起動防止が
    // 効かなくなる穴があった。settings.json をアプリとして読み込む前に軽く覗き見て
    // 実際の notes フォルダを特定し、それをキーにする（未設定/読み込み不可なら
    // DataRoot 配下の既定 notes フォルダにフォールバック）。
    private string _instanceKey = "";
    private string _mutexName = "";
    private int _showAllMessage;
    private const int HWND_BROADCAST = 0xFFFF;

    private static string ResolveInstanceKey()
    {
        var notesRoot = StorageService.PeekConfiguredNotesRoot() ?? StorageService.DefaultNotesRoot;
        return Path.GetFullPath(notesRoot).ToLowerInvariant().Replace('\\', '_').Replace(':', '_');
    }

    private Mutex? _instanceMutex;
    private System.Windows.Interop.HwndSource? _ipcWindow;

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string message);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ConfigureExceptionHandling();

        _instanceKey = ResolveInstanceKey();
        _mutexName = "ScreenStickyNotes.SingleInstance." + _instanceKey;
        _showAllMessage = RegisterWindowMessage("ScreenStickyNotes.ShowAll." + _instanceKey);

        _instanceMutex = new Mutex(initiallyOwned: true, _mutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // 既に起動済み。既存インスタンスに全表示を依頼して自分は終了する
            PostMessage(HWND_BROADCAST, _showAllMessage, 0, 0);
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
        EnsureStorageRootSelected();
        _storage = _storage.WithStorageRoot(_settings.StorageRoot);
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

        RefreshTrayMenu();
    }

    private void EnsureStorageRootSelected()
    {
        if (!string.IsNullOrWhiteSpace(_settings.StorageRoot))
            return;

        if (!string.IsNullOrWhiteSpace(_settings.NotesRoot))
        {
            _settings.StorageRoot = StorageService.GetStorageRootFromLegacyNotesRoot(_settings.NotesRoot);
            _settings.NotesRoot = "";
            _settings.Normalize();
            return;
        }

        _settings.StorageRoot = StorageService.DefaultStorageRoot;
        _settings.Normalize();
    }

    private static string? ShowStorageRootDialog(string? selectedPath)
    {
        var initialPath = string.IsNullOrWhiteSpace(selectedPath)
            ? StorageService.DefaultStorageRoot
            : selectedPath;
        Directory.CreateDirectory(initialPath);

        using var dialog = new FolderBrowserDialog
        {
            Description = LocalizationService.T("SelectNotesRootDescription"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = initialPath,
        };

        var result = dialog.ShowDialog();
        return result == DialogResult.OK ? dialog.SelectedPath : null;
    }

    private void ConfigureExceptionHandling()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            ErrorReporter.ReportNonFatal("Unhandled UI exception", e.Exception);
            TryShowErrorNotice();
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ErrorReporter.ReportNonFatal("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
                ErrorReporter.ReportNonFatal("Unhandled application exception", exception);
        };
    }

    private void TryShowErrorNotice()
    {
        if (_trayIcon == null)
            return;

        try
        {
            _trayIcon.ShowBalloonTip(
                3000,
                "ScreenStickyNotes",
                $"処理中にエラーが発生しました。詳細はログを確認してください。\n{ErrorReporter.LogPath}",
                ToolTipIcon.Warning);
        }
        catch
        {
            // Notification failure should not affect note editing.
        }
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
            if (msg == _showAllMessage)
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
        menu.Items.Add(BuildHiddenNotesMenu());
        menu.Items.Add("-");
        menu.Items.Add(LocalizationService.T("TrayNewNote"), null, (_, _) => AddNewNote());
        menu.Items.Add("-");
        menu.Items.Add(BuildSettingsMenu(startupItem));
        menu.Items.Add("-");
        menu.Items.Add(LocalizationService.T("TrayAbout"), null, (_, _) => ShowAboutWindow());
        menu.Items.Add(LocalizationService.T("TrayExit"), null, (_, _) => ExitApp());
        return menu;
    }

    private ToolStripMenuItem BuildHiddenNotesMenu()
    {
        var hiddenNotesItem = new ToolStripMenuItem(LocalizationService.T("TrayHiddenNotes"));
        var hiddenWindows = _windows
            .Where(w => w.ViewModel.Model.IsHidden)
            .OrderBy(w => w.ViewModel.Model.CreatedAt)
            .ToList();

        if (hiddenWindows.Count == 0)
        {
            hiddenNotesItem.Enabled = false;
            hiddenNotesItem.DropDownItems.Add(LocalizationService.T("TrayNoHiddenNotes"));
            return hiddenNotesItem;
        }

        hiddenNotesItem.DropDownItems.Add(LocalizationService.T("TrayShowAllHiddenNotes"), null, (_, _) => ShowAllHiddenNotes());
        hiddenNotesItem.DropDownItems.Add("-");

        foreach (var win in hiddenWindows)
        {
            var noteId = win.ViewModel.Model.Id;
            hiddenNotesItem.DropDownItems.Add(win.ViewModel.DisplayTitle, null, (_, _) => ShowHiddenNote(noteId));
        }

        return hiddenNotesItem;
    }

    private ToolStripMenuItem BuildSettingsMenu(ToolStripMenuItem startupItem)
    {
        var settingsItem = new ToolStripMenuItem(LocalizationService.T("TraySettings"));
        settingsItem.DropDownItems.Add(BuildSelectNotesRootItem());
        settingsItem.DropDownItems.Add(BuildExportNotesItem());
        settingsItem.DropDownItems.Add(BuildImportNotesItem());
        settingsItem.DropDownItems.Add("-");
        settingsItem.DropDownItems.Add(startupItem);
        settingsItem.DropDownItems.Add(BuildTitlePreviewTooltipItem());
        settingsItem.DropDownItems.Add(BuildFoldAnimationItem());
        settingsItem.DropDownItems.Add(BuildFoldButtonItem());
        settingsItem.DropDownItems.Add(BuildDarkModeItem());
        settingsItem.DropDownItems.Add(BuildLanguageMenu());
        return settingsItem;
    }

    private ToolStripMenuItem BuildSelectNotesRootItem()
    {
        var item = new ToolStripMenuItem(LocalizationService.T("TraySelectNotesRoot"));
        item.Click += (_, _) => SelectNotesRootFromTray();
        return item;
    }

    private ToolStripMenuItem BuildExportNotesItem()
    {
        var item = new ToolStripMenuItem(LocalizationService.T("TrayExportNotes"));
        item.Click += (_, _) => ExportNotesFromTray();
        return item;
    }

    private ToolStripMenuItem BuildImportNotesItem()
    {
        var item = new ToolStripMenuItem(LocalizationService.T("TrayImportNotes"));
        item.Click += (_, _) => ImportNotesFromTray();
        return item;
    }

    private async void ExportNotesFromTray()
    {
        using var dialog = new SaveFileDialog
        {
            Title = LocalizationService.T("ExportNotesTitle"),
            Filter = LocalizationService.T("NotesZipFilter"),
            FileName = $"ScreenStickyNotes-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            AddExtension = true,
            DefaultExt = "zip",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        FlushAndSave();
        SetNoteWindowsEnabled(false);
        try
        {
            await Task.Run(() => _storage.ExportNotesToZip(dialog.FileName));
            System.Windows.MessageBox.Show(
                LocalizationService.T("ExportNotesCompletedMessage"),
                LocalizationService.T("ExportNotesCompletedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Export notes", ex);
            System.Windows.MessageBox.Show(
                LocalizationService.T("ExportNotesFailedMessage"),
                LocalizationService.T("ExportNotesFailedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetNoteWindowsEnabled(true);
        }
    }

    private async void ImportNotesFromTray()
    {
        using var dialog = new OpenFileDialog
        {
            Title = LocalizationService.T("ImportNotesTitle"),
            Filter = LocalizationService.T("NotesZipFilter"),
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        var result = System.Windows.MessageBox.Show(
            LocalizationService.T("ImportNotesConfirmMessage"),
            LocalizationService.T("ImportNotesConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        FlushAndSave();
        SetNoteWindowsEnabled(false);
        try
        {
            var importResult = await Task.Run(() => _storage.ImportNotesFromZip(dialog.FileName));
            ReloadNoteWindowsFromStorage(showEmptyStorageMessage: false);
            RefreshTrayMenu();

            System.Windows.MessageBox.Show(
                string.Format(
                    LocalizationService.T("ImportNotesCompletedMessage"),
                    importResult.ImportedCount,
                    importResult.SkippedCount),
                LocalizationService.T("ImportNotesCompletedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Import notes", ex);
            System.Windows.MessageBox.Show(
                LocalizationService.T("ImportNotesFailedMessage"),
                LocalizationService.T("ImportNotesFailedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            SetNoteWindowsEnabled(true);
        }
    }

    private void SetNoteWindowsEnabled(bool enabled)
    {
        foreach (var win in _windows)
            win.IsEnabled = enabled;
    }

    private async void SelectNotesRootFromTray()
    {
        var selectedPath = ShowStorageRootDialog(StorageService.GetSelectableFolderFromStorageRoot(_settings.StorageRoot));
        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        var storageRoot = StorageService.GetStorageRootFromSelectedFolder(selectedPath);
        if (string.Equals(storageRoot, Path.GetFullPath(_settings.StorageRoot), StringComparison.OrdinalIgnoreCase))
            return;

        var targetNotesRoot = StorageService.GetNotesRootFromStorageRoot(storageRoot);
        var targetNotesMissing = !Directory.Exists(targetNotesRoot);

        FlushAndSave();
        var (canProceed, movedNotes) = await TryMoveNotesToNewStorageRootAsync(_storage.NotesRoot, storageRoot);
        if (!canProceed)
            return;

        _settings.StorageRoot = storageRoot;
        _settings.NotesRoot = "";
        _settings.Normalize();
        _storage.SaveSettings(_settings);
        _storage = _storage.WithStorageRoot(_settings.StorageRoot);

        if (targetNotesMissing && !movedNotes)
            ShowEmptyStorageInitializationMessage();

        ReloadNoteWindowsFromStorage(showEmptyStorageMessage: !targetNotesMissing || movedNotes);
        RefreshTrayMenu();

        System.Windows.MessageBox.Show(
            LocalizationService.T("SelectNotesRootChangedMessage"),
            LocalizationService.T("SelectNotesRootChangedTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // 大きな notes フォルダの移動はディスク I/O が伴うため、UI スレッドを
    // ブロックしないよう実際のコピー／移動だけバックグラウンドスレッドで行う。
    // 確認・失敗ダイアログは呼び出し元と同じスレッド（UIスレッド）で表示される。
    private async Task<(bool canProceed, bool moved)> TryMoveNotesToNewStorageRootAsync(string sourceNotesRoot, string targetStorageRoot)
    {
        var source = Path.GetFullPath(sourceNotesRoot);
        var target = StorageService.GetNotesRootFromStorageRoot(targetStorageRoot);

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            return (true, false);
        if (!Directory.Exists(source) || !Directory.EnumerateFileSystemEntries(source).Any())
            return (true, false);
        if (Directory.Exists(target))
            return (true, false);

        var result = System.Windows.MessageBox.Show(
            LocalizationService.T("MoveNotesConfirmMessage"),
            LocalizationService.T("MoveNotesConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return (true, false);

        try
        {
            await Task.Run(() => MoveDirectory(source, target));
            return (true, true);
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Move notes folder", ex);
            System.Windows.MessageBox.Show(
                LocalizationService.T("MoveNotesFailedMessage"),
                LocalizationService.T("MoveNotesFailedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return (false, false);
        }
    }

    private static void MoveDirectory(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(source, target);
            return;
        }

        // 別ドライブへの移動はファイルコピーになるため、target へ直接コピーすると
        // 途中で失敗したときに不完全なフォルダが target に残ってしまい、
        // 再試行時に「target が存在する＝移行済み」と誤認してしまう
        // （呼び出し元の Directory.Exists(target) チェック）。
        // 一時フォルダへ完全にコピーできてから target へリネームすることで、
        // 失敗時は target が存在しない状態を保ち、安全に再試行できるようにする。
        var staging = target + ".migrating-" + Guid.NewGuid().ToString("N");
        try
        {
            StorageService.CopyDirectory(source, staging);
            Directory.Move(staging, target);
        }
        catch
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            throw;
        }

        Directory.Delete(source, recursive: true);
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
        {
            if (!win.ViewModel.Model.IsHidden)
                win.Show();
        }
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
        if (!note.IsHidden)
            win.Show();
    }

    public void HideNote(string id)
    {
        var win = _windows.FirstOrDefault(w => w.ViewModel.Model.Id == id);
        if (win == null)
            return;

        win.ViewModel.Model.IsHidden = true;
        win.Hide();
        SaveAll();
        RefreshTrayMenu();
    }

    private void ShowHiddenNote(string id)
    {
        var win = _windows.FirstOrDefault(w => w.ViewModel.Model.Id == id);
        if (win == null)
            return;

        win.ViewModel.Model.IsHidden = false;
        win.Show();
        win.Activate();
        SaveAll();
        RefreshTrayMenu();
    }

    private void ShowAllHiddenNotes()
    {
        foreach (var win in _windows.Where(w => w.ViewModel.Model.IsHidden))
        {
            win.ViewModel.Model.IsHidden = false;
            win.Show();
        }

        SaveAll();
        RefreshTrayMenu();
    }

    private void ReloadNoteWindowsFromStorage(bool showEmptyStorageMessage = true)
    {
        var oldWindows = _windows.ToList();
        _windows.Clear();
        foreach (var win in oldWindows)
            win.Close();

        var notes = LoadOrCreateInitialNotes(showEmptyStorageMessage);

        foreach (var note in notes)
            OpenNoteWindow(note);
    }

    private List<StickyNote> LoadOrCreateInitialNotes(bool showEmptyStorageMessage = true)
    {
        var notes = _storage.Load();
        if (notes.Count > 0)
            return notes;

        if (showEmptyStorageMessage && !IsDefaultStorageRoot())
            ShowEmptyStorageInitializationMessage();

        notes = SampleNoteFactory.CreateInitialNotes(_settings, _storage);
        _storage.Save(notes);
        return notes;
    }

    private static void ShowEmptyStorageInitializationMessage()
        => System.Windows.MessageBox.Show(
            LocalizationService.T("InitializeEmptyStorageMessage"),
            LocalizationService.T("InitializeEmptyStorageTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private bool IsDefaultStorageRoot()
        => string.Equals(
            Path.GetFullPath(_settings.StorageRoot),
            Path.GetFullPath(StorageService.DefaultStorageRoot),
            StringComparison.OrdinalIgnoreCase);

    public bool RemoveNote(string id)
    {
        var note = _windows.FirstOrDefault(w => w.ViewModel.Model.Id == id)?.ViewModel.Model;
        if (note?.IsReadOnly == true)
            return false;

        _windows.RemoveAll(w => w.ViewModel.Model.Id == id);
        _storage.DeleteNote(id);   // 削除はここだけで行う
        SaveAll();
        return true;
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
