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

public partial class App : System.Windows.Application, INoteWindowHost
{
    public new static App Current => (App)System.Windows.Application.Current;

    private StorageService _storage = new();
    private readonly List<StickyNoteWindow> _windows = [];
    private AppSettings _settings = new();
    private NoteWorkspace? _workspace;
    private NoteWorkspace Workspace => _workspace ??= new(_windows, () => _storage, CreateNoteWindow);
    void INoteWindowHost.NoteTouched(StickyNoteWindow window) => NoteTouched(window);
    private NotifyIcon? _trayIcon;
    private NoteManagerWindow? _noteManagerWindow;
    private readonly DispatcherTimer _reminderTimer = new();
    private readonly ReminderDelivery _reminderDelivery = new();
    private bool _shuttingDown;
    private GlobalNoteHotkey? _newNoteHotkey;
    private GlobalNoteHotkey? _clipboardNoteHotkey;
    public string NewNoteHotkeyError { get; private set; } = "";
    public string ClipboardNoteHotkeyError { get; private set; } = "";

    // リマインダーの編集中・キーの指定中・一括処理中は割り込まない。
    private bool CanRunGlobalHotkey()
        => !_shuttingDown && _openReminderDialogs == 0 && _settingsWindow?.IsCapturingHotkey != true && !_windows.Any(w => !w.IsEnabled);

    public bool TrySetNewNoteHotkey(string gesture)
    {
        _newNoteHotkey ??= new GlobalNoteHotkey(() =>
        {
            if (CanRunGlobalHotkey())
                AddNewNote();
        });
        NewNoteHotkeyError = TrySetHotkey(_newNoteHotkey, gesture);
        if (NewNoteHotkeyError.Length > 0) return false;
        _settings.NewNoteHotkey = _newNoteHotkey.Gesture;
        _storage.SaveSettings(_settings);
        return true;
    }

    public bool TrySetClipboardNoteHotkey(string gesture)
    {
        _clipboardNoteHotkey ??= new GlobalNoteHotkey(() =>
        {
            if (CanRunGlobalHotkey())
                AddNewNoteFromClipboard();
        });
        ClipboardNoteHotkeyError = TrySetHotkey(_clipboardNoteHotkey, gesture);
        if (ClipboardNoteHotkeyError.Length > 0) return false;
        _settings.ClipboardNoteHotkey = _clipboardNoteHotkey.Gesture;
        _storage.SaveSettings(_settings);
        return true;
    }

    /// <summary>登録できなければ、その理由を返す。</summary>
    private static string TrySetHotkey(GlobalNoteHotkey hotkey, string gesture)
        => hotkey.TrySet(gesture) ? "" : LocalizationService.T("HotkeyUnavailable");

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

        var settingsLoad = _storage.LoadSettingsWithDiagnostics();
        _settings = settingsLoad.Value;
        DiagnosticTrace.Enabled = _settings.EnableDiagnosticTrace ||
            Environment.GetEnvironmentVariable(DiagnosticTrace.EnvVar) == "1";
        TraceEnvironment();
        // スタートアップ登録は既存のレジストリが実体なので、起動時にJSONへ反映する。
        _settings.StartWithWindows = StartupService.IsRegistered;
        EnsureStorageRootSelected();
        _storage = _storage.WithStorageRoot(_settings.StorageRoot);
        _storage.SaveSettings(_settings);

        InitIpcWindow();
        InitTrayIcon();
        InitScreenLayoutWatch();
        // 通知は1回にまとめる。続けて出すと、後の通知が前の通知を置き換えてしまう。
        var newNoteHotkeyApplied = TrySetNewNoteHotkey(_settings.NewNoteHotkey);
        var clipboardNoteHotkeyApplied = TrySetClipboardNoteHotkey(_settings.ClipboardNoteHotkey);
        if (!newNoteHotkeyApplied || !clipboardNoteHotkeyApplied)
            _trayIcon?.ShowBalloonTip(5000, "ScreenPinNotes", LocalizationService.T("HotkeyUnavailable"), ToolTipIcon.Warning);

        var notes = LoadOrCreateInitialNotes(showEmptyStorageMessage: false);
        if (settingsLoad.HasErrors)
            ShowTrayNotice(LocalizationService.T("StorageSettingsLoadFailed"));

