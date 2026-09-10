using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Reflection;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

public class StickyNoteWindowTests
{
    [WpfTheory]
    [InlineData(false, false, -1)]
    [InlineData(false, false, 1)]
    [InlineData(true, false, -1)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, -1)]
    [InlineData(false, true, 1)]
    public void SinglePixelResizeSurvivesSaveAndReload(bool folded, bool editing, int delta)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        // 編集サイズは閲覧サイズより大きくし、縮小しても最小サイズに戻されないようにする。
        var note = new StickyNote { Width = 230, Height = 320, FoldedWidth = 230,
            EditWidth = 430, EditHeight = 420, IsFolded = folded };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            if (editing) InvokePrivate(window, "EnterEditMode");
            window.UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(window).DpiScaleX;
            var expectedPixels = Math.Round(window.ActualWidth * dpi) + delta;
            window.Width = expectedPixels / dpi;
            window.UpdateLayout();
            InvokePrivate(window, "SaveNote");
            var restored = Assert.Single(storage.Load());
            var restoredWindow = new StickyNoteWindow(new StickyNoteViewModel(restored, App.Current.Settings), storage);
            try
            {
                restoredWindow.Show();
                if (editing) InvokePrivate(restoredWindow, "EnterEditMode");
                restoredWindow.UpdateLayout();
                Assert.Equal(expectedPixels, Math.Round(restoredWindow.ActualWidth * dpi));
            }
            finally { restoredWindow.Close(); }
        }
        finally { window.Close(); }
    }

    private static void AssertWindowCoordinate(double expected, double actual, Window window, bool vertical = false)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var scale = vertical ? dpi.DpiScaleY : dpi.DpiScaleX;
        Assert.InRange(Math.Abs(actual - expected), 0, 0.5 / scale + 1e-7);
    }

    public static IEnumerable<object[]> ModeRoundTripCases()
    {
        foreach (var hidden in new[] { false, true })
        foreach (var folded in new[] { false, true })
        foreach (var separated in new[] { false, true })
        foreach (var animated in new[] { false, true })
            yield return new object[] { hidden, folded, separated, animated };
    }

    public static IEnumerable<object[]> ModeMoveCases()
        => ModeRoundTripCases().Where(row => !(bool)row[3]).Select(row => row.Take(3).ToArray());

    [WpfTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RapidFoldRoundTrip_PreservesExpandedHeight(bool hidden, bool initiallyFolded)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = App.Current.Settings;
        var previous = settings.EnableFoldAnimation;
        settings.EnableFoldAnimation = true;
        var note = new StickyNote
        {
            X = 180, Y = 180, Width = 420, Height = 320,
            FoldedX = 90, FoldedY = 100, FoldedWidth = 230,
            IsFolded = initiallyFolded, IsTitleBarHidden = hidden,
            IsPositionSeparated = true, Content = "日本語\n2行目",
        };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            InvokePrivate(window, "ToggleFold", (object?)null);
            await Task.Delay(30);
            InvokePrivate(window, "ToggleFold", (object?)null);
            await Task.Delay(settings.Timings.FoldAnimationMs + 100);
            window.UpdateLayout();
            Assert.Equal(initiallyFolded, note.IsFolded);
            Assert.Equal(320, note.Height, 1);
            AssertWindowCoordinate(initiallyFolded ? 90 : 180, window.Left, window);
            AssertWindowCoordinate(initiallyFolded ? 100 : 180, window.Top, window, vertical: true);
            AssertWindowCoordinate(initiallyFolded ? 230 : 420, window.Width, window);
            Assert.Equal(180, note.X);
            Assert.Equal(180, note.Y);
            Assert.Equal(90, note.FoldedX);
            Assert.Equal(100, note.FoldedY);
            Assert.Equal(420, note.Width);
            Assert.Equal(230, note.FoldedWidth);
            Assert.False(GetPrivateField<bool>(window, "_isFoldAnimationRunning"));
        }
        finally { window.Close(); settings.EnableFoldAnimation = previous; }
    }

    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedUnfold_CompletesEditingCallbackOnlyOnce(bool hidden)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = App.Current.Settings;
        var previous = settings.EnableFoldAnimation;
        settings.EnableFoldAnimation = true;
        var note = new StickyNote { IsFolded = true, IsTitleBarHidden = hidden, Height = 320, Content = "日本語の本文" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var completions = 0;
            InvokePrivate(window, "ToggleFold", (Action)(() =>
            {
                completions++;
                InvokePrivate(window, "EnterEditMode");
            }));
            InvokePrivate(window, "ToggleFold", (object?)null);
            await Task.Delay(settings.Timings.FoldAnimationMs + 100);
            window.UpdateLayout();
            Assert.Equal(1, completions);
            Assert.True(note.IsFolded);
            Assert.False(GetPrivateField<bool>(window, "_isEditMode"));
            Assert.False(GetPrivateField<bool>(window, "_isFoldAnimationRunning"));
            Assert.Equal(320, note.Height);
            Assert.Equal("日本語の本文", note.Content);
        }
        finally { window.Close(); settings.EnableFoldAnimation = previous; }
    }

    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditingSizeRoundTrip_RestoresViewAndRetainsEditSize(bool hidden)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { X = 180, Y = 180, Width = 420, Height = 320, IsTitleBarHidden = hidden, Content = "本文" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            for (var cycle = 0; cycle < 3; cycle++)
            {
                InvokePrivate(window, "EnterEditMode");
                window.Width = 520;
                window.Height = 420;
                window.UpdateLayout();
                InvokePrivate(window, "EnterViewMode");
                window.UpdateLayout();
                Assert.Equal(420, window.Width);
                Assert.Equal(320, window.Height);
                Assert.Equal(520, note.EditWidth);
                Assert.Equal(420, note.EditHeight);
                InvokePrivate(window, "ToggleTitleBarHidden");
                window.UpdateLayout();
            }
        }
        finally { window.Close(); }
    }

    [WpfTheory]
    [MemberData(nameof(ModeMoveCases))]
    public void ModeMove_SaveReloadPreservesIndependentPositions(bool hidden, bool folded, bool separated)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            X = 180, Y = 180, Width = 420, Height = 320,
            FoldedX = 180, FoldedY = 180, FoldedWidth = 230,
            IsFolded = folded, IsTitleBarHidden = hidden, Content = "日本語の保存テスト\n2行目🦊",
        };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            SetPrivateField(window, "_isDragging", true);
            SetPrivateField(window, "_dragMoved", true);
            SetPrivateField(window, "_dragSeparatesFoldedPosition", separated);
            window.Left = 280;
            window.Top = 290;
            var movedX = window.Left;
            var movedY = window.Top;
            var root = (UIElement)window.FindName(hidden ? "TitleBarOverlay" : "TitleBar");
            InvokePrivate(window, "TitleBar_MouseLeftButtonUp", root,
                new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left));
            Assert.Equal(separated, note.IsPositionSeparated);
            Assert.Equal(folded && separated ? 180 : movedX, note.X);
            Assert.Equal(folded && separated ? 180 : movedY, note.Y);
            Assert.Equal(!folded && separated ? 180 : movedX, note.FoldedX);
            Assert.Equal(!folded && separated ? 180 : movedY, note.FoldedY);
            storage.Save(new[] { note });
            var restored = Assert.Single(storage.Load());
            Assert.Equal(note.X, restored.X);
            Assert.Equal(note.Y, restored.Y);
            Assert.Equal(note.FoldedX, restored.FoldedX);
            Assert.Equal(note.FoldedY, restored.FoldedY);
            Assert.Equal(note.IsPositionSeparated, restored.IsPositionSeparated);
            Assert.Equal(note.Content, restored.Content);
            Assert.Equal(hidden, restored.IsTitleBarHidden);
            Assert.Equal(folded, restored.IsFolded);
        }
        finally { window.Close(); }
    }

    [WpfTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void JapaneseEditing_UndoRedoEscapeAndTitleEnter(bool hidden, bool titleOnly)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsTitleBarHidden = hidden, Content = "元の日本語", Title = "元のタイトル" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            InvokePrivate(window, titleOnly ? "EnterTitleEditMode" : "EnterEditMode");
            var editor = (TextBox)window.FindName(titleOnly ? "TitleEditBox" : "BodyEditBox");
            Assert.True(InputMethod.GetIsInputMethodEnabled(editor));
            Assert.Equal(InputMethodState.On, InputMethod.GetPreferredImeState(editor));
            var original = editor.Text;
            editor.SelectAll();
            editor.SelectedText = "漢字・かな・カナ・半角ｶﾅ・🦊・か\u3099";
            var edited = editor.Text;
            Assert.True(editor.CanUndo);
            editor.Undo();
            Assert.Equal(original, editor.Text);
            editor.Redo();
            Assert.Equal(edited, editor.Text);
            var source = PresentationSource.FromVisual(window)!;
            var imeKey = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.ImeProcessed) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            InvokePrivate(window, "ContentBox_PreviewKeyDown", editor, imeKey);
            Assert.False(imeKey.Handled);
            Assert.True(GetPrivateField<bool>(window, "_isEditMode"));
            var finish = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, titleOnly ? Key.Enter : Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            InvokePrivate(window, "ContentBox_PreviewKeyDown", editor, finish);
            Assert.True(finish.Handled);
            Assert.False(GetPrivateField<bool>(window, "_isEditMode"));
            Assert.Equal(edited, titleOnly ? note.Title : note.Content);
            Assert.Equal(hidden, note.IsTitleBarHidden);
        }
        finally { window.Close(); }
    }

    [WpfTheory]
    [MemberData(nameof(ModeRoundTripCases))]
    public async Task ModeRoundTrip_PreservesBoundsAndJapaneseContent(bool hidden, bool folded, bool separated, bool animated)
    {
        EnsureApplication();
        var settings = App.Current.Settings;
        var previousAnimation = settings.EnableFoldAnimation;
        settings.EnableFoldAnimation = animated;
        using var temp = new TempDataDirectory();
        var note = new StickyNote
        {
            X = 180, Y = 180, Width = 420, Height = 320,
            FoldedX = separated ? 90 : 180, FoldedY = separated ? 100 : 180,
            FoldedWidth = 230, IsFolded = folded, IsTitleBarHidden = hidden,
            IsPositionSeparated = separated, Content = "# 日本語テスト\n漢字・ひらがな・カタカナ・半角ｶﾅ\n絵文字🦊と結合文字か\u3099",
        };
        var originalContent = note.Content;
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            for (var cycle = 0; cycle < 4; cycle++)
            {
                InvokePrivate(window, "ToggleFold", (object?)null);
                await Task.Delay(settings.Timings.FoldAnimationMs + 80);
                window.UpdateLayout();
                AssertWindowCoordinate(note.IsFolded ? (separated ? 90 : 180) : 180, window.Left, window);
                AssertWindowCoordinate(note.IsFolded ? (separated ? 100 : 180) : 180, window.Top, window, vertical: true);
                AssertWindowCoordinate(note.IsFolded ? 230 : 420, window.Width, window);
                Assert.Equal(320, note.Height, 1);
                Assert.Equal(originalContent, note.Content);
                Assert.Equal(separated, note.IsPositionSeparated);
            }
            Assert.Equal(folded, note.IsFolded);
        }
        finally { window.Close(); settings.EnableFoldAnimation = previousAnimation; }
    }

    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditToolbar_StaysInsideWorkAreaAtScreenEdges(bool atBottom)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var window = new StickyNoteWindow(new StickyNoteViewModel(new StickyNote { Content = "body", Width = 400, Height = 180 }, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            InvokePrivate(window, "EnterEditMode");
            var work = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(window).Handle).WorkingArea;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
            window.Left = work.Right / dpi.DpiScaleX - window.Width;
            window.Top = atBottom ? work.Bottom / dpi.DpiScaleY - window.Height : work.Top / dpi.DpiScaleY;
            window.Activate();
            InvokePrivate(window, "ShowEditToolbar");
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var popup = (Popup)window.FindName("EditToolbarPopup");
            var toolbar = (Border)window.FindName("StatusBar");
            Assert.True(popup.IsOpen);
            var start = toolbar.PointToScreen(new Point());
            var end = toolbar.PointToScreen(new Point(toolbar.ActualWidth, toolbar.ActualHeight));
            Assert.True(start.X >= work.Left - 1 && start.Y >= work.Top - 1);
            Assert.True(end.X <= work.Right + 1 && end.Y <= work.Bottom + 1);
            if (atBottom) Assert.True(end.Y <= window.PointToScreen(new Point()).Y + 1, $"toolbar={start}..{end}; window={window.PointToScreen(new Point())}; size={window.Width}x{window.Height}; dpi={dpi.DpiScaleX}; work={work}");
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void EditingBadge_ClearsHorizontalScrollbarAndReturnsWhenItDisappears()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var window = new StickyNoteWindow(new StickyNoteViewModel(new StickyNote { Content = "short" }, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            InvokePrivate(window, "EnterEditMode");
            var editor = (TextBox)window.FindName("BodyEditBox");
            var badge = (Border)window.FindName("EditingBadge");
            editor.Text = new string('W', 500);
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var viewer = (ScrollViewer)editor.Template.FindName("PART_ContentHost", editor);
            Assert.Equal(Visibility.Visible, viewer.ComputedHorizontalScrollBarVisibility);
            var bar = (ScrollBar)viewer.Template.FindName("PART_HorizontalScrollBar", viewer);
            Assert.True(badge.TranslatePoint(new Point(0, badge.ActualHeight), window).Y <= bar.TranslatePoint(new Point(), window).Y);
            editor.Text = "short";
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Equal(Visibility.Collapsed, viewer.ComputedHorizontalScrollBarVisibility);
            Assert.Equal(6, badge.Margin.Bottom);
        }
        finally { window.Close(); }
    }

    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unfold_RaisesTemporarilyAndRestoresPinOnDeactivation(bool pinned)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsFolded = true, IsTopmost = pinned };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            InvokePrivate(window, "ToggleFold", (object?)null);
            Assert.True(window.Topmost);
            Assert.Equal(pinned, note.IsTopmost);
            InvokePrivate(window, "Window_Deactivated", window, EventArgs.Empty);
            Assert.Equal(pinned, window.Topmost);
            Assert.Equal(pinned, note.IsTopmost);
        }
        finally { window.Close(); }
    }

    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReminderFlashUsesOverlayAndStopsWhenHidden(bool folded)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsFolded = folded, OpacityPercent = 65, Content = "body" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.FlashForReminder();
            var overlay = (Border)window.FindName("ReminderFlashBorder");
            Assert.True(overlay.HasAnimatedProperties);
            Assert.False(overlay.IsHitTestVisible);
            Assert.Equal(65, note.OpacityPercent);
            Assert.Equal("body", note.Content);
            window.Hide();
            Assert.False(overlay.HasAnimatedProperties);
            Assert.Equal(0, overlay.Opacity);
            window.Show();
            Assert.Equal(0, overlay.Opacity);
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void ImagePreview_ReturnsAfterShowingAndHidingTitleBar()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote
        {
            IsFolded = true, IsTitleBarHidden = false, Title = "Title", Width = 260,
            Content = "![](assets/long-folder-name/image.png)",
        };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            InvokePrivate(window, "SetResizeEnabled", false);
            window.UpdateLayout();
            var content = (RichTextBox)window.FindName("ContentBox");
            for (var i = 0; i < 3; i++)
            {
                InvokePrivate(window, "ToggleTitleBarHidden");
                window.UpdateLayout();
                Assert.Equal(Visibility.Visible, content.Visibility);
                Assert.EndsWith("image.png", new TextRange(content.Document.ContentStart, content.Document.ContentEnd).Text.Trim());
                Assert.Equal(0, content.VerticalOffset);
                InvokePrivate(window, "ToggleTitleBarHidden");
                window.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, content.Visibility);
            }
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void FoldedHiddenTitleBar_TogglingTitleBar_RecalculatesFoldedHeightAndHidesBody()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsFolded = true, IsTitleBarHidden = true, Content = "first line\nsecond line", Height = 320 };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            var content = (RichTextBox)window.FindName("ContentBox");
            window.Show();
            InvokePrivate(window, "SetResizeEnabled", false);
            window.UpdateLayout();
            var firstLineHeight = window.ActualHeight;
            InvokePrivate(window, "ToggleTitleBarHidden");
            window.UpdateLayout();
            Assert.False(window.ViewModel.IsTitleBarHidden);
            Assert.Equal(Visibility.Collapsed, content.Visibility);
            Assert.Equal(window.Height, window.MinHeight);
            Assert.Equal(window.Height, window.MaxHeight);
            Assert.Equal(window.ViewModel.TitleBarHeight + 2, window.ActualHeight, 1);
            InvokePrivate(window, "ToggleTitleBarHidden");
            window.UpdateLayout();
            Assert.True(window.ViewModel.IsTitleBarHidden);
            Assert.Equal(Visibility.Visible, content.Visibility);
            Assert.Equal(firstLineHeight, window.ActualHeight, 1);
            Assert.Equal(window.ActualHeight, window.MinHeight, 1);
            Assert.Equal(window.ActualHeight, window.MaxHeight, 1);
            Assert.Equal(window.ViewModel.TitleFontSize, content.FontSize);
            Assert.Equal(0, content.VerticalOffset);
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void MarkdownRebuild_BatchesChangesAndDoesNotKeepGeneratedUndoHistory()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsTitleBarHidden = true };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            var box = (RichTextBox)window.FindName("ContentBox");
            var notifications = 0;
            box.TextChanged += (_, _) => notifications++;
            var text = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"## Heading {i}\n**Body** {i}"));
            InvokePrivate(window, "LoadContent", text);
            Assert.Equal(400, box.Document.Blocks.Count);
            Assert.Equal(1, notifications);
            Assert.False(box.CanUndo);
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void ContextMenus_ExposeTitleBarToggleAndKeepDeletionSeparate()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var window = new StickyNoteWindow(new StickyNoteViewModel(new StickyNote(), new AppSettings()), new StorageService(temp.Path));
        try
        {
            foreach (var name in new[] { "TitleText", "ContentBox", "BodyEditBox" })
            {
                var menu = ((FrameworkElement)window.FindName(name)).ContextMenu;
                var items = menu.Items.OfType<MenuItem>().ToArray();
                var toggle = Assert.Single(items, i => Equals(i.Header, LocalizationService.T("HideTitleBar")));
                Assert.True(toggle.IsCheckable);
                Assert.Contains(items, i => Equals(i.Header, LocalizationService.T("SelectAll")));
                var hide = items.Single(i => Equals(i.Header, LocalizationService.T("HideNote")));
                Assert.IsType<Separator>(menu.Items[menu.Items.IndexOf(hide) - 1]);
                Assert.Equal(LocalizationService.T("Delete"), items.Last().Header);
            }
            var titleMenu = ((FrameworkElement)window.FindName("TitleText")).ContextMenu;
            var titleToggle = titleMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, LocalizationService.T("HideTitleBar")));
            titleToggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(window.ViewModel.IsTitleBarHidden);
            titleToggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(window.ViewModel.IsTitleBarHidden);
        }
        finally { window.Close(); }
    }

    // タイトルバーを隠すと、タイトル右クリックにしか無い項目は到達不能になる。
    // 本文の右クリックからも同じことができること。
    [WpfFact]
    public void ContentMenu_ReachesTheTitleOnlyActions()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsTitleBarHidden = true };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            var menu = ((FrameworkElement)window.FindName("ContentBox")).ContextMenu;
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Contains(items, i => Equals(i.Header, LocalizationService.T("EditTitle")));
            Assert.Contains(items, i => Equals(i.Header, LocalizationService.T("ResetPositionSeparation")));

            var zOrder = Assert.Single(items, i => Equals(i.Header, LocalizationService.T("ZOrder")));
            var zOrderChildren = zOrder.Items.OfType<MenuItem>().Select(i => i.Header).ToArray();
            Assert.Contains(LocalizationService.T("BringToFront"), zOrderChildren);
            Assert.Contains(LocalizationService.T("SendToBack"), zOrderChildren);

            // 位置が分離していないときは、そろえる項目を押せないこと。
            var reset = items.Single(i => Equals(i.Header, LocalizationService.T("ResetPositionSeparation")));
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Assert.False(reset.IsEnabled);
            window.ViewModel.IsPositionSeparated = true;
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Assert.True(reset.IsEnabled);
        }
        finally { window.Close(); }
    }

    // タイトルバーを隠していると入力欄も消えているので、タイトル編集に入ったら
    // 編集の間だけタイトルバーを出し、終わったらまた隠すこと。
    [WpfFact]
    public void TitleEditing_WithHiddenTitleBar_ShowsTheBarWhileEditing()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsTitleBarHidden = true };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            var titleBar = (FrameworkElement)window.FindName("TitleBar");
            var titleEditBox = (FrameworkElement)window.FindName("TitleEditBox");
            Assert.Equal(Visibility.Collapsed, titleBar.Visibility);

            InvokePrivate(window, "EnterTitleEditMode");
            Assert.Equal(Visibility.Visible, titleBar.Visibility);
            Assert.Equal(Visibility.Visible, titleEditBox.Visibility);

            InvokePrivate(window, "EnterViewMode");
            Assert.Equal(Visibility.Collapsed, titleBar.Visibility);
        }
        finally { window.Close(); }
    }

    // 畳んだタイトルバー無しの本文は、タイトルバーとして扱う（掴んで動かせる）。
    [WpfTheory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]    // タイトルバーがあるならそちらを掴む
    [InlineData(false, true, false)]    // 展開中の本文は読む場所
    public void BodyActsAsTitleBar_OnlyWhileFoldedWithHiddenTitleBar(bool folded, bool hiddenTitleBar, bool expected)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsFolded = folded, IsTitleBarHidden = hiddenTitleBar };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            Assert.Equal(expected, GetPrivateProperty<bool>(window, "BodyActsAsTitleBar"));

            // 編集中は本文が本来の入力欄に戻るので、掴む対象ではなくなる。
            InvokePrivate(window, "EnterEditMode");
            Assert.False(GetPrivateProperty<bool>(window, "BodyActsAsTitleBar"));
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void FoldedImagePath_ResizesFromTheFilenameEnd()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        const string path = "assets/very-long-folder-name/0月写真/image.png";
        var note = new StickyNote { Content = $"![写真]({path})", IsFolded = true, IsTitleBarHidden = true, Width = 165 };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            InvokePrivate(window, "LoadContent", note.Content);
            var box = (RichTextBox)window.FindName("ContentBox");
            string Text() => new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text.Trim();
            Assert.StartsWith("…", Text());
            Assert.EndsWith("image.png", Text());
            window.Width = 800;
            window.UpdateLayout();
            InvokePrivate(window, "UpdateImagePathPreview");
            Assert.Equal(path, Text());
            Assert.Equal($"![写真]({path})", note.Content);
        }
        finally { window.Close(); }
    }

    // アイコンの有無で UpdateImagePathPreview が予約する幅（iconWidth）が
    // 変わるが、アイコンの付け外しだけではウィンドウ幅は変わらず SizeChanged が
    // 飛ばないので、Icon の PropertyChanged からも明示的に呼び直す必要がある。
    [WpfFact]
    public void FoldedImagePath_RefreshesWhenIconChangesWithoutResizing()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        const string path = "assets/very-long-folder-name/0月写真/image.png";
        var note = new StickyNote { Content = $"![写真]({path})", IsFolded = true, IsTitleBarHidden = true, Width = 165 };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            InvokePrivate(window, "LoadContent", note.Content);
            var box = (RichTextBox)window.FindName("ContentBox");
            string Text() => new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text.Trim();
            var withoutIcon = Text();

            window.ViewModel.Icon = "💡";
            window.UpdateLayout();

            Assert.NotEqual(withoutIcon, Text());
            Assert.EndsWith("image.png", Text());
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void ShowInTaskbar_FollowsSettingAtConstructionAndOnRefresh()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var previous = app.Settings.ShowNotesInTaskbar;
        using var temp = new TempDataDirectory();
        try
        {
            app.Settings.ShowNotesInTaskbar = true;
            var window = new StickyNoteWindow(
                new StickyNoteViewModel(new StickyNote(), app.Settings), new StorageService(temp.Path));
            try
            {
                Assert.True(window.ShowInTaskbar);

                app.Settings.ShowNotesInTaskbar = false;
                window.RefreshSettings();
                Assert.False(window.ShowInTaskbar);
            }
            finally { window.Close(); }
        }
        finally { app.Settings.ShowNotesInTaskbar = previous; }
    }

    [WpfTheory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void FoldedPreview_ReadOnlyInitialization_PreservesVisibilityAndRemovesFormatting(bool locked, bool hiddenTitleBar)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        const string source = "# **操作ヘルプ** *斜体* ~~取消~~ `code` [リンク](https://example.com)\n本文";
        var note = new StickyNote
        {
            Content = source, IsFolded = true, IsTitleBarHidden = hiddenTitleBar, IsReadOnly = locked,
        };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            // 起動時のLoaded処理と同じ順序。アプリ全体のStartupは起動しない。
            InvokePrivate(window, "LoadContent", source);
            InvokePrivate(window, "ApplyReadOnlyState");
            window.Show();
            window.UpdateLayout();
            var box = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            Assert.Equal(hiddenTitleBar ? Visibility.Visible : Visibility.Collapsed, box.Visibility);
            if (hiddenTitleBar)
            {
                var paragraph = Assert.IsType<Paragraph>(box.Document.Blocks.FirstBlock);
                var run = Assert.IsType<Run>(Assert.Single(paragraph.Inlines.Cast<Inline>()));
                Assert.Equal("操作ヘルプ 斜体 取消 code リンク", run.Text);
                Assert.Equal(FontWeights.Normal, run.FontWeight);
                Assert.Equal(FontStyles.Normal, run.FontStyle);
                Assert.True(run.TextDecorations == null || run.TextDecorations.Count == 0);
                Assert.True(box.ActualHeight > 0);
            }
            Assert.Equal(source, note.Content);
            window.ViewModel.IsFolded = false;
            InvokePrivate(window, "LoadContent", source);
            var heading = Assert.IsType<Paragraph>(box.Document.Blocks.FirstBlock);
            Assert.Equal(FontWeights.Bold, heading.FontWeight);
            Assert.Contains("本文", new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text);
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void FontSizeButtons_KeepContextMenuOpenAndUpdateVisibleSizes()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var window = new StickyNoteWindow(new StickyNoteViewModel(new StickyNote(), new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            var content = (RichTextBox)window.FindName("ContentBox");
            var menu = content.ContextMenu;
            menu.PlacementTarget = content;
            menu.IsOpen = true;
            var toolbar = (StackPanel)((Border)menu.Tag).Child;
            var buttons = (StackPanel)toolbar.Children[0];
            for (var i = 0; i < 3; i++)
                ((Button)buttons.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)buttons.Children[3]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(menu.IsOpen);
            var sizes = (TextBlock)toolbar.Children[1];
            Assert.Equal($"A: {window.ViewModel.FontSize} pt    T: {window.ViewModel.TitleFontSize} pt", sizes.Text);
            menu.IsOpen = false;
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void EditSize_NeverShrinksBelowExpandedSizePerDimension()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var model = new StickyNote { Width = 360, Height = 280, EditWidth = 200, EditHeight = 400 };
        var window = new StickyNoteWindow(new StickyNoteViewModel(model, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            InvokePrivate(window, "EnterEditMode");
            Assert.Equal(360, window.Width);
            Assert.Equal(400, window.Height);
            InvokePrivate(window, "EnterViewMode");
            Assert.Equal(360, window.Width);
            Assert.Equal(280, window.Height);
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void EditSize_IsRememberedWithoutChangingNormalSize()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var model = new StickyNote { Width = 260, Height = 220, Content = "body" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(model, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            InvokePrivate(window, "EnterEditMode");
            Assert.Equal(260, window.Width);
            Assert.Equal(220, window.Height);
            window.Width = 420;
            window.Height = 360;
            window.UpdateLayout();
            Assert.Equal(420, model.EditWidth);
            Assert.Equal(360, model.EditHeight);
            Assert.Equal(260, model.Width);
            Assert.Equal(220, model.Height);
            InvokePrivate(window, "EnterViewMode");
            Assert.Equal(260, window.Width);
            Assert.Equal(220, window.Height);
            InvokePrivate(window, "EnterEditMode");
            Assert.Equal(420, window.Width);
            Assert.Equal(360, window.Height);
            InvokePrivate(window, "ToggleFold", (object?)null);
            Assert.Equal(260, model.Width);
            Assert.Equal(420, model.EditWidth);
            var restored = System.Text.Json.JsonSerializer.Deserialize<StickyNote>(System.Text.Json.JsonSerializer.Serialize(model))!;
            Assert.Equal(360, restored.EditHeight);
        }
        finally { window.Close(); }
    }

    // 編集モードは大きさだけでなく位置も一時的に変える。閲覧へ戻したときに
    // 大きさだけ元へ戻ると、付箋が勝手に動いたように見える。
    [WpfFact]
    public void EditSize_LeavingEditMode_RestoresTheExpandedViewPosition()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var model = new StickyNote
        {
            IsFolded = true,
            IsTitleBarHidden = true,
            IsPositionSeparated = true,
            X = 500, Y = 400, Width = 300, Height = 250,
            FoldedX = 100, FoldedY = 200, FoldedWidth = 180,
            Content = "first line\nsecond line",
        };
        var vm = new StickyNoteViewModel(model, new AppSettings { EnableFoldAnimation = false });
        var window = new StickyNoteWindow(vm, new StorageService(temp.Path));
        try
        {
            window.Show();
            InvokePrivate(window, "ToggleFold", (object?)null);
            Assert.Equal(500, window.Left);
            Assert.Equal(400, window.Top);

            InvokePrivate(window, "EnterEditMode");
            // 左辺・上辺をつかんで広げたときと同じ動き。
            window.Left -= 120;
            window.Top -= 60;
            window.Width += 120;
            window.Height += 60;
            window.UpdateLayout();
            Assert.Equal(500, model.X);
            Assert.Equal(400, model.Y);

            InvokePrivate(window, "EnterViewMode");
            Assert.Equal(500, window.Left);
            Assert.Equal(400, window.Top);
            Assert.Equal(300, window.Width);
            AssertWindowCoordinate(250, window.Height, window, vertical: true);
            Assert.Equal(500, model.X);
            Assert.Equal(400, model.Y);
            // 折りたたみ側の位置と幅は巻き込まれない。
            Assert.Equal(100, model.FoldedX);
            Assert.Equal(200, model.FoldedY);
            Assert.Equal(180, model.FoldedWidth);
        }
        finally { window.Close(); }
    }

    // 編集用の大きさへ広げると作業領域からはみ出す付箋は、画面内へ押し戻される。
    // そのぶんを閲覧時の位置として覚えてしまうと、編集して閉じただけで動く。
    [WpfFact]
    public void EditSize_EnteringEditModeNearTheScreenEdge_DoesNotMoveTheNote()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var workArea = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var model = new StickyNote
        {
            X = workArea.Right - 320, Y = workArea.Bottom - 270,
            Width = 300, Height = 250,
            EditWidth = 420, EditHeight = 360,
            Content = "body",
        };
        var vm = new StickyNoteViewModel(model, new AppSettings());
        var window = new StickyNoteWindow(vm, new StorageService(temp.Path));
        try
        {
            window.Show();
            var dpi = VisualTreeHelper.GetDpi(window);
            window.Left = workArea.Right / dpi.DpiScaleX - 320;
            window.Top = workArea.Bottom / dpi.DpiScaleY - 270;
            var left = model.X;
            var top = model.Y;

            InvokePrivate(window, "EnterEditMode");
            window.UpdateLayout();
            Assert.Equal(left, model.X);
            Assert.Equal(top, model.Y);

            InvokePrivate(window, "EnterViewMode");
            AssertWindowCoordinate(left, window.Left, window);
            AssertWindowCoordinate(top, window.Top, window, vertical: true);
            Assert.Equal(left, model.X);
            Assert.Equal(top, model.Y);
        }
        finally { window.Close(); }
    }

    // 編集中に付箋をつかんで動かしたぶんは、閲覧へ戻しても残す。
    [WpfFact]
    public void EditSize_MovingWhileEditing_KeepsTheNewPosition()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var model = new StickyNote { X = 300, Y = 240, Width = 300, Height = 250, Content = "body" };
        var vm = new StickyNoteViewModel(model, new AppSettings());
        var window = new StickyNoteWindow(vm, new StorageService(temp.Path));
        try
        {
            window.Show();
            InvokePrivate(window, "EnterEditMode");
            window.Left = 420;
            window.Top = 360;
            // ドラッグの終わりに TitleBar_MouseLeftButtonUp が呼ぶのと同じ保存。
            InvokePrivate(window, "SaveCurrentPositionToModel");

            InvokePrivate(window, "EnterViewMode");
            Assert.Equal(420, window.Left);
            Assert.Equal(360, window.Top);
            Assert.Equal(420, model.X);
            Assert.Equal(360, model.Y);
        }
        finally { window.Close(); }
    }

    // Ctrl+Enter で編集を終えると、編集モードの出入りが Ctrl 押下中に走る。
    // それを Ctrl ドラッグと同じ「位置を分ける」合図と取り違えてはいけない。
    // Ctrl キーの状態はテストから作れないので、同じ分岐を通る
    // _dragSeparatesFoldedPosition で代用する。
    [WpfFact]
    public void LeavingEditMode_DoesNotSeparateThePositionOnItsOwn()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var model = new StickyNote { X = 300, Y = 240, Width = 300, Height = 250, Content = "body" };
        var vm = new StickyNoteViewModel(model, new AppSettings());
        var window = new StickyNoteWindow(vm, new StorageService(temp.Path));
        try
        {
            window.Show();
            SetPrivateField(window, "_dragSeparatesFoldedPosition", true);

            InvokePrivate(window, "EnterEditMode");
            InvokePrivate(window, "EnterViewMode");

            Assert.False(model.IsPositionSeparated);
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public async Task FontPicker_FirstOpeningReplacesLoadingWithNames()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var window = new StickyNoteWindow(new StickyNoteViewModel(new StickyNote(), new AppSettings()),
            new StorageService(temp.Path));
        try
        {
            window.Show();
            var popup = (Popup)typeof(StickyNoteWindow).GetField("_fontPopup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            popup.PlacementTarget = window;
            popup.IsOpen = true;
            var panel = (StackPanel)((Border)popup.Child).Child;
            var list = panel.Children.OfType<ListBox>().Single();
            for (var i = 0; i < 100 && list.ItemsSource == null; i++) await Task.Delay(50);
            Assert.NotNull(list.ItemsSource);
            Assert.NotEmpty(list.Items.Cast<object>());
            Assert.All(list.Items.Cast<object>(), item => Assert.IsType<FontCatalog.Entry>(item));
            popup.IsOpen = false;
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void LinkEditDialog_HidesToolbarUntilClosed()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var window = new StickyNoteWindow(new StickyNoteViewModel(
            new StickyNote { Content = "[label](https://example.com)" }, new AppSettings()),
            new StorageService(temp.Path));
        try
        {
            window.Show();
            InvokePrivate(window, "EnterEditMode");
            var toolbar = Assert.IsType<Popup>(window.FindName("EditToolbarPopup"));
            Assert.True(toolbar.IsOpen);
            bool hidden = false;
            bool stayedHidden = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) =>
            {
                var dialog = window.OwnedWindows.OfType<LinkEditDialog>().FirstOrDefault(d => d.IsVisible);
                if (dialog == null) return;
                timer.Stop();
                hidden = !toolbar.IsOpen;
                InvokePrivate(window, "ShowEditToolbar");
                stayedHidden = !toolbar.IsOpen;
                dialog.Close();
            };
            timer.Start();
            InvokePrivate(window, "EditMarkdownLink", MarkdownLinkEditor.FindAt(window.ViewModel.Content, 3)!);
            Assert.True(hidden);
            Assert.True(stayedHidden);
            Assert.True(toolbar.IsOpen);
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void ConstructingNotes_DoesNotStartInstalledFontScan()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var windows = new List<StickyNoteWindow>();
        // 走査は静的にキャッシュされるので、先行するテスト（フォントピッカーを
        // 開くものなど）が既に始めていることがある。ここで見たいのは「付箋を
        // 生成しただけでは走査が始まらない」ことなので、実行順に左右されない
        // よう、キャッシュを空に戻してから確認する。null に戻すと次の要求で
        // 走査し直されるだけで、他のテストには影響しない。
        var field = typeof(FontCatalog).GetField("_loading",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        field.SetValue(null, null);
        try
        {
            for (var i = 0; i < 3; i++)
                windows.Add(new StickyNoteWindow(
                    new StickyNoteViewModel(new StickyNote(), new AppSettings()), storage));
            Assert.Null(field.GetValue(null));
        }
        finally
        {
            foreach (var window in windows) window.Close();
        }
    }

    [WpfFact]
    public void ReloadExternalContent_FromBackgroundThread_UpdatesOnlyThroughUiDispatcher()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        Directory.CreateDirectory(temp.Path);
        var storage = new StorageService(temp.Path);
        var externalPath = Path.Combine(temp.Path, "external.md");
        File.WriteAllText(externalPath, "updated externally");
        var vm = new StickyNoteViewModel(
            new StickyNote
            {
                Content = "cached content",
                ExternalContentPath = externalPath,
                IsReadOnly = true,
            },
            new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            Task.Run(window.ReloadExternalContent).GetAwaiter().GetResult();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.Equal("updated externally", vm.Content);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void SetBodyFontSize_ViewModeRecalculatesMarkdownHeadingSize()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(new StickyNote { Content = "# Heading\n\nBody", FontSize = 13 }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Show();
            var contentBox = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var before = Assert.IsType<Paragraph>(contentBox.Document.Blocks.First());
            Assert.Equal(21, before.FontSize);

            InvokePrivate(window, "SetBodyFontSize", 20d);

            var after = Assert.IsType<Paragraph>(contentBox.Document.Blocks.First());
            Assert.Equal(28, after.FontSize);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void BodyEditBox_EditModeContextMenuAndViewModeRoundTrip_WorkTogether()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(
            new StickyNote { Content = "# title\n\nbody" },
            new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            var bodyEditBox = Assert.IsType<TextBox>(window.FindName("BodyEditBox"));
            var contentBox = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));

            Assert.NotNull(bodyEditBox.ContextMenu);
            Assert.NotNull(contentBox.ContextMenu);
            Assert.Contains(
                bodyEditBox.ContextMenu.Items.OfType<MenuItem>(),
                item => Equals(item.Header, "貼り付け") || Equals(item.Header, "Paste"));
            Assert.Contains(
                bodyEditBox.ContextMenu.Items.OfType<MenuItem>(),
                item => Equals(item.Header, "Markdownリンクとして貼り付け") || Equals(item.Header, "Paste as Markdown link"));
            Assert.Contains(
                bodyEditBox.ContextMenu.Items.OfType<MenuItem>(),
                item => Equals(item.Header, "リマインダー...") || Equals(item.Header, "Reminder..."));
            AssertMenuOmitsIconAndColorOptions(bodyEditBox.ContextMenu);
            AssertMenuOmitsIconAndColorOptions(contentBox.ContextMenu);

            InvokePrivate(window, "EnterEditMode");

            Assert.Equal(Visibility.Visible, bodyEditBox.Visibility);
            Assert.Equal(Visibility.Collapsed, contentBox.Visibility);
            Assert.Equal("# title\n\nbody", bodyEditBox.Text);

            bodyEditBox.Text = "# heading\nあいう";

            Assert.Equal("# heading\nあいう", vm.Content);

            bodyEditBox.Select(bodyEditBox.Text.Length, 0);
            var textData = new DataObject();
            textData.SetData(DataFormats.UnicodeText, "\n追加");
            InvokePrivate(window, "PasteFromDataObject", textData);

            Assert.Equal("# heading\nあいう\n追加", vm.Content);
            Assert.Equal(vm.Content.Length, bodyEditBox.SelectionStart);

            bodyEditBox.Select(bodyEditBox.Text.Length, 0);
            var imageData = new DataObject();
            imageData.SetData(DataFormats.Bitmap, CreateBitmapSource());
            InvokePrivate(window, "PasteFromDataObject", imageData);

            Assert.Contains("![image](assets/image-", vm.Content);
            Assert.Contains(".png)", vm.Content);
            Assert.True(Directory.Exists(storage.GetNoteAssetsDirectoryPath(vm.Model.Id)));
            Assert.Single(Directory.EnumerateFiles(storage.GetNoteAssetsDirectoryPath(vm.Model.Id), "*.png"));

            InvokePrivate(window, "EnterViewMode");

            Assert.Equal(Visibility.Collapsed, bodyEditBox.Visibility);
            Assert.Equal(Visibility.Visible, contentBox.Visibility);
            Assert.Contains("# heading\nあいう\n追加", vm.Content);
            Assert.NotEmpty(contentBox.Document.Blocks);
            Assert.All(contentBox.Document.Blocks.OfType<Block>(), block => Assert.IsType<Paragraph>(block));
        }
        finally
        {
            window.Close();
        }
    }

    // クイックアクション行は押しても何も起きないので、矢印キーの
    // 移動先にしない。キーボードからは通常のメニュー項目を使う。
    [WpfFact]
    public void ContextMenus_QuickActionsRow_IsNotAKeyboardStop()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(
            new StickyNote { Content = "body" },
            new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            var bodyEditBox = Assert.IsType<TextBox>(window.FindName("BodyEditBox"));
            var contentBox = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var titleText = Assert.IsType<TextBlock>(window.FindName("TitleText"));

            foreach (var contextMenu in new[]
                     {
                         contentBox.ContextMenu, bodyEditBox.ContextMenu, titleText.ContextMenu,
                     })
            {
                Assert.NotNull(contextMenu);
                var row = Assert.IsType<Border>(contextMenu.Tag);
                var toolbar = Assert.IsType<StackPanel>(row.Child);
                var panel = Assert.IsType<StackPanel>(toolbar.Children[0]);
                Assert.False(row.Focusable);
                Assert.Equal(7, panel.Children.Count);
                Assert.All(
                    panel.Children.OfType<Button>(),
                    button => Assert.False(button.Focusable));
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertMenuOmitsIconAndColorOptions(ContextMenu contextMenu)
    {
        Assert.DoesNotContain(
            contextMenu.Items.OfType<MenuItem>(),
            item => Equals(item.Header, "アイコンを変更") || Equals(item.Header, "Change icon"));
        Assert.DoesNotContain(
            contextMenu.Items.OfType<MenuItem>(),
            item => Equals(item.Header, "色を変更") || Equals(item.Header, "Change color"));
    }

    [WpfFact]
    public void LoadContent_ReadOnlyMarkdownImageWithoutWidth_DoesNotUpscaleNaturalSize()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            Width = 420,
            Height = 320,
            IsReadOnly = true,
            Content = "![image](assets/pasted.png)",
        };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        SavePng(System.IO.Path.Combine(assetsDir, "pasted.png"), CreateBitmapSource());
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            InvokePrivate(window, "LoadContent", note.Content);
            var contentBox = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var image = Assert.Single(EnumerateImages(contentBox.Document));

            Assert.Equal(2 / VisualTreeHelper.GetDpi(window).DpiScaleX, image.Width, 8);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void LoadContent_MarkdownImageTooltipShowsResolvedFilePath()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            Content = "![説明](assets/pasted.png)",
        };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        var imagePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(assetsDir, "pasted.png"));
        SavePng(imagePath, CreateBitmapSource());
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            InvokePrivate(window, "LoadContent", note.Content);
            var contentBox = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var image = Assert.Single(EnumerateImages(contentBox.Document));

            Assert.Equal(imagePath, Assert.IsType<string>(image.ToolTip));
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void EnterEditMode_PrefersFullWidthNativeIme()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(new StickyNote { Content = "本文" }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            InvokePrivate(window, "EnterEditMode");
            var bodyEditBox = Assert.IsType<TextBox>(window.FindName("BodyEditBox"));
            var conversionMode = InputMethod.GetPreferredImeConversionMode(bodyEditBox);

            Assert.True((conversionMode & ImeConversionModeValues.Native) != 0);
            Assert.True((conversionMode & ImeConversionModeValues.FullShape) != 0);
            Assert.False((conversionMode & ImeConversionModeValues.Katakana) != 0);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void BodyEditBox_LostKeyboardFocus_KeepsEditMode()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(new StickyNote { Content = "body" }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            InvokePrivate(window, "EnterEditMode");
            var bodyEditBox = Assert.IsType<TextBox>(window.FindName("BodyEditBox"));
            var contentBox = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));

            InvokePrivate(window, "BodyEditBox_LostKeyboardFocus", bodyEditBox, null);

            Assert.Equal(Visibility.Visible, bodyEditBox.Visibility);
            Assert.Equal(Visibility.Collapsed, contentBox.Visibility);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void DoneEditing_Click_LeavesEditMode()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(new StickyNote { Content = "body" }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            InvokePrivate(window, "EnterEditMode");
            var doneButton = Assert.IsType<Button>(window.FindName("DoneEditingButton"));
            var bodyEditBox = Assert.IsType<TextBox>(window.FindName("BodyEditBox"));
            var contentBox = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));

            InvokePrivate(window, "DoneEditing_Click", doneButton, new RoutedEventArgs());

            Assert.Equal(Visibility.Collapsed, bodyEditBox.Visibility);
            Assert.Equal(Visibility.Visible, contentBox.Visibility);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void EnterEditMode_KeepsEditToolbarOpen()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(new StickyNote { Content = "body" }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Show();
            InvokePrivate(window, "EnterEditMode");
            var toolbar = Assert.IsType<Popup>(window.FindName("EditToolbarPopup"));

            Assert.True(toolbar.IsOpen);

            InvokePrivate(window, "ScheduleHideEditToolbar");

            Assert.True(toolbar.IsOpen);
            var editor = Assert.IsType<TextBox>(window.FindName("BodyEditBox"));
            InvokePrivate(window, "BodyEditBox_ContextMenuOpening", editor, null);
            Assert.False(toolbar.IsOpen);
            InvokePrivate(window, "ContentContextMenu_Closed", editor.ContextMenu, new RoutedEventArgs());
            Assert.True(toolbar.IsOpen);

            window.UpdateLayout();
            var before = toolbar.Child.PointToScreen(new System.Windows.Point());
            window.Height += 60;
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var after = toolbar.Child.PointToScreen(new System.Windows.Point());
            Assert.True(after.Y > before.Y + 40, "Toolbar should follow the resized note bottom.");
            typeof(StickyNoteWindow).GetField("_isDragging", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            window.Left += 50;
            window.Top += 30;
            window.UpdateLayout();
            var moved = toolbar.Child.PointToScreen(new System.Windows.Point());
            Assert.True(moved.X > after.X + 35, "Toolbar should follow horizontal dragging.");
            Assert.True(moved.Y > after.Y + 20, "Toolbar should follow vertical dragging.");
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void ContextMenuClosed_WhenAnotherWindowIsActive_DoesNotRestoreToolbar()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var vm = new StickyNoteViewModel(new StickyNote { Content = "body" }, new AppSettings());
        var window = new StickyNoteWindow(vm, new StorageService(temp.Path));
        var other = new System.Windows.Window();
        try
        {
            window.Show();
            window.Activate();
            InvokePrivate(window, "EnterEditMode");
            var toolbar = Assert.IsType<Popup>(window.FindName("EditToolbarPopup"));
            var editor = Assert.IsType<TextBox>(window.FindName("BodyEditBox"));
            InvokePrivate(window, "BodyEditBox_ContextMenuOpening", editor, null);
            other.Show();
            other.Activate();
            Assert.False(window.IsActive);
            InvokePrivate(window, "ContentContextMenu_Closed", editor.ContextMenu, new RoutedEventArgs());
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.False(toolbar.IsOpen);
            Assert.True(other.IsActive);
            InvokePrivate(window, "ShowEditToolbar");
            Assert.False(toolbar.IsOpen);
            window.Activate();
            Assert.True(toolbar.IsOpen);
        }
        finally
        {
            other.Close();
            window.Close();
        }
    }

    [WpfFact]
    public void ColorAndIconButtons_ToggleTheirPalettes()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(new StickyNote { Content = "body" }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Show();
            InvokePrivate(window, "EnterEditMode");
            var colorButton = Assert.IsType<Button>(window.FindName("ColorButton"));
            var iconButton = Assert.IsType<Button>(window.FindName("IconButton"));
            var colorPopup = GetPrivateField<Popup>(window, "_colorPopup");
            var iconPopup = GetPrivateField<Popup>(window, "_iconPopup");

            InvokePrivate(window, "Color_Click", colorButton, new RoutedEventArgs());
            Assert.True(colorPopup.IsOpen);
            Assert.True(colorPopup.StaysOpen); // Mouse-down must not dismiss before the button's Click.
            InvokePrivate(window, "Color_Click", colorButton, new RoutedEventArgs());
            Assert.False(colorPopup.IsOpen);

            InvokePrivate(window, "Color_Click", colorButton, new RoutedEventArgs());
            Assert.True(colorPopup.IsOpen);

            InvokePrivate(window, "Icon_Click", iconButton, new RoutedEventArgs());
            Assert.True(iconPopup.IsOpen);
            Assert.True(iconPopup.StaysOpen);
            Assert.False(colorPopup.IsOpen);
            InvokePrivate(window, "Icon_Click", iconButton, new RoutedEventArgs());
            Assert.False(iconPopup.IsOpen);

            InvokePrivate(window, "OpenIconPickerAtMouse");
            Assert.True(iconPopup.IsOpen);
            Assert.Same(window.FindName("RootBorder"), iconPopup.PlacementTarget);
            InvokePrivate(window, "OpenColorPickerAtMouse");
            Assert.False(iconPopup.IsOpen);
            Assert.True(colorPopup.IsOpen);
            Assert.Same(window.FindName("RootBorder"), colorPopup.PlacementTarget);
            InvokePrivate(window, "Window_Deactivated", window, EventArgs.Empty);
            Assert.False(colorPopup.IsOpen);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.False(GetPrivateField<bool>(window, "_watchingPickerInput"));
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void Hide_ClosesTransientPopups()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(new StickyNote { Content = "body" }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Show();
            InvokePrivate(window, "EnterEditMode");
            var colorButton = Assert.IsType<Button>(window.FindName("ColorButton"));
            var colorPopup = GetPrivateField<Popup>(window, "_colorPopup");
            var toolbar = Assert.IsType<Popup>(window.FindName("EditToolbarPopup"));

            InvokePrivate(window, "Color_Click", colorButton, new RoutedEventArgs());
            Assert.True(colorPopup.IsOpen);
            Assert.True(toolbar.IsOpen);

            window.Hide();

            Assert.False(colorPopup.IsOpen);
            Assert.False(toolbar.IsOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void ResizeMarkdownImage_ReadOnlyNote_SavesDisplayOverrideWithoutChangingContent()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            IsReadOnly = true,
            Content = "![image](assets/pasted.png)",
        };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        SavePng(System.IO.Path.Combine(assetsDir, "pasted.png"), CreateBitmapSource());
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            InvokePrivate(window, "LoadContent", note.Content);
            var contexts = (System.Collections.IDictionary)window.GetType()
                .GetField("_markdownImageContexts", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window)!;
            var context = Assert.Single(contexts.Values.Cast<object>());

            InvokePrivate(window, "ResizeMarkdownImage", context, 200);

            Assert.Equal("![image](assets/pasted.png)", note.Content);
            Assert.Contains(note.ExternalImageWidthOverrides, pair => pair.Key.EndsWith(":assets/pasted.png") && pair.Value > 0);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void ResizeMarkdownImage_WhenWidthAttributeIsDuplicated_ReplacesAllWidthAttributes()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            Content = "![image](assets/pasted.png){width=238}{width=1190}",
        };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        SavePng(System.IO.Path.Combine(assetsDir, "pasted.png"), CreateBitmapSource());
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            InvokePrivate(window, "LoadContent", note.Content);
            var contexts = (System.Collections.IDictionary)window.GetType()
                .GetField("_markdownImageContexts", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window)!;
            var context = Assert.Single(contexts.Values.Cast<object>());

            InvokePrivate(window, "ResizeMarkdownImage", context, 200);

            var expectedWidth = (int)Math.Round(4 / VisualTreeHelper.GetDpi(window).DpiScaleX);
            Assert.Equal($"![image](assets/pasted.png){{width={expectedWidth}}}", note.Content);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void RemoveMarkdownImageReference_WithUndoRestoresDetachedImage()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            Content = "before\n![image](assets/pasted.png)\nafter",
        };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        SavePng(System.IO.Path.Combine(assetsDir, "pasted.png"), CreateBitmapSource());
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            InvokePrivate(window, "LoadContent", note.Content);
            var context = GetOnlyMarkdownImageContext(window);

            InvokePrivate(window, "RemoveMarkdownImageReference", context, true);
            Assert.Equal("before\nafter", note.Content);

            var undone = Assert.IsType<bool>(InvokePrivateWithResult(window, "TryUndoLastContentChange", window.FindName("ContentBox"))!);

            Assert.True(undone);
            Assert.Equal("before\n![image](assets/pasted.png)\nafter", note.Content);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void RemoveMarkdownImageReference_UndoDoesNotOverwriteLaterContent()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            Content = "before\n![image](assets/pasted.png)\nafter",
        };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        SavePng(System.IO.Path.Combine(assetsDir, "pasted.png"), CreateBitmapSource());
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            InvokePrivate(window, "LoadContent", note.Content);
            var context = GetOnlyMarkdownImageContext(window);

            InvokePrivate(window, "RemoveMarkdownImageReference", context, true);
            vm.Content = "later edit";

            var undone = Assert.IsType<bool>(InvokePrivateWithResult(window, "TryUndoLastContentChange", window.FindName("ContentBox"))!);

            Assert.False(undone);
            Assert.Equal("later edit", note.Content);
        }
        finally
        {
            window.Close();
        }
    }

    // タイトルバーを隠すモードでは畳む先が無いので、本文を消さずに
    // 1行目だけを残す。本文を消してしまうと畳んだ付箋が空になる。
    [WpfFact]
    public void FoldedNote_WithHiddenTitleBar_KeepsTheFirstBodyLine()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote
        {
            Height = 320,
            IsFolded = true,
            IsTitleBarHidden = true,
            Content = "first line\nsecond line\nthird line",
        };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            var contentBox = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            Assert.Equal(Visibility.Visible, contentBox.Visibility);
            // 1行しか出ない高さにスクロールバーが出ると畳んだ見た目が壊れる。
            Assert.Equal(ScrollBarVisibility.Disabled, contentBox.VerticalScrollBarVisibility);
            Assert.True(window.Height < note.Height,
                $"folded height {window.Height} should be below the open height {note.Height}");
        }
        finally { window.Close(); }
    }

    // アイコンが無いとつかむ所が数ピクセルの余白しか残らず、付箋を動かせない。
    // 代わりの持ち手を出す。アイコンがあるならそれ自体がつかみ代になる。
    [WpfTheory]
    [InlineData("", Visibility.Visible)]
    [InlineData("🦊", Visibility.Collapsed)]
    public void MoveHandle_AppearsOnlyWhenTheNoteHasNoIcon(string icon, Visibility expected)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsTitleBarHidden = true, Icon = icon };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            var handle = Assert.IsType<System.Windows.Shapes.Path>(window.FindName("OverlayMoveHandle"));
            InvokePrivate(window, "UpdateTitleBarButtonsVisibility");
            Assert.Equal(expected, handle.Visibility);
        }
        finally { window.Close(); }
    }

    // 編集ツールバーはフォーカスが外れると隠れるので、それだけでは編集中か
    // どうか分からない。表示はフォーカスに関係なく出しておく。
    [WpfFact]
    public void EditingBadge_FollowsTheEditMode()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(new StickyNote { Content = "body" }, new AppSettings()),
            new StorageService(temp.Path));
        try
        {
            var badge = Assert.IsType<Border>(window.FindName("EditingBadge"));
            var badgeText = Assert.IsType<TextBlock>(window.FindName("EditingBadgeText"));
            Assert.Equal(Visibility.Collapsed, badge.Visibility);
            // 文言は言語カタログから来る。キーが無いとキー名がそのまま出る。
            Assert.Equal(LocalizationService.T("EditingBadge"), badgeText.Text);
            Assert.NotEqual("EditingBadge", badgeText.Text);

            InvokePrivate(window, "EnterEditMode");
            Assert.Equal(Visibility.Visible, badge.Visibility);

            InvokePrivate(window, "EnterViewMode");
            Assert.Equal(Visibility.Collapsed, badge.Visibility);
        }
        finally { window.Close(); }
    }

    // 編集ロック中の付箋は編集モードに入らないので、表示も出さない。
    [WpfFact]
    public void EditingBadge_StaysHiddenOnALockedNote()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(new StickyNote { IsReadOnly = true }, new AppSettings()),
            new StorageService(temp.Path));
        try
        {
            InvokePrivate(window, "EnterEditMode");
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Border>(window.FindName("EditingBadge")).Visibility);
        }
        finally { window.Close(); }
    }

    // 1行しか見えない状態では、アイコンが唯一の付箋の見分けになる。
    // ホバーしていなくても出しておく。ボタン類は出さない。
    [WpfFact]
    public void FoldedNote_WithHiddenTitleBar_ShowsTheIconWithoutHovering()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsFolded = true, IsTitleBarHidden = true, Icon = "🦊" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            Assert.Equal(Visibility.Visible,
                Assert.IsType<Grid>(window.FindName("TitleBarOverlay")).Visibility);
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<StackPanel>(window.FindName("TitleBarOverlayActions")).Visibility);
        }
        finally { window.Close(); }
    }

    // 畳むとタイトルバー表示の付箋と1行表示の付箋が同じ形になるので、
    // タイトルバーを隠している側にだけ左端の縦線を出して見分けを付ける。
    [WpfTheory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void TitleBarHiddenSpine_ShowsOnlyWhileTheTitleBarIsHidden(bool hiddenTitleBar, bool folded)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsFolded = folded, IsTitleBarHidden = hiddenTitleBar, Content = "body" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var spine = Assert.IsType<System.Windows.Shapes.Rectangle>(window.FindName("TitleBarHiddenSpine"));
            Assert.Equal(hiddenTitleBar ? Visibility.Visible : Visibility.Collapsed, spine.Visibility);
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void TitleBarHiddenSpine_FollowsTheTitleBarToggle()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsTitleBarHidden = false, Content = "body" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            var spine = Assert.IsType<System.Windows.Shapes.Rectangle>(window.FindName("TitleBarHiddenSpine"));
            Assert.Equal(Visibility.Collapsed, spine.Visibility);

            InvokePrivate(window, "ToggleTitleBarHidden");
            window.UpdateLayout();
            Assert.Equal(Visibility.Visible, spine.Visibility);

            InvokePrivate(window, "ToggleTitleBarHidden");
            window.UpdateLayout();
            Assert.Equal(Visibility.Collapsed, spine.Visibility);
        }
        finally { window.Close(); }
    }

    // 縦線は左端の幅リサイズ枠と重なり、畳んだ1行表示では本文がドラッグの
    // つかみ代になる。当たり判定を持たせるとどちらも塞いでしまう。
    [WpfFact]
    public void TitleBarHiddenSpine_TakesNoMouseInput()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsFolded = true, IsTitleBarHidden = true, Content = "body" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            var spine = Assert.IsType<System.Windows.Shapes.Rectangle>(window.FindName("TitleBarHiddenSpine"));
            Assert.False(spine.IsHitTestVisible);
        }
        finally { window.Close(); }
    }

    // 角の丸みは Border だけでなく、子要素のはみ出しを切る2枚のクリップにも効く。
    [WpfTheory]
    [InlineData(0)]
    [InlineData(12)]
    public void NoteFrame_FollowsTheCornerRadiusSetting(double radius)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings();
        settings.Layout.NoteCornerRadius = radius;
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(new StickyNote { Content = "body" }, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var root = Assert.IsType<Border>(window.FindName("RootBorder"));
            var surface = Assert.IsType<Grid>(window.FindName("NoteSurface"));

            Assert.Equal(new CornerRadius(radius), root.CornerRadius);
            Assert.Equal(radius, Assert.IsType<RectangleGeometry>(root.Clip).RadiusX);
            // 内側は枠線のぶんだけ小さい丸み。
            Assert.Equal(Math.Max(0, radius - 1), Assert.IsType<RectangleGeometry>(surface.Clip).RadiusX);
        }
        finally { window.Close(); }
    }

    // 丸みを変えても大きさは変わらないので SizeChanged は飛ばない。
    // それでもクリップが引き直されること。
    [WpfFact]
    public void NoteFrame_FollowsACornerRadiusChangeWhileOpen()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings();
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(new StickyNote { Content = "body" }, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var root = Assert.IsType<Border>(window.FindName("RootBorder"));
            Assert.Equal(0, Assert.IsType<RectangleGeometry>(root.Clip).RadiusX);

            settings.Layout.NoteCornerRadius = 12;
            window.RefreshSettings();
            window.UpdateLayout();

            Assert.Equal(new CornerRadius(12), root.CornerRadius);
            Assert.Equal(12, Assert.IsType<RectangleGeometry>(root.Clip).RadiusX);
        }
        finally { window.Close(); }
    }

    // 「枠なし」は太さを0にせず透明で塗る。太さを変えると畳んだときの
    // 高さ（FoldedHeight）まで動いてしまうため。
    [WpfFact]
    public void NoteFrame_WithoutABorder_KeepsItsThicknessAndPaintsItTransparent()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings { NoteBorderColor = AppSettings.NoteBorderNone };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(new StickyNote { Content = "body" }, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var root = Assert.IsType<Border>(window.FindName("RootBorder"));

            Assert.Equal(new Thickness(1), root.BorderThickness);
            Assert.Equal(0, Assert.IsType<SolidColorBrush>(root.BorderBrush).Color.A);
        }
        finally { window.Close(); }
    }

    // アイコンは App の設定を見る。付箋ごとではなくアプリ全体の見た目の話なので。
    [WpfFact]
    public void NoteIcon_FollowsTheMonochromeSetting()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var previous = App.Current.Settings.MonochromeIcons;
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(new StickyNote { Icon = "🔴" }, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            var icon = Assert.IsType<Image>(window.FindName("IconImage"));
            var colour = icon.Source;

            App.Current.Settings.MonochromeIcons = true;
            window.RefreshSettings();
            window.UpdateLayout();

            Assert.NotSame(colour, icon.Source);
            Assert.Same(EmojiRenderer.Render("🔴", monochrome: true), icon.Source);
        }
        finally
        {
            App.Current.Settings.MonochromeIcons = previous;
            window.Close();
        }
    }

    [WpfFact]
    public void TitleBarHiddenSpine_CanBeTurnedOffInSettings()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings { ShowTitleBarHiddenSpine = false };
        var note = new StickyNote { IsFolded = true, IsTitleBarHidden = true, Content = "body" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var spine = Assert.IsType<System.Windows.Shapes.Rectangle>(window.FindName("TitleBarHiddenSpine"));
            Assert.Equal(Visibility.Collapsed, spine.Visibility);
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void TitleBarHiddenSpine_UsesTheConfiguredWidth()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings();
        settings.Layout.TitleBarHiddenSpineWidth = 8;
        var note = new StickyNote { IsTitleBarHidden = true, Content = "body" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var spine = Assert.IsType<System.Windows.Shapes.Rectangle>(window.FindName("TitleBarHiddenSpine"));
            Assert.Equal(8, spine.Width);
            Assert.Equal(8, spine.ActualWidth);
        }
        finally { window.Close(); }
    }

    // 既定では端から離して置く。上下も左と同じだけ空け、四方の余白をそろえる。
    [WpfFact]
    public void TitleBarHiddenSpine_SitsOffTheEdgeByDefault()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings();
        var note = new StickyNote { IsTitleBarHidden = true, Content = "body" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var spine = Assert.IsType<System.Windows.Shapes.Rectangle>(window.FindName("TitleBarHiddenSpine"));
            Assert.Equal(3, spine.Margin.Left);
            Assert.Equal(3, spine.Margin.Top);
            Assert.Equal(3, spine.Margin.Bottom);

            // 掴むための透明な板が右へはみ出す先は本文の左余白の中まで。
            // 端からの距離 + 板の幅 が、本文の文字が始まる位置
            // （ContentBox の Padding.Left）を越えないこと。
            var handle = Assert.IsType<Border>(window.FindName("TitleBarHiddenSpineHandle"));
            var box = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            Assert.True(3 + handle.ActualWidth <= box.Padding.Left,
                $"grab handle reaches {3 + handle.ActualWidth}px, text starts at {box.Padding.Left}px");
        }
        finally { window.Close(); }
    }

    // 従来の置き方に戻すと、余白なしで左端に貼り付く。
    [WpfFact]
    public void TitleBarHiddenSpine_CanGoBackToTheEdge()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings { TitleBarHiddenSpineStyle = AppSettings.SpineStyleEdge };
        settings.Layout.NoteCornerRadius = 8;
        var note = new StickyNote { IsTitleBarHidden = true, Content = "body" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var spine = Assert.IsType<System.Windows.Shapes.Rectangle>(window.FindName("TitleBarHiddenSpine"));
            Assert.Equal(new Thickness(0), spine.Margin);
        }
        finally { window.Close(); }
    }

    // 上下は左と同じだけ空けるのが基本。ただし角を大きく丸めていると、それでも
    // RootBorder の切り抜きが帯の端を削り、端ほど細く見えてしまう。
    // そのときは削られる高さまで広げて、太さの変わらない1本の線にする。
    [WpfTheory]
    [InlineData(0.0, 3.0)]
    [InlineData(1.0, 3.0)]
    [InlineData(8.0, 3.0)]
    [InlineData(16.0, 3.0)]   // 切り抜きのほうが深く、距離より広く空ける
    [InlineData(16.0, 12.0)]
    public void TitleBarHiddenSpine_ClearsTheRoundedCorner(double radius, double inset)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings();
        settings.Layout.NoteCornerRadius = radius;
        settings.Layout.TitleBarHiddenSpineInset = inset;
        var note = new StickyNote { IsTitleBarHidden = true, Content = "body" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var spine = Assert.IsType<System.Windows.Shapes.Rectangle>(window.FindName("TitleBarHiddenSpine"));

            // 帯の左辺（外枠1px + 端からの距離）で切り抜きに掛かる高さ。
            var x = 1 + inset;
            var clipped = radius <= 0 || x >= radius
                ? 0
                : System.Math.Max(0, radius - System.Math.Sqrt((radius * radius) - ((radius - x) * (radius - x))) - 1);
            var expected = System.Math.Max(inset, clipped);

            Assert.Equal(expected, spine.Margin.Top, 6);
            Assert.Equal(expected, spine.Margin.Bottom, 6);
            Assert.Equal(inset, spine.Margin.Left);
            // 上下に詰めても、太さそのものは設定どおりのまま。
            Assert.Equal(settings.Layout.TitleBarHiddenSpineWidth, spine.ActualWidth);
        }
        finally { window.Close(); }
    }

    // 本文は帯を避けて始まる。帯のすぐ隣から文字が始まると、目印ではなく
    // 本文の飾り罫のように見えてしまう。
    [WpfTheory]
    [InlineData(true, 3.0, 3.0, 14.0)]    // 端からの距離3px + 帯3px + 本来の余白8px
    [InlineData(true, 6.0, 8.0, 22.0)]
    [InlineData(false, 3.0, 3.0, 8.0)]    // 帯を出さないなら広げる理由がない
    public void NoteContentPadding_KeepsTheBodyClearOfTheSpine(
        bool showSpine, double inset, double spineWidth, double expectedLeft)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings { ShowTitleBarHiddenSpine = showSpine };
        settings.Layout.TitleBarHiddenSpineInset = inset;
        settings.Layout.TitleBarHiddenSpineWidth = spineWidth;
        var note = new StickyNote { IsTitleBarHidden = true, Content = "body" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var box = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var editor = Assert.IsType<TextBox>(window.FindName("BodyEditBox"));

            Assert.Equal(new Thickness(expectedLeft, 8, 8, 8), box.Padding);
            // 編集欄も同じ。切り替えで文字が横に跳ねると読みにくい。
            Assert.Equal(box.Padding, editor.Padding);
        }
        finally { window.Close(); }
    }

    // 画像1枚だけの付箋は、付箋を画像の額縁として使うことが多い。文字と違って
    // 余白の中で読むものではないので、帯の場所以外は詰めて画像を目一杯見せる。
    [WpfTheory]
    [InlineData(true, 9.0)]    // 端からの距離3px + 帯3px + 帯の右にも3px
    [InlineData(false, 0.0)]   // 帯が無ければ避けるものが無い
    public void NoteContentPadding_TightensForAnImageOnlyNote(bool showSpine, double expectedLeft)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var settings = new AppSettings { ShowTitleBarHiddenSpine = showSpine };
        var note = new StickyNote
        {
            Width = 400,
            Height = 300,
            IsTitleBarHidden = true,
            Content = "![](assets/pasted.png)",
        };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        SavePng(System.IO.Path.Combine(assetsDir, "pasted.png"), CreateBitmapSource());
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, settings), storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            InvokePrivate(window, "LoadContent", note.Content);
            window.UpdateLayout();
            var box = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var editor = Assert.IsType<TextBox>(window.FindName("BodyEditBox"));

            Assert.Equal(new Thickness(expectedLeft, 0, 0, 0), box.Padding);
            // FlowDocument が既定で持つ左右5pxの余白も詰める。
            Assert.Equal(new Thickness(0), box.Document.PagePadding);
            // 画像の上下の空きも、隣り合う行が無いので要らない。
            Assert.Equal(new Thickness(0), Assert.Single(EnumerateImages(box.Document)).Margin);

            // 編集中は生の Markdown 文字列なので、編集欄は詰めない。
            Assert.Equal(showSpine ? 14 : 8, editor.Padding.Left);
            Assert.Equal(8, editor.Padding.Top);
        }
        finally { window.Close(); }
    }

    // 画像の上で右クリックしても、通常の本文メニューがそのまま出る。
    // 以前は画像に専用のメニューを持たせていたので、画像の上では
    // コピーも削除もリマインダーも出せなかった。
    [WpfFact]
    public void ContentContextMenu_OverAnImage_KeepsTheOrdinaryItems()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote { Width = 400, Height = 300, Content = "![](assets/pasted.png)" };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        SavePng(System.IO.Path.Combine(assetsDir, "pasted.png"), CreateBitmapSource());
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            InvokePrivate(window, "LoadContent", note.Content);
            var box = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var image = Assert.Single(EnumerateImages(box.Document));

            // 画像そのものは自前のメニューを持たない。持つと本文のメニューが出せない。
            Assert.Null(image.ContextMenu);

            OpenContentContextMenuOver(window, box, image);

            var headers = MenuHeaders(box.ContextMenu!);
            // 画像用の項目
            Assert.Contains("画像のサイズ", headers);
            Assert.Contains("この画像に合わせて付箋のサイズを調整", headers);
            // 付箋の全画像版は引っ込む。画像1枚だと同じ動きの項目が2つ並ぶため。
            Assert.DoesNotContain("画像に合わせて付箋のサイズを調整", headers);
            Assert.Contains("付箋から画像を外す", headers);
            Assert.Contains("画像ファイルごと削除", headers);
            // 通常の項目も一緒に出る
            Assert.Contains("コピー", headers);
            Assert.Contains("すべて選択", headers);
            Assert.Contains("付箋を非表示", headers);
            Assert.Contains("付箋を削除", headers);

            // 倍率は小メニューの中。通常の項目まで並ぶと画面に収まらない。
            var sizeItem = FindMenuItem(box.ContextMenu!, "画像のサイズ");
            var percents = MenuHeaders(sizeItem);
            Assert.Contains("20%", percents);
            Assert.Contains("200%", percents);
            Assert.Contains("画像サイズを自動調整に戻す", percents);
        }
        finally { window.Close(); }
    }

    // 画像の外で開いたときは画像用の項目を出さない。押しても効かない項目が
    // 並んでいると、どれが今の右クリックに効くのか分からなくなる。
    [WpfFact]
    public void ContentContextMenu_AwayFromAnImage_HidesTheImageItems()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote { Width = 400, Height = 300, Content = "本文\n![](assets/pasted.png)" };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        SavePng(System.IO.Path.Combine(assetsDir, "pasted.png"), CreateBitmapSource());
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            InvokePrivate(window, "LoadContent", note.Content);
            var box = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var image = Assert.Single(EnumerateImages(box.Document));

            OpenContentContextMenuOver(window, box, image);
            Assert.Equal(Visibility.Visible, FindMenuItem(box.ContextMenu!, "画像のサイズ").Visibility);

            // 本文の文字の上で開き直すと引っ込む。
            OpenContentContextMenuOver(window, box, box);
            foreach (var header in new[]
                     {
                         "画像のサイズ", "この画像に合わせて付箋のサイズを調整",
                         "付箋から画像を外す", "画像ファイルごと削除",
                     })
                Assert.Equal(Visibility.Collapsed, FindMenuItem(box.ContextMenu!, header).Visibility);

            Assert.Contains("コピー", MenuHeaders(box.ContextMenu!));
        }
        finally { window.Close(); }
    }

    // 編集禁止の付箋では本文を書き換える項目を止める。大きさを変えるだけの
    // 「付箋のサイズを調整」は本文に触らないので残す。
    [WpfFact]
    public void ContentContextMenu_OverAnImage_RespectsReadOnly()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            Width = 400,
            Height = 300,
            IsReadOnly = true,
            Content = "![](assets/pasted.png)",
        };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        SavePng(System.IO.Path.Combine(assetsDir, "pasted.png"), CreateBitmapSource());
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            InvokePrivate(window, "LoadContent", note.Content);
            var box = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var image = Assert.Single(EnumerateImages(box.Document));

            OpenContentContextMenuOver(window, box, image);

            Assert.False(FindMenuItem(box.ContextMenu!, "付箋から画像を外す").IsEnabled);
            Assert.False(FindMenuItem(box.ContextMenu!, "画像ファイルごと削除").IsEnabled);
            Assert.True(FindMenuItem(box.ContextMenu!, "この画像に合わせて付箋のサイズを調整").IsEnabled);
        }
        finally { window.Close(); }
    }

    // 付箋の assets の外にある画像は、付箋から外せてもファイルは消せない。
    [WpfFact]
    public void ContentContextMenu_ForAnImageOutsideTheNote_KeepsTheFile()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        Directory.CreateDirectory(temp.Path);
        var outside = System.IO.Path.Combine(temp.Path, "outside.png");
        SavePng(outside, CreateBitmapSource());
        var note = new StickyNote { Width = 400, Height = 300, Content = $"![]({outside.Replace("\\", "/")})" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            InvokePrivate(window, "LoadContent", note.Content);
            var box = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var image = Assert.Single(EnumerateImages(box.Document));

            OpenContentContextMenuOver(window, box, image);

            Assert.True(FindMenuItem(box.ContextMenu!, "付箋から画像を外す").IsEnabled);
            Assert.False(FindMenuItem(box.ContextMenu!, "画像ファイルごと削除").IsEnabled);
        }
        finally { window.Close(); }
    }

    // キーボード（Shift+F10）から開いたときは、直前の右クリックの記憶を捨てる。
    // 残していると、関係のない場所から画像用の項目が出てしまう。
    [WpfFact]
    public void ContentContextMenu_OpenedFromTheKeyboard_ForgetsTheLastImage()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote { Width = 400, Height = 300, Content = "![](assets/pasted.png)" };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        SavePng(System.IO.Path.Combine(assetsDir, "pasted.png"), CreateBitmapSource());
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            InvokePrivate(window, "LoadContent", note.Content);
            var box = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var image = Assert.Single(EnumerateImages(box.Document));

            OpenContentContextMenuOver(window, box, image);
            Assert.Equal(Visibility.Visible, FindMenuItem(box.ContextMenu!, "画像のサイズ").Visibility);

            // マウスを経由せず、カーソル位置を持たないまま開く。
            InvokePrivate(window, "ContentBox_ContextMenuOpening", box, ContextMenuArgs(box, fromKeyboard: true));
            Assert.Equal(Visibility.Collapsed, FindMenuItem(box.ContextMenu!, "画像のサイズ").Visibility);
        }
        finally { window.Close(); }
    }

    // 右クリック → メニューを開く、の2段階をそのまま踏む。
    private static void OpenContentContextMenuOver(
        StickyNoteWindow window, RichTextBox box, DependencyObject target)
    {
        var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
        {
            RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent,
            Source = target,
        };
        InvokePrivate(window, "ContentBox_PreviewMouseRightButtonDown", box, down);
        InvokePrivate(window, "ContentBox_ContextMenuOpening", box, ContextMenuArgs(box, fromKeyboard: false));
    }

    // ContextMenuEventArgs のコンストラクタは internal なので、リフレクションで作る。
    private static ContextMenuEventArgs ContextMenuArgs(object source, bool fromKeyboard)
        => (ContextMenuEventArgs)Activator.CreateInstance(
            typeof(ContextMenuEventArgs),
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            fromKeyboard
                ? new object[] { source, true }
                : new object[] { source, true, 10.0, 10.0 },
            null)!;

    // 実際に見える項目だけ。隠してあるだけの項目も混ざると、出ている・出て
    // いないの確認にならない。
    private static IEnumerable<string> MenuHeaders(ItemsControl menu)
        => menu.Items.OfType<MenuItem>()
            .Where(item => item.Visibility == Visibility.Visible)
            .Select(item => item.Header as string ?? "");

    private static MenuItem FindMenuItem(ItemsControl menu, string header)
        => menu.Items.OfType<MenuItem>().Single(item => (item.Header as string) == header);

    // 画像だけの付箋で帯をどう置くかは設定で選べる。「並べる」だけが場所を空け、
    // 「重ねる」「出さない」は画像を縁まで広げる。
    [WpfTheory]
    [InlineData(AppSettings.ImageSpineGutter, 9.0, true)]
    [InlineData(AppSettings.ImageSpineOverlay, 0.0, true)]
    [InlineData(AppSettings.ImageSpineHidden, 0.0, false)]
    public void ImageOnlyNote_PlacesTheSpineAsConfigured(
        string style, double expectedLeft, bool spineShown)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var settings = new AppSettings { ImageOnlySpineStyle = style };
        var note = new StickyNote
        {
            Width = 400,
            Height = 300,
            IsTitleBarHidden = true,
            Content = "![](assets/pasted.png)",
        };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        SavePng(System.IO.Path.Combine(assetsDir, "pasted.png"), CreateBitmapSource());
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, settings), storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            InvokePrivate(window, "LoadContent", note.Content);
            window.UpdateLayout();
            var box = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var spine = Assert.IsType<System.Windows.Shapes.Rectangle>(window.FindName("TitleBarHiddenSpine"));
            var handle = Assert.IsType<Border>(window.FindName("TitleBarHiddenSpineHandle"));

            Assert.Equal(new Thickness(expectedLeft, 0, 0, 0), box.Padding);
            var expectedVisibility = spineShown ? Visibility.Visible : Visibility.Collapsed;
            Assert.Equal(expectedVisibility, spine.Visibility);
            // 帯を出さないなら、掴んで動かす板も一緒に消える。
            Assert.Equal(expectedVisibility, handle.Visibility);
        }
        finally { window.Close(); }
    }

    // 設定は画像だけの付箋にしか効かない。文字のある付箋の帯は消えない。
    [WpfFact]
    public void ImageOnlySpineStyle_LeavesTextNotesAlone()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings { ImageOnlySpineStyle = AppSettings.ImageSpineHidden };
        var note = new StickyNote { IsTitleBarHidden = true, Content = "本文" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var spine = Assert.IsType<System.Windows.Shapes.Rectangle>(window.FindName("TitleBarHiddenSpine"));
            var box = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));

            Assert.Equal(Visibility.Visible, spine.Visibility);
            Assert.Equal(new Thickness(14, 8, 8, 8), box.Padding);

            // 本文を画像1枚に差し替えると、そこで初めて帯が消える。
            window.ViewModel.Content = "![](assets/pasted.png)";
            window.UpdateLayout();
            Assert.Equal(Visibility.Collapsed, spine.Visibility);
        }
        finally { window.Close(); }
    }

    // 画像の前後に文字があれば普通の本文。詰めると文字が縁に貼り付く。
    [WpfFact]
    public void NoteContentPadding_StaysNormalWhenTextSurroundsTheImage()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            Width = 400,
            Height = 300,
            IsTitleBarHidden = true,
            Content = "見出し\n![](assets/pasted.png)",
        };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        SavePng(System.IO.Path.Combine(assetsDir, "pasted.png"), CreateBitmapSource());
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            InvokePrivate(window, "LoadContent", note.Content);
            window.UpdateLayout();
            var box = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));

            Assert.Equal(new Thickness(14, 8, 8, 8), box.Padding);
            Assert.Equal(new Thickness(5, 0, 5, 0), box.Document.PagePadding);
            Assert.Equal(new Thickness(0, 3, 0, 3), Assert.Single(EnumerateImages(box.Document)).Margin);
        }
        finally { window.Close(); }
    }

    // 縦スクロールバーの場所を空けるかどうか。空けたままだと右端に18px残る。
    // 縦に収まりきる画像なら空けずに済ませ、収まらない画像では空けておく
    // （空けずに広げると、後からバーが出て画像がはみ出し横スクロールまで増える）。
    [WpfTheory]
    // どちらも付箋より横に大きく、幅に合わせて縮小される画像。
    [InlineData(800, 300, false)]     // 横長。縮めても縦に収まるので右端まで使う
    [InlineData(800, 2000, true)]     // 縦長。バーが出るので場所を空ける
    public void ImageOnlyNote_ReservesScrollBarRoomOnlyWhenItNeedsIt(
        int pixelWidth, int pixelHeight, bool reservesRoom)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            Width = 400,
            Height = 300,
            IsTitleBarHidden = true,
            Content = "![](assets/pasted.png)",
        };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        SavePng(System.IO.Path.Combine(assetsDir, "pasted.png"), CreateSolidBitmapSource(pixelWidth, pixelHeight));
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            InvokePrivate(window, "LoadContent", note.Content);
            window.UpdateLayout();
            var box = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var image = Assert.Single(EnumerateImages(box.Document));

            var rightGap = box.ActualWidth - box.Padding.Left - image.Width;
            if (reservesRoom)
                Assert.Equal(18, rightGap, 1);
            else
                Assert.Equal(0, rightGap, 1);
        }
        finally { window.Close(); }
    }

    // タイトルバーを出している付箋には帯が無いので、余白は元のままでよい。
    [WpfFact]
    public void NoteContentPadding_StaysNarrowWhileTheTitleBarIsShown()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings();
        var note = new StickyNote { IsTitleBarHidden = false, Content = "body" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var box = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            Assert.Equal(new Thickness(8), box.Padding);

            // タイトルバーを隠すと帯が出るので、そのぶん本文をずらす。
            window.ViewModel.IsTitleBarHidden = true;
            window.UpdateLayout();
            Assert.Equal(new Thickness(14, 8, 8, 8), box.Padding);
        }
        finally { window.Close(); }
    }

    // 帯そのものは細すぎて狙えないので、当たり判定は下に敷いた透明な板が持つ。
    // 板は帯と同じ位置から始まり、幅リサイズの当たり判定に食われる分だけ
    // 右へ広がる。広がった先は本文の左余白（Padding 8px）までに収まる。
    [WpfTheory]
    [InlineData(3.0, 7.0)]    // 左端 4px のうち 1px がリサイズ枠。6px 掴めるまで広げる
    [InlineData(0.0, 10.0)]   // 端に寄せるほどリサイズ枠に食われ、広げる量が増える
    [InlineData(8.0, 6.0)]    // リサイズ枠の外まで離れていれば、最低限の幅で足りる
    public void TitleBarHiddenSpineHandle_LeavesRoomToGrab(double inset, double expectedWidth)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings();
        settings.Layout.TitleBarHiddenSpineInset = inset;
        var note = new StickyNote { IsTitleBarHidden = true, Content = "body" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var spine = Assert.IsType<System.Windows.Shapes.Rectangle>(window.FindName("TitleBarHiddenSpine"));
            var handle = Assert.IsType<Border>(window.FindName("TitleBarHiddenSpineHandle"));

            Assert.True(handle.IsHitTestVisible);
            Assert.False(spine.IsHitTestVisible);
            Assert.Equal(spine.Visibility, handle.Visibility);
            Assert.Equal(spine.Margin, handle.Margin);
            Assert.Equal(expectedWidth, handle.ActualWidth);
        }
        finally { window.Close(); }
    }

    // 帯を押すとタイトルバーと同じドラッグが始まり、離すと終わる。
    [WpfFact]
    public void TitleBarHiddenSpineHandle_StartsAndEndsADrag()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings();
        var note = new StickyNote { IsTitleBarHidden = true, Content = "body" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var handle = Assert.IsType<Border>(window.FindName("TitleBarHiddenSpineHandle"));

            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                Source = handle,
            };
            handle.RaiseEvent(down);
            Assert.True(down.Handled);
            Assert.True(GetPrivateField<bool>(window, "_isDragging"));
            Assert.True(GetPrivateField<bool>(window, "_isDraggingSpine"));

            var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonUpEvent,
                Source = handle,
            };
            handle.RaiseEvent(up);
            Assert.True(up.Handled);
            Assert.False(GetPrivateField<bool>(window, "_isDragging"));
            Assert.False(GetPrivateField<bool>(window, "_isDraggingSpine"));
        }
        finally { window.Close(); }
    }

    // キャプチャを横取りされたまま _isDragging が残ると、次に触れただけで動く。
    [WpfFact]
    public void TitleBarHiddenSpineHandle_DropsTheDragWhenCaptureIsLost()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings();
        var note = new StickyNote { IsTitleBarHidden = true, Content = "body" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var handle = Assert.IsType<Border>(window.FindName("TitleBarHiddenSpineHandle"));

            handle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                Source = handle,
            });
            Assert.True(GetPrivateField<bool>(window, "_isDragging"));

            handle.ReleaseMouseCapture();
            Assert.False(GetPrivateField<bool>(window, "_isDragging"));
            Assert.False(GetPrivateField<bool>(window, "_isDraggingSpine"));
        }
        finally { window.Close(); }
    }

    // 設定画面は開いている付箋へ RefreshSettings() で反映する。
    [WpfFact]
    public void TitleBarHiddenSpine_PlacementFollowsASettingsChangeWhileOpen()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings();
        var note = new StickyNote { IsTitleBarHidden = true, Content = "body" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var spine = Assert.IsType<System.Windows.Shapes.Rectangle>(window.FindName("TitleBarHiddenSpine"));
            Assert.Equal(3, spine.Margin.Left);

            settings.Layout.TitleBarHiddenSpineInset = 6;
            window.RefreshSettings();
            window.UpdateLayout();
            Assert.Equal(6, spine.Margin.Left);
            Assert.Equal(6, spine.Margin.Top);

            settings.TitleBarHiddenSpineStyle = AppSettings.SpineStyleEdge;
            window.RefreshSettings();
            window.UpdateLayout();
            Assert.Equal(new Thickness(0), spine.Margin);
        }
        finally { window.Close(); }
    }

    // 設定画面は開いている付箋へ RefreshSettings() で反映する。
    [WpfFact]
    public void TitleBarHiddenSpine_FollowsASettingsChangeWhileOpen()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings();
        var note = new StickyNote { IsTitleBarHidden = true, Content = "body" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            var spine = Assert.IsType<System.Windows.Shapes.Rectangle>(window.FindName("TitleBarHiddenSpine"));
            Assert.Equal(Visibility.Visible, spine.Visibility);
            Assert.Equal(3, spine.Width);

            settings.Layout.TitleBarHiddenSpineWidth = 6;
            window.RefreshSettings();
            window.UpdateLayout();
            Assert.Equal(6, spine.Width);

            settings.ShowTitleBarHiddenSpine = false;
            window.RefreshSettings();
            window.UpdateLayout();
            Assert.Equal(Visibility.Collapsed, spine.Visibility);
        }
        finally { window.Close(); }
    }

    // アイコン未設定なら、空の帯が本文に浮くだけなので出さない。
    [WpfFact]
    public void FoldedNote_WithoutAnIcon_ShowsNothingUntilHovered()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsFolded = true, IsTitleBarHidden = true, Icon = "" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Grid>(window.FindName("TitleBarOverlay")).Visibility);
        }
        finally { window.Close(); }
    }

    // 展開していてもアイコンは出す。タイトルバーが無いと、これが唯一の見分けになる。
    [WpfFact]
    public void UnfoldedNote_WithHiddenTitleBar_StillShowsTheIcon()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsFolded = false, IsTitleBarHidden = true, Icon = "🦊" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            Assert.Equal(Visibility.Visible,
                Assert.IsType<Grid>(window.FindName("TitleBarOverlay")).Visibility);
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<StackPanel>(window.FindName("TitleBarOverlayActions")).Visibility);
            // 枠と塗りもホバーまで出さない。
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Border>(window.FindName("TitleBarOverlayBackdrop")).Visibility);
        }
        finally { window.Close(); }
    }

    // タイトルバー側の折りたたみボタンを出さない設定でも、こちらは出す。
    // 従うと、畳む手段がダブルクリックだけになって見つけられない。
    [WpfFact]
    public void HiddenTitleBar_AlwaysOffersTheFoldButtonOnHover()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsTitleBarHidden = true, Icon = "🦊" };
        var settings = new AppSettings { ShowFoldButton = false };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            var foldButton = Assert.IsType<Button>(window.FindName("OverlayFoldButton"));
            // XAML の既定値のまま通ってしまわないよう、いったん消してから
            // 表示更新を走らせる。
            foldButton.Visibility = Visibility.Collapsed;
            InvokePrivate(window, "UpdateTitleBarButtonsVisibility");
            Assert.Equal(Visibility.Visible, foldButton.Visibility);
        }
        finally { window.Close(); }
    }

    // タイトルバーを隠した1行表示では、先頭行が見出しでも拡大せずタイトル
    // 文字サイズに揃える。見出しかどうかで畳んだ高さが変わってはいけない。
    [WpfFact]
    public void FoldedNote_WithHiddenTitleBar_IgnoresHeadingSizeOnTheFirstLine()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var settings = new AppSettings();

        double FoldedHeightOf(string content)
        {
            var note = new StickyNote
            {
                Height = 320, IsFolded = true, IsTitleBarHidden = true, Content = content,
            };
            var window = new StickyNoteWindow(new StickyNoteViewModel(note, settings), storage);
            try { return window.Height; }
            finally { window.Close(); }
        }

        var plain = FoldedHeightOf("plain first line\nsecond");
        var heading = FoldedHeightOf("# heading first line\nsecond");

        Assert.Equal(plain, heading);
    }

    // タイトルバーがある通常の付箋は、従来どおり本文ごと畳む。
    [WpfFact]
    public void FoldedNote_WithTitleBar_StillHidesTheBody()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { Height = 320, IsFolded = true, IsTitleBarHidden = false };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            var contentBox = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            Assert.Equal(Visibility.Collapsed, contentBox.Visibility);
        }
        finally { window.Close(); }
    }

    // 隠す設定では、常時表示のタイトルバーが場所ごと消えていること。
    [WpfFact]
    public void HiddenTitleBar_TakesNoLayoutSpace()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsTitleBarHidden = true };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            var titleBar = Assert.IsType<Grid>(window.FindName("TitleBar"));
            var overlay = Assert.IsType<Grid>(window.FindName("TitleBarOverlay"));
            Assert.Equal(Visibility.Collapsed, titleBar.Visibility);
            // オーバーレイはホバー中だけ出す。出しっぱなしでは本文を隠してしまう。
            Assert.Equal(Visibility.Collapsed, overlay.Visibility);
        }
        finally { window.Close(); }
    }

    // アイコンピッカーのボタンに、そのピッカーで選べない絵文字を出していると、
    // 気に入って探しても見つからない。看板はパレット収録のものに限る。
    [Fact]
    public void IconPickerButtonGlyph_IsInTheDefaultPalette()
    {
        var glyph = Assert.IsType<string>(typeof(StickyNoteWindow)
            .GetField("IconPickerGlyph", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetRawConstantValue());
        Assert.Contains(glyph, AppSettings.DefaultIconPalette());
    }

    // 閉じた付箋を開いた表示の高さで作ってから Loaded で縮めていたため、
    // 起動時に縦長の枠が一瞬見えていた。Show() の前、つまり構築した時点で
    // 既に閉じた高さになっていることを確かめる。
    [WpfFact]
    public void FoldedNote_HasItsFoldedHeightBeforeBeingShown()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings();
        var note = new StickyNote { Width = 260, Height = 320, IsFolded = true };
        var vm = new StickyNoteViewModel(note, settings);
        var window = new StickyNoteWindow(vm, new StorageService(temp.Path));
        try
        {
            var expected = vm.TitleBarHeight + settings.Layout.RootBorderThickness * 2;
            Assert.Equal(expected, window.Height);
            Assert.NotEqual(note.Height, window.Height);
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<RichTextBox>(window.FindName("ContentBox")).Visibility);
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void UnfoldedNote_KeepsItsSavedHeightBeforeBeingShown()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { Width = 260, Height = 320, IsFolded = false };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            Assert.Equal(320, window.Height);
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void ToggleFold_ReadOnlyImageWithoutWidth_KeepsNaturalSizeAfterUnfold()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            Width = 420,
            FoldedWidth = 180,
            Height = 320,
            IsReadOnly = true,
            IsFolded = true,
            Content = "![image](assets/pasted.png)",
        };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        SavePng(System.IO.Path.Combine(assetsDir, "pasted.png"), CreateBitmapSource());
        var vm = new StickyNoteViewModel(note, new AppSettings { EnableFoldAnimation = false });
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            InvokePrivate(window, "LoadContent", note.Content);
            var contentBox = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var foldedImage = Assert.Single(EnumerateImages(contentBox.Document));

            InvokePrivate(window, "ToggleFold", (object?)null);
            var unfoldedImage = Assert.Single(EnumerateImages(contentBox.Document));

            Assert.Equal(2 / VisualTreeHelper.GetDpi(window).DpiScaleX, foldedImage.Width, 8);
            Assert.Equal(2 / VisualTreeHelper.GetDpi(window).DpiScaleX, unfoldedImage.Width, 8);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void SaveCurrentPositionToModel_WhenPositionSeparated_DoesNotResyncOtherState()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            X = 100,
            Y = 110,
            FoldedX = 10,
            FoldedY = 20,
            IsPositionSeparated = true,
        };
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Left = 240;
            window.Top = 260;

            InvokePrivate(window, "SaveCurrentPositionToModel");

            Assert.Equal(240, note.X);
            Assert.Equal(260, note.Y);
            Assert.Equal(10, note.FoldedX);
            Assert.Equal(20, note.FoldedY);
            Assert.True(note.IsPositionSeparated);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void SaveCurrentPositionToModel_WhenDragSeparatesPosition_EntersSeparatedState()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote { X = 100, Y = 110, FoldedX = 10, FoldedY = 20 };
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Left = 240;
            window.Top = 260;
            SetPrivateField(window, "_dragSeparatesFoldedPosition", true);

            InvokePrivate(window, "SaveCurrentPositionToModel");

            Assert.True(note.IsPositionSeparated);
            Assert.Equal(10, note.FoldedX);
            Assert.Equal(20, note.FoldedY);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void MarkPositionSeparatedIfOpenViewMovedAwayFromClosedView_WhenClosedPositionDiffers_EntersSeparatedState()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            X = 100,
            Y = 110,
            FoldedX = 10,
            FoldedY = 20,
            IsPositionSeparated = false,
        };
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Left = 240;
            window.Top = 260;

            InvokePrivate(window, "MarkPositionSeparatedIfOpenViewMovedAwayFromClosedView");

            Assert.True(note.IsPositionSeparated);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void ResetPositionSeparation_WhileUnfolded_ReconnectsToTitleBarPosition()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            X = 100,
            Y = 110,
            FoldedX = 10,
            FoldedY = 20,
            IsPositionSeparated = true,
        };
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Left = 240;
            window.Top = 260;

            InvokePrivate(window, "ResetPositionSeparation");

            Assert.False(note.IsPositionSeparated);
            Assert.Equal(10, window.Left);
            Assert.Equal(20, window.Top);
            Assert.Equal(10, note.X);
            Assert.Equal(20, note.Y);
            Assert.Equal(10, note.FoldedX);
            Assert.Equal(20, note.FoldedY);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void ToggleFold_ToClosedView_AppliesClosedViewBounds()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            X = 240,
            Y = 260,
            Width = 420,
            Height = 320,
            FoldedX = 10,
            FoldedY = 20,
            FoldedWidth = 180,
        };
        var vm = new StickyNoteViewModel(note, new AppSettings { EnableFoldAnimation = false });
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Left = note.X;
            window.Top = note.Y;
            window.Width = note.Width;
            window.Height = note.Height;

            InvokePrivate(window, "ToggleFold", (object?)null);

            Assert.True(note.IsFolded);
            Assert.Equal(10, window.Left);
            Assert.Equal(20, window.Top);
            Assert.Equal(180, window.Width);
            Assert.Equal(10, note.FoldedX);
            Assert.Equal(20, note.FoldedY);
            Assert.Equal(180, note.FoldedWidth);
            Assert.Equal(320, note.Height);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void ShouldToggleView_Default_TogglesOnSecondMouseDown()
    {
        EnsureApplication();
        App.Current.Settings.DoubleClickToToggleView = true;
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(new StickyNote(), new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            Assert.False((bool)InvokePrivateWithResult(window, "ShouldToggleViewOnMouseDown", 1)!);
            Assert.True((bool)InvokePrivateWithResult(window, "ShouldToggleViewOnMouseDown", 2)!);
            Assert.False((bool)InvokePrivateWithResult(window, "ShouldToggleViewOnMouseUp", 1)!);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void ShouldToggleViewForClick_WhenSingleClickConfigured_UsesSingleClick()
    {
        EnsureApplication();
        App.Current.Settings.DoubleClickToToggleView = false;
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(new StickyNote(), new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            Assert.False((bool)InvokePrivateWithResult(window, "ShouldToggleViewOnMouseDown", 2)!);
            Assert.True((bool)InvokePrivateWithResult(window, "ShouldToggleViewOnMouseUp", 1)!);
            Assert.False((bool)InvokePrivateWithResult(window, "ShouldToggleViewOnMouseUp", 2)!);
        }
        finally
        {
            App.Current.Settings.DoubleClickToToggleView = true;
            window.Close();
        }
    }

    [WpfFact]
    public void CanAcceptNoteContent_RejectsNewContentOverConfiguredLimit()
    {
        EnsureApplication();
        App.Current.Settings.MaxNoteContentBytes = 12;
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(new StickyNote { Content = "short" }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            Assert.True((bool)InvokePrivateWithResult(window, "CanAcceptNoteContent", "123456789012")!);
            Assert.False((bool)InvokePrivateWithResult(window, "CanAcceptNoteContent", "1234567890123")!);
        }
        finally
        {
            App.Current.Settings.MaxNoteContentBytes = 1024 * 1024;
            window.Close();
        }
    }

    [WpfFact]
    public void CanAcceptNoteContent_AllowsShrinkingExistingOversizedContent()
    {
        EnsureApplication();
        App.Current.Settings.MaxNoteContentBytes = 12;
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(new StickyNote { Content = "123456789012345" }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            Assert.True((bool)InvokePrivateWithResult(window, "CanAcceptNoteContent", "12345678901234")!);
            Assert.False((bool)InvokePrivateWithResult(window, "CanAcceptNoteContent", "1234567890123456")!);
        }
        finally
        {
            App.Current.Settings.MaxNoteContentBytes = 1024 * 1024;
            window.Close();
        }
    }

    private static void InvokePrivate(object target, string methodName)
        => InvokePrivate(target, methodName, []);

    private static void InvokePrivate(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(target, args);
    }

    private static T GetPrivateProperty<T>(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return (T)property.GetValue(target)!;
    }

    private static object? InvokePrivateWithResult(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(target, args);
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(target, value);
    }

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field.GetValue(target));
    }

    private static object GetOnlyMarkdownImageContext(StickyNoteWindow window)
    {
        var contexts = (System.Collections.IDictionary)window.GetType()
            .GetField("_markdownImageContexts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;
        return Assert.Single(contexts.Values.Cast<object>());
    }

    private static System.Windows.Media.Imaging.BitmapSource CreateBitmapSource()
    {
        var pixels = new byte[]
        {
            0, 0, 255, 255,
            0, 255, 0, 255,
            255, 0, 0, 255,
            255, 255, 255, 255,
        };
        return System.Windows.Media.Imaging.BitmapSource.Create(
            2,
            2,
            96,
            96,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            pixels,
            8);
    }

    // 付箋より大きく引き伸ばされる画像。縮小の掛かり方を見るテストで使う。
    private static System.Windows.Media.Imaging.BitmapSource CreateSolidBitmapSource(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = 255;
        return System.Windows.Media.Imaging.BitmapSource.Create(
            width, height, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);
    }

    private static void SavePng(string path, System.Windows.Media.Imaging.BitmapSource bitmap)
    {
        using var stream = File.Create(path);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static IEnumerable<Image> EnumerateImages(FlowDocument document)
    {
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                foreach (var embeddedImage in EnumerateImages(paragraph.Inlines))
                    yield return embeddedImage;
            }
        }
    }

    private static IEnumerable<Image> EnumerateImages(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is InlineUIContainer { Child: Image embeddedImage })
                yield return embeddedImage;
            else if (inline is Span span)
            {
                foreach (var nestedImage in EnumerateImages(span.Inlines))
                    yield return nestedImage;
            }
        }
    }

    private static void EnsureApplication() => WpfApplicationFixture.Ensure();

    private sealed class TempDataDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ScreenPinNotes.Tests",
            Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
