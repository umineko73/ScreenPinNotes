using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using System.Reflection;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.ViewModels;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

public class StickyNoteWindowTests
{
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
            Assert.Equal(initiallyFolded ? 90 : 180, window.Left, 1);
            Assert.Equal(initiallyFolded ? 100 : 180, window.Top, 1);
            Assert.Equal(initiallyFolded ? 230 : 420, window.Width, 1);
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
            var root = (UIElement)window.FindName(hidden ? "TitleBarOverlay" : "TitleBar");
            InvokePrivate(window, "TitleBar_MouseLeftButtonUp", root,
                new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left));
            Assert.Equal(separated, note.IsPositionSeparated);
            Assert.Equal(folded && separated ? 180 : 280, note.X);
            Assert.Equal(folded && separated ? 180 : 290, note.Y);
            Assert.Equal(!folded && separated ? 180 : 280, note.FoldedX);
            Assert.Equal(!folded && separated ? 180 : 290, note.FoldedY);
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
                Assert.Equal(note.IsFolded ? (separated ? 90 : 180) : 180, window.Left, 1);
                Assert.Equal(note.IsFolded ? (separated ? 100 : 180) : 180, window.Top, 1);
                Assert.Equal(note.IsFolded ? 230 : 420, window.Width, 1);
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
            if (atBottom) Assert.True(end.Y <= window.PointToScreen(new Point()).Y + 1);
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

            Assert.Equal(2, image.Width);
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

            Assert.Equal("![image](assets/pasted.png){width=4}", note.Content);
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

            Assert.Equal(2, foldedImage.Width);
            Assert.Equal(2, unfoldedImage.Width);
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
