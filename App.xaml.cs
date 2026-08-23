using System.Drawing;
using System.Drawing.Drawing2D;
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
    private NotifyIcon? _trayIcon;

    public IReadOnlyList<StickyNoteWindow> NoteWindows => _windows;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        InitTrayIcon();

        var notes = _storage.Load();
        if (notes.Count == 0)
            notes.Add(new StickyNote());

        foreach (var note in notes)
            OpenNoteWindow(note);
    }

    private void InitTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = CreateStickyNoteIcon(),
            Text = "ScreenStickyNotes",
            Visible = true,
        };

        var startupItem = new ToolStripMenuItem("スタートアップに登録")
        {
            Checked = StartupService.IsRegistered,
            CheckOnClick = false,
        };
        startupItem.Click += (_, _) =>
        {
            if (StartupService.IsRegistered)
            {
                StartupService.Unregister();
                startupItem.Checked = false;
            }
            else
            {
                StartupService.Register();
                startupItem.Checked = true;
            }
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("全表示",    null, (_, _) => ShowAllNotes());
        menu.Items.Add("全非表示",  null, (_, _) => HideAllNotes());
        menu.Items.Add("-");
        menu.Items.Add("新規付箋作成", null, (_, _) => AddNewNote());
        menu.Items.Add("-");
        menu.Items.Add(startupItem);
        menu.Items.Add("-");
        menu.Items.Add("終了", null, (_, _) => ExitApp());
        _trayIcon.ContextMenuStrip = menu;

        // 左クリックで全表示トグル
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ToggleAllNotes();
        };
    }

    // ─── アイコン生成（付箋メモのイメージ）────────────────────────

    private static Icon CreateStickyNoteIcon()
    {
        using var bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode    = SmoothingMode.AntiAlias;
            g.CompositingMode  = CompositingMode.SourceOver;
            g.Clear(Color.Transparent);

            // 本体（黄色）
            using var bodyBrush = new SolidBrush(Color.FromArgb(255, 253, 210, 40));
            g.FillRectangle(bodyBrush, 2, 2, 24, 28);

            // 折れ角（暗い黄色）
            using var foldBrush = new SolidBrush(Color.FromArgb(200, 190, 150, 20));
            g.FillPolygon(foldBrush, (System.Drawing.Point[])[new(18, 2), new(26, 2), new(26, 10), new(18, 10)]);

            // 折れ線
            using var linePen = new System.Drawing.Pen(Color.FromArgb(160, 140, 100, 10), 1);
            g.DrawLine(linePen, 18, 2, 26, 10);
            g.DrawLine(linePen, 18, 2, 18, 10);
            g.DrawLine(linePen, 18, 10, 26, 10);

            // テキスト行
            using var textPen = new System.Drawing.Pen(Color.FromArgb(180, 120, 80, 10), 1.5f);
            g.DrawLine(textPen,  5, 14, 16, 14);
            g.DrawLine(textPen,  5, 18, 16, 18);
            g.DrawLine(textPen,  5, 22, 12, 22);
        }

        nint hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        // Clone してハンドルを自前で管理
        var cloned = (Icon)icon.Clone();
        DestroyIcon(hIcon);
        return cloned;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint handle);

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

    // ─── 付箋追加・削除 ──────────────────────────────────────────

    public void AddNewNote()
    {
        var note = new StickyNote
        {
            X = 150 + _windows.Count * 20,
            Y = 150 + _windows.Count * 20,
        };
        OpenNoteWindow(note);
        SaveAll();
    }

    private void OpenNoteWindow(StickyNote note)
    {
        var vm  = new StickyNoteViewModel(note);
        var win = new StickyNoteWindow(vm);
        _windows.Add(win);
        win.Show();
    }

    public void RemoveNote(string id)
    {
        _windows.RemoveAll(w => w.ViewModel.Model.Id == id);
        SaveAll();
    }

    public void SaveAll()
    {
        var notes = _windows.Select(w => w.ViewModel.Model).ToList();
        _storage.Save(notes);
    }

    private void ExitApp()
    {
        SaveAll();
        _trayIcon?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
