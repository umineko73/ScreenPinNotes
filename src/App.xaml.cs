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

using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes;

public partial class App : System.Windows.Application
{
    public new static App Current => (App)System.Windows.Application.Current;

    private StorageService _storage = new();
    private readonly List<StickyNoteWindow> _windows = [];
    private AppSettings _settings = new();
    private NotifyIcon? _trayIcon;
    private NoteManagerWindow? _noteManagerWindow;
    private readonly DispatcherTimer _reminderTimer = new();
    private readonly ReminderDelivery _reminderDelivery = new();
    private bool _shuttingDown;
    private GlobalNoteHotkey? _newNoteHotkey;
    public string NewNoteHotkeyError { get; private set; } = "";

    public bool TrySetNewNoteHotkey(string gesture)
    {
        _newNoteHotkey ??= new GlobalNoteHotkey(() =>
        {
            if (!_shuttingDown && _openReminderDialogs == 0 && _settingsWindow?.IsCapturingHotkey != true && !_windows.Any(w => !w.IsEnabled))
                AddNewNote();
        });
        if (!_newNoteHotkey.TrySet(gesture))
        {
            NewNoteHotkeyError = LocalizationService.T("HotkeyUnavailable");
            return false;
        }
        NewNoteHotkeyError = "";
        _settings.NewNoteHotkey = _newNoteHotkey.Gesture;
        _storage.SaveSettings(_settings);
        return true;
    }

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

        var instanceKey = ResolveInstanceKey();
        var mutexName = "ScreenPinNotes.SingleInstance." + instanceKey;
        _showAllMessage = RegisterWindowMessage("ScreenPinNotes.ShowAll." + instanceKey);

        _instanceMutex = new Mutex(initiallyOwned: true, mutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // 既に起動済み。既存インスタンスに全表示を依頼して自分は終了する
            PostMessage(HWND_BROADCAST, _showAllMessage, 0, 0);
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        // ログオフ・シャットダウン時にデバウンス待ちの変更を取りこぼさない。
        // ここで閉じられるウィンドウは「ユーザーが個別に閉じた」わけではないので、
        // 非表示への読み替え（HideNoteOnWindowClose）はしないよう先に印を付ける。
        SessionEnding += (_, _) =>
        {
            _shuttingDown = true;
            FlushAndSave();
        };

        _settings = _storage.LoadSettings();
        // スタートアップ登録は既存のレジストリが実体なので、起動時にJSONへ反映する。
        _settings.StartWithWindows = StartupService.IsRegistered;
        EnsureStorageRootSelected();
        _storage = _storage.WithStorageRoot(_settings.StorageRoot);
        _storage.SaveSettings(_settings);

        InitIpcWindow();
        InitTrayIcon();
        if (!TrySetNewNoteHotkey(_settings.NewNoteHotkey))
            _trayIcon?.ShowBalloonTip(5000, "ScreenPinNotes", NewNoteHotkeyError, ToolTipIcon.Warning);

        var notes = _storage.Load();
        if (notes.Count == 0)
        {
            notes = SampleNoteFactory.CreateInitialNotes(_settings, _storage);
            _storage.Save(notes);
        }

        foreach (var note in notes)
            OpenNoteWindow(note);
        ForgetLastActiveNote();

        RefreshTrayMenu();
        StartReminderTimer();
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
                "ScreenPinNotes",
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
        var parameters = new System.Windows.Interop.HwndSourceParameters("ScreenPinNotesIpc")
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
            Text = "ScreenPinNotes",
            Visible = true,
        };
        _trayIcon.ContextMenuStrip = BuildTrayMenu();
        _trayIcon.BalloonTipClicked += (_, _) => Dispatcher.BeginInvoke(ShowNoteManager);