        // 「起動時に表示しない」ときも付箋ごとの非表示は書き換えない。トレイの
        // 「すべて表示」や二重起動時の全表示で、普段どおりに出てくる。
        foreach (var note in notes)
            OpenNoteWindow(note, show: !_settings.StartHidden);
        ForgetLastActiveNote();

        RefreshTrayMenu();
        StartReminderTimer();
    }

    /// <summary>
    /// 再現する PC としない PC の違いを見比べるための環境情報。辺ドラッグの挙動は
    /// 「ドラッグ中にウィンドウの内容を表示」やリモート接続、クリック判定の設定で変わる。
    /// </summary>
    private void TraceEnvironment()
    {
        if (!DiagnosticTrace.Enabled) return;
        var version = typeof(App).Assembly.GetName().Version;
        var screens = string.Join("; ", Screen.AllScreens.Select(s =>
            $"{s.DeviceName} {s.Bounds.Width}x{s.Bounds.Height}@{s.Bounds.X},{s.Bounds.Y}{(s.Primary ? " primary" : "")}"));
        DiagnosticTrace.Write(
            $"START version={version} os={Environment.OSVersion.VersionString} " +
            $"dragFullWindows={SystemInformation.DragFullWindows} remoteSession={SystemInformation.TerminalServerSession} " +
            $"doubleClickTime={SystemInformation.DoubleClickTime} doubleClickSize={SystemInformation.DoubleClickSize} " +
            $"dragSize={SystemInformation.DragSize} buttonsSwapped={SystemInformation.MouseButtonsSwapped} " +
            $"monitors={MonitorLayout.Signature(MonitorLayout.Current())} screens=[{screens}]");
        DiagnosticTrace.Write(
            $"SETTINGS doubleClickToToggle={_settings.DoubleClickToToggleView} foldAnimation={_settings.EnableFoldAnimation} " +
            $"showInTaskbar={_settings.ShowNotesInTaskbar} showFoldButton={_settings.ShowFoldButton} " +
            $"resizeBorder={_settings.Layout.ResizeBorder} border={_settings.NoteBorderColor} corner={_settings.Layout.NoteCornerRadius}");
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
            Icon = TryLoadTrayIconResource(_settings.Theme == "Dark") ?? LoadTrayIcon(),
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
        menu.Items.Add(LocalizationService.T("TrayNewNoteFromClipboard"), null, (_, _) => AddNewNoteFromClipboard());
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
        // Prevent autosave/closing from restoring old content after replacement.
        foreach (var win in _windows) win.DisableSaving();
        try
        {
            var importResult = await Task.Run(() => _storage.ImportNotesFromZip(dialog.FileName,
                note => Dispatcher.Invoke(() =>
                {
                    var conflict = new ImportConflictDialog(note);
                    conflict.ShowDialog();
                    return conflict.Action;
                })));
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
            ReloadNoteWindowsFromStorage(showEmptyStorageMessage: false);
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
        if (_trayIcon != null)
        {
            var oldIcon = _trayIcon.Icon;
            _trayIcon.Icon = TryLoadTrayIconResource(_settings.Theme == "Dark") ?? LoadTrayIcon();
            oldIcon?.Dispose();
        }
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
    /// <summary>
    /// タスクトレイのアイコン。読めなくても起動は続ける。ここで例外を投げると
    /// OnStartup の途中で落ち、トレイどころか付箋の表示・作成まで巻き添えになる
    /// ―― 絵が既定に変わるだけの話なので、割に合わない。
    /// </summary>
    private static Icon LoadTrayIcon()
        => TryLoadTrayIconResource()
           ?? TryLoadExecutableIcon()
           ?? (Icon)SystemIcons.Application.Clone();

    /// <summary>
    /// exe の Win32 リソースではなく WPF リソースから読むのは、.ico に入っている
    /// 複数の絵からトレイの大きさに合ったものを選ぶため。
    /// </summary>
    public static Icon? TryLoadTrayIconResource(bool dark = false)
    {
        try
        {
            // 実行アセンブリ名で明示する。"pack://application:,,,/app.ico" だと
            // エントリアセンブリ側を見に行くので、テストなど別の exe から
            // 動かしたときに見つからない。
            var assembly = typeof(App).Assembly.GetName().Name;
            var file = dark ? "app-dark.ico" : "app.ico";
            var uri = new Uri($"pack://application:,,,/{assembly};component/{file}");
            if (System.Windows.Application.GetResourceStream(uri) is not { } resource)
                return null;

            using var stream = resource.Stream;
            using var icon = new Icon(stream, SystemInformation.SmallIconSize);
            return (Icon)icon.Clone();
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Load tray icon resource", ex);
            return null;
        }
    }

    /// <summary>
    /// 予備。ApplicationIcon で exe 自身にも同じ絵が埋まっているので、
    /// WPF リソースが欠けていても見た目は変わらずに済む。
    /// </summary>
    private static Icon? TryLoadExecutableIcon()
    {
        try
        {
            return Environment.ProcessPath is { } path ? Icon.ExtractAssociatedIcon(path) : null;
        }
        catch (Exception ex)
        {
            ErrorReporter.ReportNonFatal("Load executable icon", ex);
            return null;
        }
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

    // ─── モニタ構成の変更 ────────────────────────────────────────
    //
    // 解像度が変わったり、付箋が出ているモニタの接続が切れたりすると、
    // 保存された位置には出せなくなる。そのままだと画面の外に残り、
    // 「すべて表示」でも戻ってこない付箋になるので、映せるモニタへ寄せる。
    // 寄せた位置は保存しない（StickyNote.PositionLayout）。構成が戻れば元へ帰る。

    private readonly DispatcherTimer _screenLayoutTimer = new();

    private void InitScreenLayoutWatch()
    {
        // 構成変更の通知は連続して届き、タスクバーの位置（作業領域）は
        // 最後の通知より少し遅れて落ち着く。まとめて一度だけ見直す。
        _screenLayoutTimer.Interval = TimeSpan.FromMilliseconds(500);
        _screenLayoutTimer.Tick += (_, _) =>
        {
            _screenLayoutTimer.Stop();
            ReconcileNoteScreenPlacement();
        };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    // SystemEvents は専用スレッドで上がるので UI スレッドへ渡す。
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() =>
        {
            if (_shuttingDown) return;
            _screenLayoutTimer.Stop();
            _screenLayoutTimer.Start();
        });

    private void ReconcileNoteScreenPlacement()
    {
        foreach (var win in _windows)
            win.ReconcileScreenPlacement();
        ApplyLayerOrder();
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
    /// 触られた付箋だけを前に出す。全体の並べ直しは他アプリよりも
    /// 全付箋を前に出してしまうため、ここでは実行しない。活性化だけを見ていると、
    /// すでに入力先になっている付箋（起動直後の最後の1枚など）を
    /// クリックしても活性化が起きず、奥に沈んだままになる。
    /// </summary>
    internal void NoteTouched(StickyNoteWindow window)
    {
        _lastActiveWindow = window;
        if (_openReminderDialogs == 0 && window.IsVisible)
        {
            window.ChangeZOrder(true);
            window.RaisePickerPopups();
        }
    }

    /// <summary>
    /// まとめて出し直すときは、直前に触った付箋の記憶を捨てる。Show() でも
    /// 活性化は起きるので、残しておくと最後に出した付箋が、設定した重なり順を
    /// 追い越して前に出てしまう。
    /// </summary>
    public void ForgetLastActiveNote() => _lastActiveWindow = null;

    /// <summary>
    /// 付箋を設定した重なり順に並べ直す。
    /// </summary>
    /// <param name="bringToFront">
    /// true なら全付箋を他のアプリより前へ出す（「すべて表示」など、付箋を見せる操作）。
    /// false なら付箋どうしの順番だけを整える。付箋を1枚足しただけで、他のアプリの後ろに
    /// あった付箋まで前へ出てこないように。トレイのメニューから操作するとアプリが前面に
    /// なるので、一番上へ置き直す並べ方では全付箋が他のアプリを越えてしまう。
    /// </param>
    public void ApplyLayerOrder(bool bringToFront = false)
    {
        // A modal reminder editor must stay above pinned notes. Apply deferred
        // layer changes when it closes instead of raising notes over its controls.
        if (_openReminderDialogs > 0) return;
        var byId = _windows.ToDictionary(w => w.ViewModel.Model.Id);
        var ordered = NoteLayers.Ordered(_windows.Select(w => w.ViewModel.Model))
            .Select(note => byId[note.Id])
            .Where(w => w.IsVisible)
            .ToList();
        var lastActive = _lastActiveWindow is { } touched && touched.IsVisible && _windows.Contains(touched)
            ? touched
            : null;

        if (bringToFront)
        {
            for (var i = ordered.Count - 1; i >= 0; i--)
                ordered[i].ChangeZOrder(true);
            foreach (var window in ordered.Where(w => w.IsTemporarilyRaised))
                window.ChangeZOrder(true);
            // 常に手前・通常の2つの帯をまたぐことはない。Windows 側が常に手前の
            // ウィンドウを必ず上に置くので、通常の付箋がここで上へ抜けることはない。
            lastActive?.ChangeZOrder(true);
        }
        else
        {
            // 常に手前の帯と通常の帯を別々に並べる。帯ごとに、一番手前に来る付箋
            // （直前に触った付箋、無ければ設定の先頭）を基準にして、残りをその後ろへ順に置く。
            foreach (var band in ordered.GroupBy(w => w.Topmost))
            {
                // 手前から「直前に触った付箋 → 一時的に手前へ出している付箋 → 設定の順」。
                var chain = band.ToList();
                var temporarilyRaised = chain.Where(w => w.IsTemporarilyRaised).ToList();
                chain.RemoveAll(w => w.IsTemporarilyRaised);
                chain.InsertRange(0, temporarilyRaised);
                if (lastActive != null && chain.Remove(lastActive))
                    chain.Insert(0, lastActive);
                for (var i = 1; i < chain.Count; i++)
                    chain[i].PlaceBelow(chain[i - 1]);
            }
        }

        foreach (var window in ordered)
            window.RaisePickerPopups();
    }

    public void MoveNoteLayers(ISet<string> ids, LayerMove move)
    {
        NoteLayers.Move(_windows.Select(w => w.ViewModel.Model), ids, move);
        // An explicit ordering command takes precedence over the last clicked note.
        ForgetLastActiveNote();
        ApplyLayerOrder(bringToFront: true);
        SaveAll();
        _noteManagerWindow?.RefreshNotes();
    }

    public void ShowAllNotes()
    {
        // 活性化せずに出す。トレイのアイコンはタスクバーの中にあるので、そこから
        // 操作した直後に最後に出した付箋が活性化すると、タスクバーで選ばれたものと
        // 見分けがつかず、閉じた表示の付箋が開いてしまう。前へ出すのは下の並べ直しで行う。
        foreach (var win in _windows)
        {
            if (!win.ViewModel.Model.IsHidden)
                win.ShowWithoutActivation();
        }
        ForgetLastActiveNote();
        ApplyLayerOrder(bringToFront: true);
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
    /// <param name="scale">
    /// x/y がどの拡大率を基準にした論理ピクセルかを示す。カーソルの近くに出すときは
    /// 計算元のウィンドウの拡大率を渡す。null なら既定の段差位置と同じプライマリ基準。
    /// </param>
    public void AddNewNote(StickyNote? template = null, double? x = null, double? y = null,
        double? scale = null)
    {
        var window = OpenNoteWindow(CreateNewNote(template, x, y, scale));
        window.StartEditingNewNote();
        SaveAll();
    }

    /// <summary>
    /// クリップボードの内容（画像ファイル・文字・画像）を本文にした付箋を作る。
    /// 中身があるので編集には入らず、そのまま表示する。
    /// </summary>
    public void AddNewNoteFromClipboard()
    {
        var note = CreateNewNote(null, null, null, null);
        if (TryGetClipboardDataObject() is not { } data ||
            !StickyNoteWindow.TryBuildClipboardNoteContent(data, _storage, note.Id, out var content))
        {
            ShowTrayNotice(LocalizationService.T("ClipboardNoteEmpty"));
            return;
        }
        if (System.Text.Encoding.UTF8.GetByteCount(content) > _settings.MaxNoteContentBytes)
        {
            ShowTrayNotice(string.Format(LocalizationService.T("NoteContentTooLarge"),
                StickyNoteWindow.FormatByteSize(_settings.MaxNoteContentBytes)));
            return;
        }

        note.Content = content;
        var window = OpenNoteWindow(note);
        window.RevealNewNote();
        SaveAll();
        RefreshTrayMenu();
        _noteManagerWindow?.RefreshNotes();
    }

    /// <summary>
    /// 付箋を複製する。貼り付けた画像も付箋ごとの assets にあるので一緒に写す。
    /// 中身があるので編集には入らず、元の付箋から少しずらして前に出す。
    /// </summary>
    public StickyNoteWindow DuplicateNote(StickyNoteWindow source)
    {
        var original = source.ViewModel.Model;
        var copy = NoteDuplicator.Create(original, _settings.Layout.NewNoteCascadeStep,
            FrontLayerOrder(), DateTime.Now);
        if (!original.IsExternalContent)
        {
            try
            {
                var assets = _storage.GetNoteAssetsDirectoryPath(original.Id);
                if (Directory.Exists(assets))
                    StorageService.CopyDirectory(assets, _storage.GetNoteAssetsDirectoryPath(copy.Id));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 画像が写せなくても本文は複製できる。画像は元の付箋に残っている。
                ErrorReporter.ReportNonFatal("Copy note assets", ex);
            }
        }

        var window = OpenNoteWindow(copy);
        window.RevealNewNote();
        SaveAll();
        RefreshTrayMenu();
        _noteManagerWindow?.RefreshNotes();
        return window;
    }

    private int FrontLayerOrder()
        => _windows.Select(w => w.ViewModel.Model.LayerOrder).DefaultIfEmpty(0).Min() - 1;

    // 他のアプリがクリップボードを開いている瞬間は読めないので、少し待って数回試す。
    private static System.Windows.IDataObject? TryGetClipboardDataObject()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return System.Windows.Clipboard.GetDataObject();
            }
            catch (System.Runtime.InteropServices.ExternalException) when (attempt < 5)
            {
                Thread.Sleep(40);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
            {
                ErrorReporter.ReportNonFatal("Read clipboard for a new note", ex);
                return null;
            }
        }
    }

    private void ShowTrayNotice(string message)
        => _trayIcon?.ShowBalloonTip(3000, "ScreenPinNotes", message, ToolTipIcon.Info);

    private StickyNote CreateNewNote(StickyNote? template, double? x, double? y, double? scale)
    {
        var layout = _settings.Layout;
        var monitors = MonitorLayout.Current();
        var note = NewNoteFactory.Create(
            _settings,
            template,
            x ?? layout.NewNoteBaseX + _windows.Count * layout.NewNoteCascadeStep,
            y ?? layout.NewNoteBaseY + _windows.Count * layout.NewNoteCascadeStep,
            DateTime.Now);
        // 作った場所を物理ピクセルで復元できるようにしておく。基準を残さないと、
        // 拡大率の違うモニタに出した付箋が次回の起動で別のモニタへ移ってしまう。
        note.PositionScale = scale is > 0 ? scale.Value : MonitorLayout.PrimaryScale(monitors);
        note.PositionLayout = MonitorLayout.Signature(monitors);

        note.LayerOrder = FrontLayerOrder();
        return note;
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
        // .log は育ち続けるログの前提が強いので、既定で tail 表示にする。
        // tail は末尾しか読まないので、巨大ファイルでもサイズ上限に引っかからない。
        var isLogFile = string.Equals(Path.GetExtension(fullPath), ".log", StringComparison.OrdinalIgnoreCase);

        // ファイル選択ダイアログは「すべてのファイル」も許容するため、
        // 巨大・バイナリファイルを選んでUIスレッドが固まるのを防ぐ。
        var info = new FileInfo(fullPath);
        if (!isLogFile && info.Exists && info.Length > MaxExternalNoteFileSizeBytes)
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
            ExternalTailMode = isLogFile,
            CreatedAt = now,
            UpdatedAt = now,
        };
        note.Content = StorageService.ReadExternalContent(note, _settings.ExternalFile.TailLineCount);

        note.LayerOrder = _windows.Select(w => w.ViewModel.Model.LayerOrder).DefaultIfEmpty(0).Min() - 1;
        OpenNoteWindow(note);
        SaveAll();
        RefreshTrayMenu();
        _noteManagerWindow?.RefreshNotes();
    }

    /// <param name="show">false なら作るだけで表示しない（起動時に表示しない設定）。</param>
    private StickyNoteWindow OpenNoteWindow(StickyNote note, bool show = true)
    {
        var window = Workspace.Open(note, show);
        QueueLayerOrder();
        return window;
    }

    private StickyNoteWindow CreateNoteWindow(StickyNote note)
    {
        var vm = new StickyNoteViewModel(note, _settings);
        var window = new StickyNoteWindow(vm, _storage, this);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StickyNoteViewModel.IsTopmost)) QueueLayerOrder();
        };
        return window;
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
        if (!Workspace.Hide(id)) return;
        SaveAll();
        RefreshTrayMenu();
        _noteManagerWindow?.RefreshNotes();
    }

    private void ShowHiddenNote(string id)
        => ShowNote(id);

    public void ShowNote(string id)
    {
        if (!Workspace.Show(id)) return;
        ApplyLayerOrder(bringToFront: true);
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
        Workspace.CloseAll();

        var notes = LoadOrCreateInitialNotes(showEmptyStorageMessage);

        foreach (var note in notes)
            OpenNoteWindow(note);
        ForgetLastActiveNote();
    }

    private List<StickyNote> LoadOrCreateInitialNotes(bool showEmptyStorageMessage = true)
    {
        var loaded = _storage.LoadWithDiagnostics(_settings.ExternalFile.TailLineCount,
            refreshExternalContent: false);
        var notes = loaded.Value;
        if (loaded.HasErrors)
        {
            ShowTrayNotice(LocalizationService.T("StorageNotesLoadFailed"));
            return notes; // A failed load is not an empty, first-run storage folder.
        }
        if (notes.Count > 0) return notes;

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

    public bool RemoveNote(string id) => RemoveNoteCore(id, closeWindow: false);

    public bool RemoveNoteFromManager(string id) => RemoveNoteCore(id, closeWindow: true);

    private bool RemoveNoteCore(string id, bool closeWindow)
    {
        if (!Workspace.Remove(id, closeWindow)) return false;
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
        Workspace.SaveAll();
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
            var alertDuration = TimeSpan.FromSeconds(_settings.ReminderAlertSeconds);
            if (reminder.FlashNote)
            {
                RevealForReminder(win, _settings.BringReminderNoteToFront);
                win.FlashForReminder(alertDuration);
            }
            // 付箋を出してから鳴らす。表示の切り替えで止まる扱いにならないように。
            if (reminder.PlaysSound)
                ReminderSound.PlayForReminder(_settings.ReminderSound, alertDuration, win);
            // null（この機能追加より前に保存されたリマインダー）は従来どおり
            // アラートを出す側として扱う。明示的に false のときだけスキップする。
            if (reminder.ShowAlert == false)
            {
                _noteManagerWindow?.RefreshNotes();
                return;
            }
            // 通知ウィンドウを出すときは、どの付箋の知らせか分かるよう常にその付箋を前に出す。
            RevealForReminder(win, bringToFront: true);

            var result = ReminderAlertWindow.ShowFor(win, win.ViewModel.DisplayTitle, dueAt);
            // 通知ウィンドウの間は付箋を押せないので、閉じた時点で音を止める。
            ReminderSound.StopFor(win);
            if (result.Snoozed && result.SnoozeDelay is TimeSpan delay)
            {
                reminder.NextAt = DateTime.Now.Add(delay);
                win.ViewModel.RefreshReminder();
            }
            SaveAll();
            _noteManagerWindow?.RefreshNotes();
        });
    }

    /// <summary>
    /// リマインダーで知らせる付箋を見えるようにする。<paramref name="bringToFront"/> なら、その付箋だけを
    /// 他のアプリの窓より前に出す（他の付箋まで前へ出さない）。
    /// </summary>
    private void RevealForReminder(StickyNoteWindow win, bool bringToFront)
    {
        if (win.ViewModel.Model.IsHidden || !win.IsVisible)
        {
            win.ViewModel.Model.IsHidden = false;
            win.Show();
            RefreshTrayMenu();
            _noteManagerWindow?.RefreshNotes();
        }
        if (bringToFront)
        {
            // 直前に触った付箋として覚え、この後の並べ直しで奥へ戻さない。
            _lastActiveWindow = win;
            ApplyLayerOrder();
            win.BringAboveOtherWindows();
        }
        else
        {
            ApplyLayerOrder();
        }
        SaveAll();
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
        _screenLayoutTimer.Stop();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        // Mutex を持つ本来のインスタンスのときだけ保存する。
        // 二重起動をブロックされた側は _instanceMutex が null で、
        // 空の _windows で上書きしてしまわないようにする。
        if (_instanceMutex != null)
            FlushAndSave();

        _trayIcon?.Dispose();
        _ipcWindow?.Dispose();
        _newNoteHotkey?.Dispose();
        ShellSwitchTracker.Uninstall();
        if (_instanceMutex != null)
        {
            try { _instanceMutex.ReleaseMutex(); } catch (ApplicationException) { /* 未所有 */ }
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
        base.OnExit(e);
    }
}