        // 左クリック時の動作は設定で選べる（既定は全表示トグル）。
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                HandleTrayLeftClick();
        };
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(LocalizationService.T("TrayShowAll"), null, (_, _) => ShowAllNotes());
        menu.Items.Add(LocalizationService.T("TrayHideAll"), null, (_, _) => HideAllNotes());
        menu.Items.Add(BuildHiddenNotesMenu());
        menu.Items.Add("-");
        menu.Items.Add(LocalizationService.T("TrayNewNote"), null, (_, _) => AddNewNote());
        menu.Items.Add(LocalizationService.T("TrayOpenExternalNote"), null, (_, _) => AddExternalFileNoteFromDialog());
        menu.Items.Add(LocalizationService.T("TrayNoteManager"), null, (_, _) => ShowNoteManager());
        menu.Items.Add("-");
        menu.Items.Add(LocalizationService.T("TraySettings"), null, (_, _) => ShowSettingsWindow());
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

    private async void ExportNotesFromTray()
    {
        using var dialog = new SaveFileDialog
        {
            Title = LocalizationService.T("ExportNotesTitle"),
            Filter = LocalizationService.T("NotesZipFilter"),
            FileName = $"ScreenPinNotes-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
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

    /// <summary>
    /// スタートアップ登録。レジストリの登録解除まで伴うので、
    /// 設定を書き換えるだけの他の項目とは分けて1箇所に置く。
    /// </summary>
    public void SetStartWithWindows(bool enabled)
    {
        if (enabled) StartupService.Register(); else StartupService.Unregister();
        _settings.StartWithWindows = enabled;
        ApplySettingsChange();
    }

    public bool IsStartupRegistered => StartupService.IsRegistered;

    public void ApplySettingsFromSettingsWindow() => ApplySettingsChange();

    public void SelectNotesRootFromSettings() => SelectNotesRootFromTray();

    public void ExportNotesFromSettings() => ExportNotesFromTray();

    public void ImportNotesFromSettings() => ImportNotesFromTray();

    // ─── 設定画面 ────────────────────────────────────────────────

    private Views.SettingsWindow? _settingsWindow;

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new Views.SettingsWindow(_settings, this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    /// <summary>保存先を変えたら、開いている設定画面の表示も追従させる。</summary>
    private void RefreshSettingsWindowNotesRoot() => _settingsWindow?.RefreshNotesRoot();

    private void ApplySettingsChange()
    {
        _settings.Normalize();
        _storage.SaveSettings(_settings);
        RefreshTrayMenu();
        RefreshSettingsWindowNotesRoot();
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

    /// <summary>タスクトレイアイコンの左クリック。動作は設定で選べる。</summary>
    private void HandleTrayLeftClick()
    {
        if (_settings.TrayClickAction == "NewNote")
            AddNewNote();
        else
            ToggleAllNotes();
    }

    private bool _layerOrderQueued;
    private int _openReminderDialogs;

    internal void ReminderDialogOpened() => _openReminderDialogs++;

    internal void ReminderDialogClosed()
    {
        _openReminderDialogs--;
        QueueLayerOrder();
    }

    private void QueueLayerOrder()
    {
        if (_layerOrderQueued || _shuttingDown) return;
        _layerOrderQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _layerOrderQueued = false;
            if (_shuttingDown) return;
            ApplyLayerOrder();
            _noteManagerWindow?.RefreshNotes();
        });
    }

    // 最後に触られた付箋。重なり順そのものは設定として持っているので書き換え
    // ないが、これを覚えておかないと、クリックで前に出た付箋をこの直後の
    // 並べ直しで奥へ送り返してしまい、一瞬表に出てすぐ裏へ戻る。
    private StickyNoteWindow? _lastActiveWindow;

    /// <summary>
    /// 触られた付箋を覚えて、前に出し直す。活性化だけを見ていると、
    /// すでに入力先になっている付箋（起動直後の最後の1枚など）を
    /// クリックしても活性化が起きず、奥に沈んだままになる。
    /// </summary>
    internal void NoteTouched(StickyNoteWindow window)
    {
        _lastActiveWindow = window;
        QueueLayerOrder();
    }

    /// <summary>
    /// まとめて出し直すときは、直前に触った付箋の記憶を捨てる。Show() でも
    /// 活性化は起きるので、残しておくと最後に出した付箋が、設定した重なり順を
    /// 追い越して前に出てしまう。
    /// </summary>
    public void ForgetLastActiveNote() => _lastActiveWindow = null;

    public void ApplyLayerOrder()
    {
        // A modal reminder editor must stay above pinned notes. Apply deferred
        // layer changes when it closes instead of raising notes over its controls.
        if (_openReminderDialogs > 0) return;
        var byId = _windows.ToDictionary(w => w.ViewModel.Model.Id);
        foreach (var note in NoteLayers.Ordered(_windows.Select(w => w.ViewModel.Model)).AsEnumerable().Reverse())
        {
            var window = byId[note.Id];
            if (window.IsVisible) window.ChangeZOrder(true);
        }
        foreach (var window in _windows.Where(w => w.IsVisible && w.IsTemporarilyRaised))
            window.ChangeZOrder(true);
        // 常に手前・通常の2つの帯をまたぐことはない。Windows 側が常に手前の
        // ウィンドウを必ず上に置くので、通常の付箋がここで上へ抜けることはない。
        if (_lastActiveWindow is { } raised && raised.IsVisible && _windows.Contains(raised))
            raised.ChangeZOrder(true);
        foreach (var window in _windows.Where(w => w.IsVisible))
            window.RaisePickerPopups();
    }

    public void MoveNoteLayers(ISet<string> ids, LayerMove move)
    {
        NoteLayers.Move(_windows.Select(w => w.ViewModel.Model), ids, move);
        ApplyLayerOrder();
        SaveAll();
        _noteManagerWindow?.RefreshNotes();
    }

    public void ShowAllNotes()
    {
        foreach (var win in _windows)
        {
            if (!win.ViewModel.Model.IsHidden)
                win.Show();
        }
        ForgetLastActiveNote();
        ApplyLayerOrder();
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
        var layout = _settings.Layout;
        var note = NewNoteFactory.Create(
            _settings,
            template,
            x ?? layout.NewNoteBaseX + _windows.Count * layout.NewNoteCascadeStep,
            y ?? layout.NewNoteBaseY + _windows.Count * layout.NewNoteCascadeStep,
            DateTime.Now);

        note.LayerOrder = _windows.Select(w => w.ViewModel.Model.LayerOrder).DefaultIfEmpty(0).Min() - 1;
        var window = OpenNoteWindow(note);
        window.StartEditingNewNote();
        SaveAll();
    }

    private void AddExternalFileNoteFromDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = LocalizationService.T("TrayOpenExternalNote"),
            Filter = LocalizationService.T("ExternalNoteFileFilter"),
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog() == DialogResult.OK)
            AddExternalFileNote(dialog.FileName);
    }

    private const long MaxExternalNoteFileSizeBytes = 20 * 1024 * 1024; // 20 MB

    public void AddExternalFileNote(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);

        // ファイル選択ダイアログは「すべてのファイル」も許容するため、
        // 巨大・バイナリファイルを選んでUIスレッドが固まるのを防ぐ。
        var info = new FileInfo(fullPath);
        if (info.Exists && info.Length > MaxExternalNoteFileSizeBytes)
        {
            System.Windows.MessageBox.Show(
                LocalizationService.T("ExternalNoteFileTooLargeMessage"),
                LocalizationService.T("ExternalNoteFileTooLargeTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var now = DateTime.Now;
        var layout = _settings.Layout;
        var note = new StickyNote
        {
            X = layout.NewNoteBaseX + _windows.Count * layout.NewNoteCascadeStep,
            Y = layout.NewNoteBaseY + _windows.Count * layout.NewNoteCascadeStep,
            Width = layout.DefaultNoteWidth,
            Height = layout.DefaultNoteHeight,
            Title = Path.GetFileName(fullPath),
            Icon = "🔗",
            IsReadOnly = true,
            ExternalContentPath = fullPath,
            CreatedAt = now,
            UpdatedAt = now,
        };
        note.Content = StorageService.ReadExternalContent(note);

        note.LayerOrder = _windows.Select(w => w.ViewModel.Model.LayerOrder).DefaultIfEmpty(0).Min() - 1;
        OpenNoteWindow(note);
        SaveAll();
        RefreshTrayMenu();
        _noteManagerWindow?.RefreshNotes();
    }

    private StickyNoteWindow OpenNoteWindow(StickyNote note)
    {
        var vm  = new StickyNoteViewModel(note, _settings);
        var win = new StickyNoteWindow(vm, _storage);
        _windows.Add(win);
        win.Activated += (_, _) => QueueLayerOrder();
        win.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StickyNoteViewModel.IsTopmost)) QueueLayerOrder();
        };
        QueueLayerOrder();
        if (!note.IsHidden)
            win.Show();
        return win;
    }

    /// <summary>
    /// ウィンドウ自体が閉じられようとしているとき（タスクバーの×、Alt+F4 など）に
    /// 付箋側から呼ぶ。閉じたウィンドウは Show() できないので、そのまま閉じさせると
    /// 一覧やトレイには残ったまま二度と表示できない抜け殻になり、「付箋をすべて表示」で
    /// InvalidOperationException になる。付箋は消さずに非表示へ読み替え、
    /// トレイの「非表示の付箋」や付箋一覧から戻せる状態にする。
    /// 閉じるのを取りやめた場合だけ true を返す。
    /// </summary>
    public bool HideNoteOnWindowClose(StickyNoteWindow win)
    {
        // 終了処理と、アプリ側から意図的に閉じるとき（削除・保存先の切り替え）は
        // すでに _windows から外れているので、そのまま閉じさせる。
        if (_shuttingDown || !_windows.Contains(win))
            return false;

        HideNote(win.ViewModel.Model.Id);
        return true;
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
        _noteManagerWindow?.RefreshNotes();
    }

    private void ShowHiddenNote(string id)
        => ShowNote(id);

    public void ShowNote(string id)
    {
        var win = _windows.FirstOrDefault(w => w.ViewModel.Model.Id == id);
        if (win == null)
            return;

        win.ViewModel.Model.IsHidden = false;
        win.Show();
        win.Activate();
        ApplyLayerOrder();
        SaveAll();
        RefreshTrayMenu();
        _noteManagerWindow?.RefreshNotes();
    }

    public void SetReminder(string id, DateTime? nextAt, ReminderSettings? options = null)
    {
        var win = _windows.FirstOrDefault(w => w.ViewModel.Model.Id == id);
        if (win == null)
            return;

        win.ViewModel.SetReminder(nextAt);
        if (nextAt != null && options != null)
        {
            win.ViewModel.Model.Reminder = options;
            win.ViewModel.RefreshReminder();
        }
        SaveAll();
        _noteManagerWindow?.RefreshNotes();
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
        _noteManagerWindow?.RefreshNotes();
    }

    private void ShowNoteManager()
    {
        if (_noteManagerWindow != null)
        {
            _noteManagerWindow.RefreshNotes();
            _noteManagerWindow.Activate();
            return;
        }

        _noteManagerWindow = new NoteManagerWindow();
        _noteManagerWindow.Closed += (_, _) => _noteManagerWindow = null;
        _noteManagerWindow.Show();
        _noteManagerWindow.Activate();
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
        ForgetLastActiveNote();
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
        var window = _windows.FirstOrDefault(w => w.ViewModel.Model.Id == id);
        var note = window?.ViewModel.Model;
        if (note?.IsReadOnly == true && !note.IsExternalContent)
            return false;

        _storage.DeleteNote(id);   // 削除はここだけで行う
        window?.DisableSaving();
        _windows.RemoveAll(w => w.ViewModel.Model.Id == id);
        SaveAll();
        RefreshTrayMenu();
        _noteManagerWindow?.RefreshNotes();
        return true;
    }

    public bool RemoveNoteFromManager(string id)
    {
        var win = _windows.FirstOrDefault(w => w.ViewModel.Model.Id == id);
        if (win == null)
            return false;
        if (win.ViewModel.Model.IsReadOnly && !win.ViewModel.Model.IsExternalContent)
            return false;

        _storage.DeleteNote(id);
        win.DisableSaving();
        _windows.Remove(win);
        win.Close();
        SaveAll();
        RefreshTrayMenu();
        _noteManagerWindow?.RefreshNotes();
        return true;
    }

    public void RememberNoteSearch(string query)
    {
        query = query.Trim();
        if (query.Length == 0 || _settings.SearchHistory.FirstOrDefault() == query) return;
        _settings.SearchHistory.RemoveAll(item => item == query);
        _settings.SearchHistory.Insert(0, query);
        _settings.SearchHistory = _settings.SearchHistory.Take(30).ToList();
        _storage.SaveSettings(_settings);
    }

    public void SaveAll()
    {
        foreach (var window in _windows)
            window.SaveNote();
    }

    private void StartReminderTimer()
    {
        _reminderTimer.Interval = TimeSpan.FromSeconds(20);
        _reminderTimer.Tick += (_, _) => CheckDueReminders();
        _reminderTimer.Start();
        Dispatcher.BeginInvoke(CheckDueReminders);
    }

    private void CheckDueReminders() => DeliverDueReminders();

    private void DeliverDueReminders()
    {
        var now = DateTime.Now;
        var due = _windows.Where(w => _reminderDelivery.IsDue(w.ViewModel.Model, now)).ToList();
        var notifications = due.Where(w => w.ViewModel.Model.Reminder!.WindowsNotification).ToList();
        if (notifications.Count > 0 && _trayIcon != null)
        {
            // One Windows banner for simultaneous reminders, so later notices do not replace earlier ones.
            var message = ReminderDelivery.FormatNotification(notifications.Select(w =>
                (w.ViewModel.Model.Reminder!.NextAt, w.ViewModel.DisplayTitle)));
            _trayIcon.ShowBalloonTip(10000, "ScreenPinNotes", message, ToolTipIcon.Info);
        }
        foreach (var win in due)
        {
            var note = win.ViewModel.Model;
            if (note.Reminder?.NextAt is not DateTime nextAt)
                continue;
            if (!_reminderDelivery.IsDue(note, now))
                continue;

            TriggerReminder(win, nextAt);
        }
    }

    private void TriggerReminder(StickyNoteWindow win, DateTime dueAt)
    {
        var note = win.ViewModel.Model;
        _reminderDelivery.Deliver(note, dueAt, DateTime.Now, reminder =>
        {
            win.ViewModel.RefreshReminder();
            SaveAll();
            if (reminder.FlashNote)
            {
                ShowNote(note.Id);
                win.FlashForReminder();
            }
            // null（この機能追加より前に保存されたリマインダー）は従来どおり
            // アラートを出す側として扱う。明示的に false のときだけスキップする。
            if (reminder.ShowAlert == false)
            {
                _noteManagerWindow?.RefreshNotes();
                return;
            }
            ShowNote(note.Id);
            System.Media.SystemSounds.Exclamation.Play();

            var result = ReminderAlertWindow.ShowFor(win, win.ViewModel.DisplayTitle, dueAt);
            if (result.Snoozed && result.SnoozeDelay is TimeSpan delay)
            {
                reminder.NextAt = DateTime.Now.Add(delay);
                win.ViewModel.RefreshReminder();
            }
            SaveAll();
            _noteManagerWindow?.RefreshNotes();
        });
    }

    /// 保留中の保存をすべて確定させてからディスクに書き出す
    public void FlushAndSave()
    {
        SaveAll();
    }

    private void ExitApp()
    {
        _shuttingDown = true;
        FlushAndSave();
        _trayIcon?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;
        _reminderTimer.Stop();

        // Mutex を持つ本来のインスタンスのときだけ保存する。
        // 二重起動をブロックされた側は _instanceMutex が null で、
        // 空の _windows で上書きしてしまわないようにする。
        if (_instanceMutex != null)
            FlushAndSave();

        _trayIcon?.Dispose();
        _ipcWindow?.Dispose();
        _newNoteHotkey?.Dispose();
        if (_instanceMutex != null)
        {
            try { _instanceMutex.ReleaseMutex(); } catch (ApplicationException) { /* 未所有 */ }
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
        base.OnExit(e);
    }
}
