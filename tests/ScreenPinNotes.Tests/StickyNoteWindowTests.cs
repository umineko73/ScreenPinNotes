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
    [InlineData(false)]
    [InlineData(true)]
    public void UnfoldAfterManualWidthChangePreservesExpandedBoundsDuringReentrantLayout(bool hidden)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { Width = 420, Height = 320, IsFolded = true,
            IsTitleBarHidden = hidden, Content = "body" };
        var vm = new StickyNoteViewModel(note, App.Current.Settings);
        var window = new StickyNoteWindow(vm, new StorageService(temp.Path));
        try
        {
            window.Show(); window.UpdateLayout();
            note.ManualFoldedWidth = 230;
            window.Width = 230;
            window.UpdateLayout();
            // Binding/activation can force layout before the transition has
            // restored its destination bounds. Deliver a pending size change then.
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.IsFolded) && !vm.IsFolded)
                {
                    window.Width = 231;
                    window.UpdateLayout();
                }
            };
            InvokePrivate(window, "ToggleFold", (object?)null);
            InvokePrivate(window, "CompleteFoldAnimation");
            window.UpdateLayout();
            Assert.Equal(420, note.Width);
            Assert.Equal(320, note.Height);
            Assert.Equal(420, window.Width);
            Assert.Equal(320, window.Height);
        }
        finally { window.Close(); }
    }

    [WpfFact]
    public void MouseActivationSuppressesTitleToggleAndConsumesActivationMarker()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var window = new StickyNoteWindow(new StickyNoteViewModel(new StickyNote(), App.Current.Settings), new StorageService(temp.Path));
        var settings = App.Current.Settings;
        var previous = settings.DoubleClickToToggleView;
        try
        {
            settings.DoubleClickToToggleView = false;
            window.Show();
            var args = new object?[] { IntPtr.Zero, 0x0021, IntPtr.Zero, new IntPtr(0x02010001), false };
            typeof(StickyNoteWindow).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
            InvokePrivate(window, "Window_PreviewMouseDown", window,
                new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
            Assert.False((bool)typeof(StickyNoteWindow).GetMethod("ShouldToggleViewOnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { 1 })!);
            // The activation marker is consumed by this gesture, even if the
            // click lands on a button instead of the title drag surface.
            Assert.False(GetPrivateField<bool>(window, "_mouseActivating"));
        }
        finally { settings.DoubleClickToToggleView = previous; window.Close(); }
    }

    /// <summary>
    /// 畳んだ付箋は開くために触る。前面化のためのクリックを別に求めると、
    /// 裏にある付箋を開くのに2回クリックが要っていた。
    /// </summary>
    [WpfFact]
    public void MouseActivationStillOpensAFoldedNote()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var window = new StickyNoteWindow(new StickyNoteViewModel(new StickyNote { IsFolded = true }, App.Current.Settings), new StorageService(temp.Path));
        var settings = App.Current.Settings;
        var previous = settings.DoubleClickToToggleView;
        try
        {
            settings.DoubleClickToToggleView = false;
            window.Show();
            var args = new object?[] { IntPtr.Zero, 0x0021, IntPtr.Zero, new IntPtr(0x02010001), false };
            typeof(StickyNoteWindow).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
            InvokePrivate(window, "Window_PreviewMouseDown", window,
                new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
            Assert.True((bool)typeof(StickyNoteWindow).GetMethod("ShouldToggleViewOnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { 1 })!);
            Assert.False(GetPrivateField<bool>(window, "_mouseActivating"));
        }
        finally { settings.DoubleClickToToggleView = previous; window.Close(); }
    }

    /// <summary>
    /// タイトルバーのボタンはウィンドウ枠の中でもクリックを受ける。右端に寄せすぎると
    /// リサイズ枠に重なり、右端をつかめる所がほとんど残らなかった。
    /// </summary>
    [WpfFact]
    public void TitleBarButtonsStayClearOfTheResizeBorder()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = App.Current.Settings;
        var previousBorder = settings.Layout.ResizeBorder;
        var previousFoldButton = settings.ShowFoldButton;
        var window = new StickyNoteWindow(new StickyNoteViewModel(new StickyNote { Width = 300, Height = 200 }, settings), new StorageService(temp.Path));
        double GapRightOfFoldButton()
        {
            var titleBar = (Grid)window.FindName("TitleBar");
            var fold = (Button)window.FindName("FoldButton");
            fold.Visibility = Visibility.Visible; // the buttons otherwise appear on hover
            window.UpdateLayout();
            return titleBar.ActualWidth - fold.TranslatePoint(new Point(fold.ActualWidth, 0), titleBar).X;
        }
        try
        {
            settings.ShowFoldButton = true;
            window.Show();
            window.RefreshSettings();
            Assert.True(GapRightOfFoldButton() > settings.Layout.ResizeBorder);

            settings.Layout.ResizeBorder = 9;
            window.RefreshSettings();
            Assert.True(GapRightOfFoldButton() > 9);
        }
        finally
        {
            settings.Layout.ResizeBorder = previousBorder;
            settings.ShowFoldButton = previousFoldButton;
            window.Close();
        }
    }

    [WpfFact]
    public void FoldedOverlayDoesNotReserveHiddenContentScrollbarSpace()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { Width = 400, Height = 300, IsTitleBarHidden = true,
            Content = string.Join("\n\n", Enumerable.Repeat("本文", 100)) };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), new StorageService(temp.Path));
        try
        {
            window.Show(); window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            InvokePrivate(window, "UpdateTitleBarOverlayOffset");
            var overlay = (Grid)window.FindName("TitleBarOverlay");
            Assert.True(overlay.Margin.Right > 4);

            InvokePrivate(window, "ToggleFold", (object?)null);
            InvokePrivate(window, "CompleteFoldAnimation");
            window.UpdateLayout();
            Assert.Equal(4, overlay.Margin.Right);

            InvokePrivate(window, "ToggleFold", (object?)null);
            InvokePrivate(window, "CompleteFoldAnimation");
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(overlay.Margin.Right > 4);
        }
        finally { window.Close(); }
    }

    [WpfTheory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void FitImageIsBlockedDuringEditingAndAvailableAfterwards(bool titleOnly, bool hiddenTitle)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote { Width = 400, Height = 320, EditWidth = 500, EditHeight = 420,
            IsTitleBarHidden = hiddenTitle, Content = "![](assets/test.png)" };
        var assets = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assets);
        SavePng(System.IO.Path.Combine(assets, "test.png"), CreateSolidBitmapSource(120, 90));
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), storage);
        try
        {
            window.Show(); window.UpdateLayout();
            InvokePrivate(window, titleOnly ? "EnterTitleEditMode" : "EnterEditMode");
            window.UpdateLayout();
            var contexts = (System.Collections.IDictionary)typeof(StickyNoteWindow).GetField("_markdownImageContexts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var image = contexts.Keys.Cast<Image>().Single();
            var context = contexts[image]!;
            InvokePrivate(window, "CaptureContextMenuImage", image);
            InvokePrivate(window, "UpdateImageMenuItems", false);
            var item = GetPrivateField<MenuItem>(window, "_fitWindowToImageItem");
            Assert.False(item.IsEnabled);
            // UI 以外から呼ばれた場合も閲覧・編集の保存領域を変更しない。
            InvokePrivate(window, "FitWindowToMarkdownImage", context);
            Assert.Equal((400d, 320d), (note.Width, note.Height));
            Assert.Equal((500d, 420d), (note.EditWidth!.Value, note.EditHeight!.Value));
            InvokePrivate(window, "SaveNote");
            var saved = Assert.Single(storage.Load());
            Assert.Equal((400d, 320d), (saved.Width, saved.Height));
            InvokePrivate(window, "EnterViewMode");
            window.UpdateLayout();
            contexts = (System.Collections.IDictionary)typeof(StickyNoteWindow).GetField("_markdownImageContexts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            image = contexts.Keys.Cast<Image>().Single();
            InvokePrivate(window, "CaptureContextMenuImage", image);
            InvokePrivate(window, "UpdateImageMenuItems", false);
            Assert.True(item.IsEnabled);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(note.Height < 320);
            Assert.Equal((500d, 420d), (note.EditWidth!.Value, note.EditHeight!.Value));
        }
        finally { window.Close(); }
    }

    [WpfTheory]
    [InlineData(800, 1600)]
    [InlineData(40, 80)]
    public async Task UnspecifiedImageFitsHeightAndFollowsHeightOnlyResize(int pixelWidth, int pixelHeight)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote { Width = 400, Height = 240, IsTitleBarHidden = true,
            Content = "![image](assets/tall.png)" };
        var assets = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assets);
        SavePng(System.IO.Path.Combine(assets, "tall.png"), CreateSolidBitmapSource(pixelWidth, pixelHeight));
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), storage);
        try
        {
            window.Show();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var box = (RichTextBox)window.FindName("ContentBox");
            var first = Assert.Single(EnumerateImages(box.Document));
            Assert.InRange(first.Height, 1, box.ActualHeight);
            Assert.Equal(2, first.Height / first.Width, 6);
            if (pixelHeight == 80)
                Assert.True(first.Height > pixelHeight / VisualTreeHelper.GetDpi(window).DpiScaleY);
            var firstHeight = first.Height;
            window.Height = 160;
            window.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var resized = Assert.Single(EnumerateImages(box.Document));
            Assert.True(resized.Height < firstHeight);
            Assert.InRange(resized.Height, 1, box.ActualHeight);
            Assert.Equal(2, resized.Height / resized.Width, 6);
            Assert.Equal("![image](assets/tall.png)", note.Content);
        }
        finally { window.Close(); }
    }

    [WpfTheory]
    [InlineData(false, false, -1)]
    [InlineData(false, false, 1)]
    [InlineData(false, true, -1)]
    [InlineData(false, true, 1)]
    // Manual folded resizing is covered by ManualFoldedWidthSurvivesContentChangesAndReload.
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
            if (initiallyFolded) Assert.InRange(window.Width, window.MinWidth, 420);
            else AssertWindowCoordinate(420, window.Width, window);
            Assert.Equal(180, note.X);
            Assert.Equal(180, note.Y);
            Assert.Equal(90, note.FoldedX);
            Assert.Equal(100, note.FoldedY);
            Assert.Equal(420, note.Width);
            Assert.InRange(note.FoldedWidth!.Value, window.MinWidth, 420);
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
            Assert.Equal(InputMethodState.DoNotCare, InputMethod.GetPreferredImeState(editor));
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
                if (note.IsFolded) Assert.InRange(window.Width, window.MinWidth, 420);
                else AssertWindowCoordinate(420, window.Width, window);
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
            // 枠だけでなく付箋全体を塗って点滅させる。本文が透けるよう半透明にする。
            var fill = Assert.IsType<SolidColorBrush>(overlay.Background);
            Assert.InRange(fill.Color.A, 1, 254);
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
                Assert.Equal(Visibility.Visible, ((Border)window.FindName("FoldedPreviewHost")).Visibility);
                Assert.EndsWith("image.png", ((TextBlock)window.FindName("FoldedPreviewText")).Text.Trim());
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
            Assert.Equal(Visibility.Visible, ((Border)window.FindName("FoldedPreviewHost")).Visibility);
            Assert.Equal(firstLineHeight, window.ActualHeight, 1);
            Assert.Equal(window.ActualHeight, window.MinHeight, 1);
            Assert.Equal(window.ActualHeight, window.MaxHeight, 1);
            Assert.Equal(window.ViewModel.FontSize, content.FontSize);
            Assert.Equal(window.ViewModel.TitleFontSize, ((TextBlock)window.FindName("FoldedPreviewText")).FontSize);
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
                // 付箋そのものを扱う項目（複製・非表示・削除）は区切り線の下にまとめる。
                var duplicate = items.Single(i => Equals(i.Header, LocalizationService.T("DuplicateNote")));
                var hide = items.Single(i => Equals(i.Header, LocalizationService.T("HideNote")));
                Assert.IsType<Separator>(menu.Items[menu.Items.IndexOf(duplicate) - 1]);
                Assert.Same(hide, menu.Items[menu.Items.IndexOf(duplicate) + 1]);
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

    // リストの行で Tab を押すと行ごと字下げされ、Ctrl+Z 1回で戻る。
    // ふつうの行では、これまでどおりタブ文字が入る。
    [WpfFact]
    public void BodyEditor_TabIndentsListLinesAndStillTypesTabsElsewhere()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { Content = "- parent\n- child\nplain" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            typeof(StickyNoteWindow).GetMethod("EnterEditMode", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            var body = (TextBox)window.FindName("BodyEditBox");
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var normalized = body.Text.Replace("\r\n", "\n");
            var childStart = body.Text.IndexOf("- child", StringComparison.Ordinal);

            body.Select(childStart + 2, 0);
            Assert.True(PressTab(body));
            Assert.Equal("- parent\n\t- child\nplain", body.Text.Replace("\r\n", "\n"));
            Assert.Equal(childStart + 3, body.SelectionStart);
            Assert.Equal("- parent\n\t- child\nplain", note.Content);

            body.Undo();
            Assert.Equal(normalized, body.Text.Replace("\r\n", "\n"));

            body.Select(body.Text.IndexOf("plain", StringComparison.Ordinal), 0);
            Assert.False(PressTab(body));
        }
        finally { window.Close(); }

        static bool PressTab(TextBox box) => Press(box, Key.Tab);
    }

    // リストの行で Enter を押すと次の項目の印が入り、空の項目ではリストを抜ける。
    [WpfFact]
    public void BodyEditor_EnterContinuesAndEndsLists()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { Content = "1. first" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            typeof(StickyNoteWindow).GetMethod("EnterEditMode", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            var body = (TextBox)window.FindName("BodyEditBox");
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            body.Select(body.Text.Length, 0);
            Assert.True(Press(body, Key.Enter));
            Assert.Equal("1. first\n2. ", note.Content);
            Assert.Equal(body.Text.Length, body.CaretIndex);

            Assert.True(Press(body, Key.Enter));
            Assert.Equal("1. first\n", note.Content);

            // 空行ではふつうの改行に任せる。
            Assert.False(Press(body, Key.Enter));
        }
        finally { window.Close(); }
    }

    private static bool Press(TextBox box, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box), 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        box.RaiseEvent(args);
        return args.Handled;
    }

    // 選ばずにタイトル全体を写す項目は、そうと分かる名前にする。
    // タイトル編集中は選んだ部分を写すふつうのコピー。
    [WpfTheory]
    [InlineData("ja", "タイトルをコピー", "コピー")]
    [InlineData("en", "Copy title", "Copy")]
    public void TitleMenu_NamesTheWholeTitleCopy(string language, string viewing, string editing)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        using var temp = new TempDataDirectory();
        var previousLanguage = app.Settings.Language;
        app.Settings.Language = language;
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(new StickyNote { Title = "Plan" }, new AppSettings { Language = language }),
            new StorageService(temp.Path));
        try
        {
            window.Show();
            var menu = ((FrameworkElement)window.FindName("TitleText")).ContextMenu;
            // タイトル編集・切り取りの次。
            var copy = menu.Items.OfType<MenuItem>().ElementAt(2);

            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Assert.Equal(viewing, copy.Header);
            Assert.True(copy.IsEnabled);
            copy.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal("Plan", System.Windows.Clipboard.GetText());

            typeof(StickyNoteWindow).GetMethod("EnterEditMode", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            var titleEditBox = (TextBox)window.FindName("TitleEditBox");
            menu.PlacementTarget = titleEditBox;
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Assert.Equal(editing, copy.Header);
        }
        finally
        {
            window.Close();
            app.Settings.Language = previousLanguage;
        }
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
            string Text() => ((TextBlock)window.FindName("FoldedPreviewText")).Text.Trim();
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
        // 畳んだ1行表示は画像ではなく画像パスの文字なので、本文の余白は
        // 文字のときと同じ。アイコンの有無で省略の仕方が変わるだけの幅を
        // 残すため、帯のぶんの余白を足しておく。
        var note = new StickyNote { Content = $"![写真]({path})", IsFolded = true, IsTitleBarHidden = true, Width = 181 };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            InvokePrivate(window, "LoadContent", note.Content);
            var box = (RichTextBox)window.FindName("ContentBox");
            string Text() => ((TextBlock)window.FindName("FoldedPreviewText")).Text.Trim();
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
            Assert.Equal(Visibility.Collapsed, box.Visibility);
            Assert.Equal(hiddenTitleBar ? Visibility.Visible : Visibility.Collapsed,
                ((Border)window.FindName("FoldedPreviewHost")).Visibility);
            if (hiddenTitleBar)
            {
                var run = (TextBlock)window.FindName("FoldedPreviewText");
                Assert.Equal("操作ヘルプ 斜体 取消 code リンク", run.Text);
                Assert.Equal(FontWeights.Normal, run.FontWeight);
                Assert.Equal(FontStyles.Normal, run.FontStyle);
                Assert.True(run.TextDecorations == null || run.TextDecorations.Count == 0);
                Assert.True(run.ActualHeight > 0);
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
            var foldedWidthBeforeEdit = model.FoldedWidth;

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
            Assert.Equal(foldedWidthBeforeEdit, model.FoldedWidth);
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
    public void ReloadExternalContent_NonTailMode_PreservesScrollAndCaretPosition()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        Directory.CreateDirectory(temp.Path);
        var storage = new StorageService(temp.Path);
        var externalPath = Path.Combine(temp.Path, "notes.md");
        var lines = Enumerable.Range(1, 200).Select(i => $"line {i}").ToArray();
        File.WriteAllText(externalPath, string.Join('\n', lines));
        var note = new StickyNote
        {
            Content = string.Join('\n', lines),
            ExternalContentPath = externalPath,
            IsReadOnly = true,
            Width = 260,
            Height = 220,
        };
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            var content = (RichTextBox)window.FindName("ContentBox")!;

            content.ScrollToVerticalOffset(300);
            window.UpdateLayout();
            var scrolledOffset = content.VerticalOffset;
            // Sanity check: the note must actually overflow for this test to mean anything.
            Assert.True(scrolledOffset > 0);

            var totalLength = new TextRange(content.Document.ContentStart, content.Document.ContentEnd).Text.Length;
            content.CaretPosition = content.Document.ContentStart.GetPositionAtOffset(totalLength / 2) ?? content.CaretPosition;
            var caretOffsetBefore = content.Document.ContentStart.GetOffsetToPosition(content.CaretPosition);

            // A watcher-driven reload (appending a line at the very end) should not
            // reset where the reader was looking.
            File.AppendAllText(externalPath, "\nline 201");
            Task.Run(window.ReloadExternalContent).GetAwaiter().GetResult();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();

            Assert.Contains("line 201", vm.Content);
            Assert.Equal(scrolledOffset, content.VerticalOffset, 1);
            Assert.Equal(caretOffsetBefore, content.Document.ContentStart.GetOffsetToPosition(content.CaretPosition));
        }
        finally
        {
            window.Close();
        }
    }

    // 濃い色の付箋＋ライトテーマ（およびその逆）で、メニューの文字が地の色と
    // 同系色になって読めなくなっていた。地がテーマで決まる以上、その上の文字も
    // テーマで決めるという取り決めを、実際のコントラストで固定しておく。
    [WpfTheory]
    [InlineData("Light", "dark-charcoal")]
    [InlineData("Light", "yellow")]
    [InlineData("Dark", "yellow")]
    [InlineData("Dark", "dark-charcoal")]
    public void ContextMenuColors_ContrastWithTheirOwnBackground(string theme, string colorKey)
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var previousTheme = app.Settings.Theme;
        using var temp = new TempDataDirectory();
        app.Settings.Theme = theme;
        var vm = new StickyNoteViewModel(new StickyNote { ColorKey = colorKey, Content = "body" }, app.Settings);
        var window = new StickyNoteWindow(vm, new StorageService(temp.Path));
        try
        {
            var menus = new[]
            {
                ((RichTextBox)window.FindName("ContentBox")!).ContextMenu!,
                ((TextBox)window.FindName("BodyEditBox")!).ContextMenu!,
                ((TextBlock)window.FindName("TitleText")!).ContextMenu!,
            };
            foreach (var menu in menus)
            {
                AssertReadable((SolidColorBrush)menu.Background, (SolidColorBrush)menu.Foreground);

                // メニュー先頭に差し込む文字サイズ・色のクイック操作行も同じ地の上にある。
                var quickRow = Assert.IsType<Border>(menu.Tag);
                AssertReadable(
                    (SolidColorBrush)quickRow.Background,
                    (SolidColorBrush)TextElement.GetForeground(quickRow));
            }
        }
        finally
        {
            window.Close();
            app.Settings.Theme = previousTheme;
        }

        static void AssertReadable(SolidColorBrush background, SolidColorBrush foreground)
        {
            static double Brightness(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
            Assert.True(
                Math.Abs(Brightness(background.Color) - Brightness(foreground.Color)) > 0.5,
                $"text {foreground.Color} is not readable on {background.Color}");
        }
    }

    // 変更を確かめている間はタイトル右端の丸が動き、止まったら透明になる。
    [WpfFact]
    public void ExternalNote_ShowsTheCheckingIndicatorOnlyWhileChecking()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var external = app.Settings.ExternalFile;
        var (previousInterval, previousStop) = (external.PollIntervalMs, external.PollStopAfterMs);
        using var temp = new TempDataDirectory();
        Directory.CreateDirectory(temp.Path);
        var path = Path.Combine(temp.Path, "app.log");
        File.WriteAllText(path, "line\n");
        external.PollIntervalMs = 200;
        external.PollStopAfterMs = 1000;
        var vm = new StickyNoteViewModel(
            new StickyNote { Content = "line\n", ExternalContentPath = path, ExternalTailMode = true, IsReadOnly = true }, app.Settings);
        var window = new StickyNoteWindow(vm, new StorageService(temp.Path));
        try
        {
            window.Show();
            var dot = (System.Windows.Shapes.Ellipse)window.FindName("ExternalPollingIndicator");
            Assert.Equal(Visibility.Visible, dot.Visibility);

            Assert.True(PumpUntil(window, () => vm.IsExternalPolling && dot.HasAnimatedProperties, 3000));
            Assert.Contains("●", vm.TitleIconTooltip);

            Assert.True(PumpUntil(window, () => !vm.IsExternalPolling, 5000));
            Assert.False(dot.HasAnimatedProperties);
            Assert.Equal(0, dot.Opacity);
            Assert.Equal(Visibility.Visible, dot.Visibility);
        }
        finally
        {
            window.Close();
            (external.PollIntervalMs, external.PollStopAfterMs) = (previousInterval, previousStop);
        }

        static bool PumpUntil(Window window, Func<bool> condition, int timeoutMs)
        {
            var until = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < until)
            {
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                if (condition()) return true;
                Thread.Sleep(20);
            }
            return condition();
        }
    }

    [WpfFact]
    public void ReloadExternalContent_WhenNoteIsInactive_FlashesTheUpdateBorder()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        Directory.CreateDirectory(temp.Path);
        var storage = new StorageService(temp.Path);
        var externalPath = Path.Combine(temp.Path, "app.log");
        File.WriteAllText(externalPath, "first line\n");
        var vm = new StickyNoteViewModel(
            new StickyNote { Content = "first line\n", ExternalContentPath = externalPath, IsReadOnly = true },
            new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        var foreground = new Window { Width = 120, Height = 120, ShowInTaskbar = false };
        try
        {
            window.Show();
            foreground.Show();
            foreground.Activate();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var border = (Border)window.FindName("UpdateFlashBorder")!;
            Assert.False(window.IsActive);
            Assert.False(border.HasAnimatedProperties);

            File.AppendAllText(externalPath, "second line\n");
            Task.Run(window.ReloadExternalContent).GetAwaiter().GetResult();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.Contains("second line", vm.Content);
            Assert.True(border.HasAnimatedProperties);
        }
        finally
        {
            foreground.Close();
            window.Close();
        }
    }

    // ERROR / FATAL は背景色を赤へ寄せて点滅させる。ふつうの更新は枠だけ。
    [WpfFact]
    public void FlashForExternalUpdate_TintsTheBackgroundOnlyForErrors()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var vm = new StickyNoteViewModel(new StickyNote { Content = "log" }, new AppSettings());
        var window = new StickyNoteWindow(vm, new StorageService(temp.Path));
        var foreground = new Window { Width = 120, Height = 120, ShowInTaskbar = false };
        try
        {
            window.Show();
            foreground.Show();
            foreground.Activate();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var border = (Border)window.FindName("UpdateFlashBorder")!;
            var root = (Border)window.FindName("RootBorder")!;
            var titleBar = (Grid)window.FindName("TitleBar")!;

            window.FlashForExternalUpdate(hasError: false);
            Assert.True(border.HasAnimatedProperties);
            Assert.False(root.HasAnimatedProperties);
            Assert.False(titleBar.HasAnimatedProperties);

            // 明滅の途中でエラーが届いたら、背景とタイトルバーの色も赤へ寄せる。
            // 上に色を重ねないので、枠の塗りは無いまま。
            window.FlashForExternalUpdate(hasError: true);
            Assert.True(root.HasAnimatedProperties);
            Assert.True(titleBar.HasAnimatedProperties);
            Assert.Null(border.Background);

            // 止めればバインディングの色に戻る。
            window.Hide();
            Assert.False(root.HasAnimatedProperties);
            Assert.Same(vm.BackgroundBrush, root.Background);
            Assert.Same(vm.TitleBarBrush, titleBar.Background);
        }
        finally
        {
            foreground.Close();
            window.Close();
        }
    }

    [Theory]
    [InlineData(0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xBF, 0xBF)] // 白 → 赤へ4分の1
    [InlineData(0xFF, 0x20, 0x20, 0x20, 0x58, 0x18, 0x18)] // 暗い背景
    [InlineData(0xB3, 0xFF, 0xF9, 0xC4, 0xFF, 0xBB, 0x93)] // 半透明の付箋は透明度を保つ
    public void ErrorTintColor_MovesTheBackgroundAQuarterTowardRed(byte a, byte r, byte g, byte b, byte er, byte eg, byte eb)
        => Assert.Equal(Color.FromArgb(a, er, eg, eb), StickyNoteWindow.ErrorTintColor(Color.FromArgb(a, r, g, b)));

    // 追記の速いログでは更新が立て続けに届く。そのたびに明滅を始めから
    // やり直すと、光りきる前に振り出しへ戻って光って見えなくなる。
    [WpfFact]
    public void ReloadExternalContent_RapidUpdates_DoNotRestartTheFlashMidPulse()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var previousInterval = app.Settings.ExternalFile.MinRefreshIntervalMs;
        using var temp = new TempDataDirectory();
        Directory.CreateDirectory(temp.Path);
        var storage = new StorageService(temp.Path);
        var externalPath = Path.Combine(temp.Path, "app.log");
        File.WriteAllText(externalPath, "line 1\n");
        var vm = new StickyNoteViewModel(
            new StickyNote
            {
                Content = "line 1\n", ExternalContentPath = externalPath,
                ExternalTailMode = true, IsReadOnly = true,
            },
            app.Settings);
        var window = new StickyNoteWindow(vm, storage);
        var foreground = new Window { Width = 120, Height = 120, ShowInTaskbar = false };
        var flashRunning = typeof(StickyNoteWindow)
            .GetField("_isUpdateFlashRunning", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            // 間引きなしで届く設定。まさにこの条件で明滅が潰れていた。
            app.Settings.ExternalFile.MinRefreshIntervalMs = 0;
            window.Show();
            foreground.Show();
            foreground.Activate();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.False(window.IsActive);

            var border = (Border)window.FindName("UpdateFlashBorder")!;
            File.AppendAllText(externalPath, "line 2\n");
            window.Dispatcher.Invoke(window.ReloadExternalContent);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.True((bool)flashRunning.GetValue(window)!);

            // 明滅の途中まで進める（0.4秒で最大まで明るくなる）。
            var until = Environment.TickCount64 + 200;
            while (Environment.TickCount64 < until)
            {
                Thread.Sleep(10);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            }
            var midPulse = border.Opacity;
            Assert.True(midPulse > 0.2, $"the pulse should be visible by now, was {midPulse}");

            // ここで次の更新が届いても、明滅は振り出しに戻らない。
            File.AppendAllText(externalPath, "line 3\n");
            window.Dispatcher.Invoke(window.ReloadExternalContent);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.Contains("line 3", vm.Content);
            Assert.True(border.Opacity >= midPulse * 0.8,
                $"the pulse restarted: {midPulse} -> {border.Opacity}");
        }
        finally
        {
            foreground.Close();
            window.Close();
            app.Settings.ExternalFile.MinRefreshIntervalMs = previousInterval;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [WpfFact]
    public void ReloadExternalContent_WhileNoteIsActive_DoesNotFlash()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        Directory.CreateDirectory(temp.Path);
        var storage = new StorageService(temp.Path);
        var externalPath = Path.Combine(temp.Path, "app.log");
        File.WriteAllText(externalPath, "first line\n");
        var vm = new StickyNoteViewModel(
            new StickyNote { Content = "first line\n", ExternalContentPath = externalPath, IsReadOnly = true },
            new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Show();
            window.Activate();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(window.IsActive);
            // "Being looked at" means the foreground window. Windows may refuse
            // the test host the foreground; the note is then not being looked at.
            if (GetForegroundWindow() != new System.Windows.Interop.WindowInteropHelper(window).Handle) return;

            File.AppendAllText(externalPath, "second line\n");
            Task.Run(window.ReloadExternalContent).GetAwaiter().GetResult();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            // 見ている本人には更新が届いているので、光らせる必要はない。
            Assert.Contains("second line", vm.Content);
            Assert.False(((Border)window.FindName("UpdateFlashBorder")!).HasAnimatedProperties);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void ReloadExternalContent_WhenFileContentIsUnchanged_DoesNothing()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        Directory.CreateDirectory(temp.Path);
        var storage = new StorageService(temp.Path);
        var externalPath = Path.Combine(temp.Path, "app.log");
        File.WriteAllText(externalPath, "unchanged\n");
        var note = new StickyNote
        {
            Content = "unchanged\n",
            ExternalContentPath = externalPath,
            IsReadOnly = true,
            UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0),
        };
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            // 中身が変わらない監視イベント（更新日時だけ触る保存など）を模す。
            File.SetLastWriteTimeUtc(externalPath, DateTime.UtcNow);
            Task.Run(window.ReloadExternalContent).GetAwaiter().GetResult();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0), note.UpdatedAt);
            Assert.False(((Border)window.FindName("UpdateFlashBorder")!).HasAnimatedProperties);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void ToggleExternalTailMode_TogglesTheTitleBarTailIndicator()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        Directory.CreateDirectory(temp.Path);
        var storage = new StorageService(temp.Path);
        var externalPath = Path.Combine(temp.Path, "app.log");
        File.WriteAllText(externalPath, "line1\nline2\n");
        var vm = new StickyNoteViewModel(
            new StickyNote { Content = "", ExternalContentPath = externalPath, IsReadOnly = true },
            new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            var indicator = (TextBlock)window.FindName("TailModeIndicator")!;
            Assert.Equal(Visibility.Collapsed, indicator.Visibility);
            var watcherOnly = GetPrivateField<ExternalFileMonitor>(window, "_externalContentMonitor");
            watcherOnly.Wake();
            Assert.False(ExternalFilePoller.Shared.Contains(watcherOnly));

            window.ToggleExternalTailMode();
            window.UpdateLayout();

            Assert.Equal(Visibility.Visible, indicator.Visibility);
            Assert.NotNull(indicator.ToolTip);
            var tailMonitor = GetPrivateField<ExternalFileMonitor>(window, "_externalContentMonitor");
            Assert.True(ExternalFilePoller.Shared.Contains(tailMonitor));

            window.ToggleExternalTailMode();
            window.UpdateLayout();

            Assert.Equal(Visibility.Collapsed, indicator.Visibility);
            Assert.False(ExternalFilePoller.Shared.Contains(tailMonitor));
            Assert.False(ExternalFilePoller.Shared.Contains(
                GetPrivateField<ExternalFileMonitor>(window, "_externalContentMonitor")));
        }
        finally
        {
            window.Close();
        }
    }

    [WpfTheory]
    [InlineData("yellow", "INFO", "#FF2E7D32")]
    [InlineData("yellow", "WARN", "#FFE65100")]
    [InlineData("yellow", "ERROR", "#FFC62828")]
    [InlineData("dark-charcoal", "INFO", "#FF81C784")]
    [InlineData("dark-charcoal", "WARN", "#FFFFB74D")]
    [InlineData("dark-charcoal", "ERROR", "#FFFF8A80")]
    public void TailMode_ColorsTheLogLevelWordForTheNoteBackground(string colorKey, string level, string expected)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        Directory.CreateDirectory(temp.Path);
        var externalPath = Path.Combine(temp.Path, "app.log");
        File.WriteAllText(externalPath, $"09:12:01 [{level}] something happened\n");
        var note = new StickyNote
        {
            ColorKey = colorKey, ExternalContentPath = externalPath,
            ExternalTailMode = true, IsReadOnly = true,
        };
        note.Content = StorageService.ReadExternalContent(note);
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var content = (RichTextBox)window.FindName("ContentBox")!;

            var levelRun = content.Document.Blocks.OfType<Paragraph>()
                .SelectMany(p => p.Inlines.OfType<Run>())
                .Single(run => run.Text == level);
            Assert.Equal(expected, ((SolidColorBrush)levelRun.Foreground).Color.ToString());

            // レベル名と数値以外は本文の色のまま。
            Assert.All(
                content.Document.Blocks.OfType<Paragraph>()
                    .SelectMany(p => p.Inlines.OfType<Run>())
                    .Where(run => run.Text != level && !run.Text.Any(char.IsAsciiDigit)),
                run => Assert.Null(run.ReadLocalValue(TextElement.ForegroundProperty) as Brush));
        }
        finally { window.Close(); }
    }

    [WpfTheory]
    [InlineData("yellow", "#FF1565C0")]
    [InlineData("dark-charcoal", "#FF64B5F6")]
    public void TailMode_ColorsDatesAndNumbersApartFromTheLevel(string colorKey, string expected)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        Directory.CreateDirectory(temp.Path);
        var externalPath = Path.Combine(temp.Path, "app.log");
        File.WriteAllText(externalPath, "2026-09-15 09:12:03.221 [WARN] slow after 42 ms\n");
        var note = new StickyNote
        {
            ColorKey = colorKey, ExternalContentPath = externalPath,
            ExternalTailMode = true, IsReadOnly = true,
        };
        note.Content = StorageService.ReadExternalContent(note);
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var runs = ((RichTextBox)window.FindName("ContentBox")!).Document.Blocks
                .OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).ToList();

            // 日付・時刻・数値がそれぞれひとまとまりで色付けされている。
            foreach (var number in new[] { "2026-09-15", "09:12:03.221", "42" })
            {
                var run = Assert.Single(runs, r => r.Text == number);
                Assert.Equal(expected, ((SolidColorBrush)run.Foreground).Color.ToString());
            }

            // レベル名は数値とは別の色のまま。
            var level = Assert.Single(runs, r => r.Text == "WARN");
            Assert.NotEqual(expected, ((SolidColorBrush)level.Foreground).Color.ToString());
        }
        finally { window.Close(); }
    }

    // 既定のスクロールバーは溝が白く、濃い色の付箋では右端に白い帯が残る。
    // 付箋の本文では溝を透かして、地の色をそのまま見せる。
    [WpfFact]
    public void NoteBody_ScrollBarsLetTheNoteColorShowThrough()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote
        {
            ColorKey = "dark-charcoal", Width = 240, Height = 120,
            Content = string.Join('\n', Enumerable.Range(1, 60).Select(i => $"line {i}")),
        };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();

            var bars = FindVisualChildren<System.Windows.Controls.Primitives.ScrollBar>(
                (RichTextBox)window.FindName("ContentBox")!).ToList();

            Assert.NotEmpty(bars);
            Assert.All(bars, bar => Assert.Equal(0, ((SolidColorBrush)bar.Background).Color.A));
        }
        finally { window.Close(); }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    [WpfFact]
    public void WithoutTailMode_TheLogLevelIsNotColored()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        Directory.CreateDirectory(temp.Path);
        var externalPath = Path.Combine(temp.Path, "notes.md");
        File.WriteAllText(externalPath, "09:12:01 [ERROR] something happened\n");
        var note = new StickyNote { ExternalContentPath = externalPath, IsReadOnly = true };
        note.Content = StorageService.ReadExternalContent(note);
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var content = (RichTextBox)window.FindName("ContentBox")!;

            Assert.All(
                content.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()),
                run => Assert.Null(run.ReadLocalValue(TextElement.ForegroundProperty) as Brush));
        }
        finally { window.Close(); }
    }

    [WpfTheory]
    [InlineData("[INFO]  still fine", "#FF40C4FF")]
    [InlineData("[ERROR] upload rejected", "#FFFF5252")]
    [InlineData("[FATAL] worker pool exhausted", "#FFFF5252")]
    public void ReloadExternalContent_FlashIsRedWhenAnErrorArrives(string appended, string expected)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        Directory.CreateDirectory(temp.Path);
        var externalPath = Path.Combine(temp.Path, "app.log");
        File.WriteAllText(externalPath, "09:12:01 [INFO]  service started\n");
        var vm = new StickyNoteViewModel(
            new StickyNote
            {
                Content = "09:12:01 [INFO]  service started\n",
                ExternalContentPath = externalPath,
                ExternalTailMode = true,
                IsReadOnly = true,
            },
            new AppSettings());
        var window = new StickyNoteWindow(vm, new StorageService(temp.Path));
        var foreground = new Window { Width = 120, Height = 120, ShowInTaskbar = false };
        try
        {
            window.Show();
            foreground.Show();
            foreground.Activate();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.False(window.IsActive);

            File.AppendAllText(externalPath, $"09:12:20 {appended}\n");
            Task.Run(window.ReloadExternalContent).GetAwaiter().GetResult();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var border = (Border)window.FindName("UpdateFlashBorder")!;
            Assert.True(border.HasAnimatedProperties);
            Assert.Equal(expected, ((SolidColorBrush)border.BorderBrush).Color.ToString());
        }
        finally
        {
            foreground.Close();
            window.Close();
        }
    }

    [WpfFact]
    public void ReloadExternalContent_TailMode_RendersAsPlainTextInsteadOfMarkdown()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        Directory.CreateDirectory(temp.Path);
        var storage = new StorageService(temp.Path);
        var externalPath = Path.Combine(temp.Path, "app.log");
        File.WriteAllText(externalPath, "line1\nline2\n# not-a-heading\nline4\n");
        var vm = new StickyNoteViewModel(
            new StickyNote
            {
                Content = "",
                ExternalContentPath = externalPath,
                ExternalTailMode = true,
                IsReadOnly = true,
            },
            new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            Task.Run(window.ReloadExternalContent).GetAwaiter().GetResult();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var contentBox = (RichTextBox)window.FindName("ContentBox")!;
            var text = new TextRange(contentBox.Document.ContentStart, contentBox.Document.ContentEnd).Text;

            // Markdown rendering would strip the leading "#" from a heading line;
            // tail mode must keep the raw log line untouched.
            Assert.Contains("# not-a-heading", text);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void ReloadExternalContent_RapidChangesWithinMinRefreshInterval_ThrottleToLatestContent()
    {
        var app = (App)WpfApplicationFixture.Ensure();
        var previousInterval = app.Settings.ExternalFile.MinRefreshIntervalMs;
        using var temp = new TempDataDirectory();
        Directory.CreateDirectory(temp.Path);
        var storage = new StorageService(temp.Path);
        var externalPath = Path.Combine(temp.Path, "external.md");
        File.WriteAllText(externalPath, "version 1");
        var vm = new StickyNoteViewModel(
            new StickyNote
            {
                Content = "cached",
                ExternalContentPath = externalPath,
                IsReadOnly = true,
            },
            app.Settings);
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            app.Settings.ExternalFile.MinRefreshIntervalMs = 500;

            Task.Run(window.ReloadExternalContent).GetAwaiter().GetResult();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Equal("version 1", vm.Content);

            // A burst of rapid file-change notifications inside the throttle window
            // must not each trigger an immediate re-read.
            File.WriteAllText(externalPath, "version 2");
            Task.Run(window.ReloadExternalContent).GetAwaiter().GetResult();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Equal("version 1", vm.Content);

            File.WriteAllText(externalPath, "version 3");
            Task.Run(window.ReloadExternalContent).GetAwaiter().GetResult();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.Equal("version 1", vm.Content);

            // Once the throttle interval elapses, the latest content on disk
            // (not an intermediate one) should surface exactly once.
            var until = Environment.TickCount64 + 3000;
            while (vm.Content != "version 3" && Environment.TickCount64 < until)
            {
                Thread.Sleep(50);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            }

            Assert.Equal("version 3", vm.Content);
        }
        finally
        {
            window.Close();
            app.Settings.ExternalFile.MinRefreshIntervalMs = previousInterval;
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

    [WpfFact]
    public void PasteFromDataObject_Files_CopiesOriginalsIntoAssets()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var sourceDir = Path.Combine(temp.Path, "source");
        Directory.CreateDirectory(sourceDir);
        var photo = Path.Combine(sourceDir, "旅行 写真 (1).PNG");
        WritePngFile(photo);
        var memo = Path.Combine(sourceDir, "memo.txt");
        File.WriteAllText(memo, "not an image");
        var vm = new StickyNoteViewModel(new StickyNote { Content = "first\nsecond" }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            var files = new DataObject();
            files.SetData(DataFormats.FileDrop, new[] { photo, memo });

            InvokePrivate(window, "PasteFromDataObject", files);

            // 画像はそのまま表示され、画像でないファイルはアイコンの札になる。
            Assert.Equal(
                "first\nsecond\n![旅行-写真-1](assets/旅行-写真-1.PNG)\n![memo.txt](assets/memo.txt)",
                vm.Content);
            var assets = storage.GetNoteAssetsDirectoryPath(vm.Model.Id);
            var copied = Directory.GetFiles(assets).Select(file => Path.GetFileName(file)!).Order().ToArray();
            Assert.Equal(["memo.txt", "旅行-写真-1.PNG"], copied);
            Assert.Equal(File.ReadAllBytes(photo), File.ReadAllBytes(Path.Combine(assets, "旅行-写真-1.PNG")));
            // どちらも元のファイルには触らない。
            Assert.True(File.Exists(photo));
            Assert.True(File.Exists(memo));
        }
        finally
        {
            window.Close();
        }
    }

    // WPF の OLE ドロップと同じく、同じ引数でトンネル→バブルの順に流す。
    // 本文の TextBox 自身のドロップ処理より先にウィンドウが受け取れているかを確かめる。
    [WpfFact]
    public void ImageFileDrop_IsTakenBeforeTheBodyEditorAndRefusedWhileLocked()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        Directory.CreateDirectory(temp.Path);
        var photo = Path.Combine(temp.Path, "photo.png");
        WritePngFile(photo);
        var data = new DataObject(DataFormats.FileDrop, new[] { photo });
        var vm = new StickyNoteViewModel(new StickyNote { Content = "first\nsecond" }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        var lockedVm = new StickyNoteViewModel(new StickyNote { Content = "locked", IsReadOnly = true }, new AppSettings());
        var lockedWindow = new StickyNoteWindow(lockedVm, storage);
        try
        {
            InvokePrivate(window, "EnterEditMode");
            var body = Assert.IsType<TextBox>(window.FindName("BodyEditBox"));

            var over = RaiseDragEvent(body, data, DragDrop.PreviewDragOverEvent, DragDrop.DragOverEvent);
            Assert.True(over.Handled);
            Assert.Equal(DragDropEffects.Copy, over.Effects);

            var drop = RaiseDragEvent(body, data, DragDrop.PreviewDropEvent, DragDrop.DropEvent);
            Assert.True(drop.Handled);
            Assert.Equal(DragDropEffects.Copy, drop.Effects);
            Assert.Contains("![photo](assets/photo.png)", vm.Content);
            Assert.Contains("first", vm.Content);
            Assert.Single(Directory.GetFiles(storage.GetNoteAssetsDirectoryPath(vm.Model.Id)));

            var beforeMoveOnlyDrop = vm.Content;
            var moveOnlyDrop = RaiseDragEvent(body, data, DragDrop.PreviewDropEvent,
                DragDrop.DropEvent, DragDropEffects.Move);
            Assert.Equal(DragDropEffects.None, moveOnlyDrop.Effects);
            Assert.Equal(beforeMoveOnlyDrop, vm.Content);
            Assert.Single(Directory.GetFiles(storage.GetNoteAssetsDirectoryPath(vm.Model.Id)));

            var lockedBody = Assert.IsType<RichTextBox>(lockedWindow.FindName("ContentBox"));
            var lockedOver = RaiseDragEvent(lockedBody, data, DragDrop.PreviewDragOverEvent, DragDrop.DragOverEvent);
            Assert.Equal(DragDropEffects.None, lockedOver.Effects);
            var lockedDrop = RaiseDragEvent(lockedBody, data, DragDrop.PreviewDropEvent, DragDrop.DropEvent);
            Assert.Equal(DragDropEffects.None, lockedDrop.Effects);
            Assert.Equal("locked", lockedVm.Content);
            Assert.False(Directory.Exists(storage.GetNoteAssetsDirectoryPath(lockedVm.Model.Id)));
        }
        finally
        {
            window.Close();
            lockedWindow.Close();
        }
    }

    /// <summary>
    /// 画像以外のファイルは、assets へコピーして札として置く。
    /// Shift を押しながら落としたときとフォルダーは、コピーせず元の場所を指す。
    /// </summary>
    [WpfFact]
    public void FileDrop_CopiesIntoAssetsAndLinksWhileShiftIsHeld()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        Directory.CreateDirectory(temp.Path);
        var report = Path.Combine(temp.Path, "report.pdf");
        File.WriteAllText(report, "pdf");
        var spec = Path.Combine(temp.Path, "spec sheet.docx");
        File.WriteAllText(spec, "docx");
        var folder = Path.Combine(temp.Path, "materials");
        Directory.CreateDirectory(folder);
        var vm = new StickyNoteViewModel(new StickyNote { Content = "first" }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            InvokePrivate(window, "EnterEditMode");
            var body = Assert.IsType<TextBox>(window.FindName("BodyEditBox"));

            RaiseDragEvent(body, new DataObject(DataFormats.FileDrop, new[] { report }),
                DragDrop.PreviewDropEvent, DragDrop.DropEvent);
            Assert.Contains("![report.pdf](assets/report.pdf)", vm.Content);
            Assert.True(File.Exists(Path.Combine(storage.GetNoteAssetsDirectoryPath(vm.Model.Id), "report.pdf")));

            // Shift 付きはコピーしない。空白を含む場所は <> で囲む。
            RaiseDragEvent(body, new DataObject(DataFormats.FileDrop, new[] { spec }),
                DragDrop.PreviewDropEvent, DragDrop.DropEvent,
                DragDropEffects.Copy | DragDropEffects.Move, DragDropKeyStates.ShiftKey);
            Assert.Contains($"![spec sheet.docx](<{spec}>)", vm.Content);
            Assert.False(File.Exists(Path.Combine(storage.GetNoteAssetsDirectoryPath(vm.Model.Id), "spec-sheet.docx")));

            // フォルダーは中身ごと持ってこない。Windows パスは空白がなくても <> で囲む。
            RaiseDragEvent(body, new DataObject(DataFormats.FileDrop, new[] { folder }),
                DragDrop.PreviewDropEvent, DragDrop.DropEvent);
            Assert.Contains($"![materials](<{folder}>)", vm.Content);
            Assert.Contains(vm.Content.Split('\n'),
                line => MarkdownRenderer.GetImageOnlyTarget(line) == folder);
            Assert.Equal(["report.pdf"],
                Directory.GetFiles(storage.GetNoteAssetsDirectoryPath(vm.Model.Id)).Select(Path.GetFileName));
        }
        finally
        {
            window.Close();
        }
    }

    private static DragEventArgs RaiseDragEvent(UIElement target, IDataObject data, RoutedEvent tunnel, RoutedEvent bubble,
        DragDropEffects allowed = DragDropEffects.Copy | DragDropEffects.Move,
        DragDropKeyStates keyStates = DragDropKeyStates.None)
    {
        var constructor = typeof(DragEventArgs)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 5);
        var args = (DragEventArgs)constructor.Invoke([data, keyStates, allowed, target, new Point(1, 1)]);
        args.Effects = allowed;
        args.RoutedEvent = tunnel;
        target.RaiseEvent(args);
        args.RoutedEvent = bubble;
        target.RaiseEvent(args);
        return args;
    }

    private static void WritePngFile(string path)
    {
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(CreateBitmapSource()));
        using var stream = File.Create(path);
        encoder.Save(stream);
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
    public void LoadContent_ReadOnlyMarkdownImageWithoutWidth_UpscalesToFitWindow()
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

            Assert.True(image.Width > 2 / VisualTreeHelper.GetDpi(window).DpiScaleX);
            Assert.Equal(image.Width, image.Height, 8);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void LoadContent_WideImageWithTextFitsBesideTheVerticalScrollBar()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote
        {
            Width = 380,
            Height = 300,
            IsReadOnly = true,
            Content = "text\n\n![image](assets/wide.png)\n\n" + string.Join("\n\n", Enumerable.Range(1, 20).Select(i => $"line {i}")),
        };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        var pixels = new byte[1200 * 200 * 4];
        SavePng(System.IO.Path.Combine(assetsDir, "wide.png"),
            System.Windows.Media.Imaging.BitmapSource.Create(1200, 200, 96, 96, PixelFormats.Bgra32, null, pixels, 1200 * 4));
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), storage);
        try
        {
            window.Show();
            InvokePrivate(window, "LoadContent", note.Content);
            window.UpdateLayout();
            var contentBox = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var viewer = Assert.Single(FindVisualChildren<ScrollViewer>(contentBox));
            Assert.Equal(Visibility.Visible, viewer.ComputedVerticalScrollBarVisibility);
            var image = Assert.Single(EnumerateImages(contentBox.Document));
            var padding = contentBox.Document.PagePadding;
            Assert.True(image.Width <= viewer.ViewportWidth - padding.Left - padding.Right,
                $"image {image.Width} should fit viewport {viewer.ViewportWidth} minus page padding {padding.Left + padding.Right}");
            Assert.Equal(Visibility.Collapsed, viewer.ComputedHorizontalScrollBarVisibility);
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
    public void EnterEditMode_DoesNotControlIme()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(new StickyNote { Content = "本文" }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            InvokePrivate(window, "EnterEditMode");
            var bodyEditBox = Assert.IsType<TextBox>(window.FindName("BodyEditBox"));
            var conversionMode = InputMethod.GetPreferredImeConversionMode(bodyEditBox);

            Assert.Equal(ImeConversionModeValues.DoNotCare, conversionMode);
            Assert.Equal(InputMethodState.DoNotCare, InputMethod.GetPreferredImeState(bodyEditBox));
            bodyEditBox.GetBindingExpression(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty)!.UpdateTarget();
            Assert.Equal(bodyEditBox.Foreground, bodyEditBox.CaretBrush);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void MarkdownTable_UsesContentWidths()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { Content = "| A | B |\n| --- | --- |\n| short | longer content |" };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), new StorageService(temp.Path));
        try
        {
            window.Show();
            InvokePrivate(window, "LoadContent", note.Content);
            window.UpdateLayout();
            var box = (RichTextBox)window.FindName("ContentBox");
            var table = Assert.IsType<Table>(box.Document.Blocks.FirstBlock);
            Assert.All(table.Columns.Cast<TableColumn>(), column => Assert.True(column.Width.IsAbsolute));
            Assert.True(table.Columns[1].Width.Value > table.Columns[0].Width.Value);
        }
        finally { window.Close(); }
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

    /// <summary>
    /// 確定ボタンはツールバーではなく付箋本体の右下にある。ツールバーはフォーカスが
    /// 外れると隠れるので、そこに置くと編集を終える手段まで一緒に消えてしまう。
    /// 「編集中」（左下）の反対側に置く。
    /// </summary>
    [WpfFact]
    public void DoneEditingButton_SitsAtTheBottomRightOfTheNoteNotInTheToolbar()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(new StickyNote { Content = "body" }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            var done = Assert.IsType<Button>(window.FindName("DoneEditingButton"));
            var badge = Assert.IsType<Border>(window.FindName("EditingBadge"));
            var toolbar = Assert.IsType<Border>(window.FindName("StatusBar"));

            // ツールバーの中にはいない。
            Assert.False(IsInside(done, toolbar));
            // 「編集中」と同じ行の反対側の隅。
            Assert.Equal(System.Windows.HorizontalAlignment.Right, done.HorizontalAlignment);
            Assert.Equal(System.Windows.VerticalAlignment.Bottom, done.VerticalAlignment);
            Assert.Equal(System.Windows.HorizontalAlignment.Left, badge.HorizontalAlignment);
            Assert.Equal(System.Windows.VerticalAlignment.Bottom, badge.VerticalAlignment);
            Assert.Equal(Grid.GetRow(badge), Grid.GetRow(done));
            // 押してもフォーカスが本文から動かない（カーソルや選択を乱さない）。
            Assert.False(done.Focusable);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 確定ボタンは角から少し離して、押しやすい大きさにする。
    /// </summary>
    [WpfFact]
    public void DoneEditingButton_IsBigEnoughAndKeptOffTheCorner()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(new StickyNote { Content = "body" }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            var done = Assert.IsType<Button>(window.FindName("DoneEditingButton"));

            Assert.Equal(36, done.Width);
            Assert.Equal(36, done.Height);
            Assert.Equal(10, done.Margin.Right);
            Assert.Equal(10, done.Margin.Bottom);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 編集中は最終行の下に帯を空け、最終行が確定ボタンや「編集中」の下に
    /// 隠れないようにする。本文はその帯の上で終わる。
    /// </summary>
    [WpfFact]
    public void BodyEditing_LeavesARoomBelowTheLastLineForTheButtons()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var text = string.Join("\n", Enumerable.Range(1, 60).Select(i => "line " + i));
        var vm = new StickyNoteViewModel(new StickyNote { Content = text, Height = 260 }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            InvokePrivate(window, "EnterEditMode");
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var editor = Assert.IsType<TextBox>(window.FindName("BodyEditBox"));
            var done = Assert.IsType<Button>(window.FindName("DoneEditingButton"));
            var badge = Assert.IsType<Border>(window.FindName("EditingBadge"));

            // いちばん下までスクロールして、最終行を確定ボタンの高さに来るところまで送る。
            editor.ScrollToEnd();
            window.UpdateLayout();
            var lastLine = editor.GetRectFromCharacterIndex(editor.Text.Length);
            var lastLineBottom = editor.TranslatePoint(lastLine.BottomLeft, window).Y;
            var buttonTop = done.TranslatePoint(new Point(), window).Y;
            var badgeTop = badge.TranslatePoint(new Point(), window).Y;

            // 最終行は、確定ボタンと「編集中」のどちらの上端よりも上に収まる。
            Assert.True(lastLineBottom <= buttonTop, $"last line ends at {lastLineBottom}, button starts at {buttonTop}");
            Assert.True(lastLineBottom <= badgeTop, $"last line ends at {lastLineBottom}, badge starts at {badgeTop}");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 小さな付箋では帯のぶん本文の欄が潰れて編集できなくなってしまう。
    /// 帯を削ってでも、本文には3行ぶんの高さを残す。
    /// </summary>
    [WpfFact]
    public void BodyEditing_ShrinksTheRoomOnATinyNoteInsteadOfCrushingTheText()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(new StickyNote { Content = "text", Height = 125 }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            InvokePrivate(window, "EnterEditMode");
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var editor = Assert.IsType<TextBox>(window.FindName("BodyEditBox"));
            var viewer = (ScrollViewer)editor.Template.FindName("PART_ContentHost", editor);

            // 帯は満額(50)までは取れないが、まったく無くなるわけでもない。
            // そのうえで、本文には3行ぶんの高さが残っている。
            Assert.InRange(editor.Padding.Bottom, 9, 49);
            Assert.True(viewer.ViewportHeight >= vm.FontSize * 1.5 * 3 - 1,
                $"text area {viewer.ViewportHeight}pt for a {vm.FontSize}pt font");
        }
        finally
        {
            window.Close();
        }
    }

    private static bool IsInside(DependencyObject? node, DependencyObject container)
    {
        for (; node != null; node = LogicalTreeHelper.GetParent(node) ?? VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, container)) return true;
        return false;
    }

    /// <summary>編集している間だけ出る。ツールバーが隠れても出たまま。</summary>
    [WpfFact]
    public void DoneEditingButton_ShowsOnlyWhileEditingAndSurvivesTheToolbarHiding()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var vm = new StickyNoteViewModel(new StickyNote { Content = "body" }, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            var done = Assert.IsType<Button>(window.FindName("DoneEditingButton"));
            var toolbar = Assert.IsType<Popup>(window.FindName("EditToolbarPopup"));
            Assert.Equal(Visibility.Collapsed, done.Visibility);

            InvokePrivate(window, "EnterEditMode");
            Assert.Equal(Visibility.Visible, done.Visibility);

            // フォーカスが外れてツールバーが隠れても、確定ボタンは残る。
            InvokePrivate(window, "HideEditToolbar");
            Assert.False(toolbar.IsOpen);
            Assert.Equal(Visibility.Visible, done.Visibility);

            InvokePrivate(window, "DoneEditing_Click", done, new RoutedEventArgs());
            Assert.Equal(Visibility.Collapsed, done.Visibility);

            // タイトルだけ直すときも同じ。ツールバーには無いので、ここに出す。
            InvokePrivate(window, "EnterTitleEditMode");
            Assert.Equal(Visibility.Visible, done.Visibility);
            InvokePrivate(window, "DoneEditing_Click", done, new RoutedEventArgs());
            Assert.Equal(Visibility.Collapsed, done.Visibility);
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

    [WpfTheory]
    [InlineData(2)]
    [InlineData(3000)]
    public void ResizeMarkdownImage_ReadOnlyNote_SavesDisplayOverrideWithoutChangingContent(int imageWidth)
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
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(imageWidth, 2, 96, 96,
            PixelFormats.Bgra32, null, new byte[imageWidth * 2 * 4], imageWidth * 4);
        SavePng(System.IO.Path.Combine(assetsDir, "pasted.png"), bitmap);
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
            var expectedWidth = Math.Round(imageWidth * 2 / VisualTreeHelper.GetDpi(window).DpiScaleX);
            Assert.Contains(note.ExternalImageWidthOverrides, pair => pair.Key.EndsWith(":assets/pasted.png") && pair.Value == expectedWidth);
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
            Assert.Equal(Visibility.Collapsed, contentBox.Visibility);
            Assert.Equal(Visibility.Visible, ((Border)window.FindName("FoldedPreviewHost")).Visibility);
            Assert.Equal("first line", ((TextBlock)window.FindName("FoldedPreviewText")).Text);
            Assert.True(window.Height < note.Height,
                $"folded height {window.Height} should be below the open height {note.Height}");
        }
        finally { window.Close(); }
    }

    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FoldedPreview_ReusesMarkdownAndImagesAndRestoresScroll(bool animated)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote { IsTitleBarHidden = true, Width = 420, Height = 300,
            Content = "# Heading\n![image](assets/pasted.png)\n" + string.Join("\n\n", Enumerable.Repeat("**本文** with [link](https://example.com)", 150)) };
        Directory.CreateDirectory(storage.GetNoteAssetsDirectoryPath(note.Id));
        SavePng(System.IO.Path.Combine(storage.GetNoteAssetsDirectoryPath(note.Id), "pasted.png"), CreateBitmapSource());
        var previous = App.Current.Settings.EnableFoldAnimation;
        App.Current.Settings.EnableFoldAnimation = animated;
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, App.Current.Settings), storage);
        try
        {
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var box = (RichTextBox)window.FindName("ContentBox");
            box.ScrollToVerticalOffset(200);
            window.UpdateLayout();
            var offset = box.VerticalOffset;
            Assert.True(offset > 0);
            var blocks = box.Document.Blocks.Cast<Block>().ToArray();
            var image = Assert.Single(EnumerateImages(box.Document));
            for (var i = 0; i < 3; i++)
            {
                InvokePrivate(window, "ToggleFold", (object?)null);
                InvokePrivate(window, "CompleteFoldAnimation");
                Assert.Equal("Heading", ((TextBlock)window.FindName("FoldedPreviewText")).Text);
                Assert.Equal(window.ViewModel.FontSize, box.FontSize);
                Assert.Same(blocks[0], box.Document.Blocks.FirstBlock);
                InvokePrivate(window, "ToggleFold", (object?)null);
                if (animated)
                    Assert.Equal(Visibility.Collapsed, box.Visibility);
                InvokePrivate(window, "CompleteFoldAnimation");
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.Equal(blocks, box.Document.Blocks.Cast<Block>().ToArray());
                Assert.Same(image, Assert.Single(EnumerateImages(box.Document)));
                Assert.Equal(offset, box.VerticalOffset, 1);
            }
            InvokePrivate(window, "ToggleFold", (object?)null);
            InvokePrivate(window, "CompleteFoldAnimation");
            window.ViewModel.Content = "# Updated\nnew body";
            InvokePrivate(window, "LoadContent", window.ViewModel.Content);
            Assert.Equal("Updated", ((TextBlock)window.FindName("FoldedPreviewText")).Text);
            Assert.Same(blocks[0], box.Document.Blocks.FirstBlock);
            InvokePrivate(window, "ToggleFold", (object?)null);
            InvokePrivate(window, "CompleteFoldAnimation");
            Assert.NotSame(blocks[0], box.Document.Blocks.FirstBlock);
            Assert.Contains("new body", new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text);
        }
        finally { window.Close(); App.Current.Settings.EnableFoldAnimation = previous; }
    }

    [WpfFact]
    public void FoldedPreview_ReloadsImagesCreatedOrChangedWhileFolded()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote { IsTitleBarHidden = true, Content = "![image](assets/pasted.png)" };
        var directory = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "pasted.png");
        var window = new StickyNoteWindow(new StickyNoteViewModel(note, new AppSettings()), storage);
        void Toggle()
        {
            InvokePrivate(window, "ToggleFold", (object?)null);
            InvokePrivate(window, "CompleteFoldAnimation");
            window.UpdateLayout();
        }
        try
        {
            window.Show();
            window.UpdateLayout();
            var box = (RichTextBox)window.FindName("ContentBox");
            Assert.Empty(EnumerateImages(box.Document));
            Toggle();
            SavePng(path, CreateBitmapSource());
            Toggle();
            var image = Assert.Single(EnumerateImages(box.Document));
            Toggle();
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(5));
            Toggle();
            Assert.NotSame(image, Assert.Single(EnumerateImages(box.Document)));
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

    [WpfTheory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void TitleBarHiddenSpine_DoesNotCoverEditorsAndReturnsAfterEditing(bool titleOnly, bool showSpine)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote { IsTitleBarHidden = true, Width = 260, Height = 220,
            EditWidth = 260, EditHeight = 220, Content = new string('x', 200) };
        var window = new StickyNoteWindow(new StickyNoteViewModel(note,
            new AppSettings { ShowTitleBarHiddenSpine = showSpine }), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var spine = (System.Windows.Shapes.Rectangle)window.FindName("TitleBarHiddenSpine");
            var handle = (Border)window.FindName("TitleBarHiddenSpineHandle");
            var expectedViewing = showSpine ? Visibility.Visible : Visibility.Collapsed;
            Assert.Equal(expectedViewing, spine.Visibility);

            InvokePrivate(window, titleOnly ? "EnterTitleEditMode" : "EnterEditMode");
            window.UpdateLayout();
            Assert.Equal(Visibility.Collapsed, spine.Visibility);
            Assert.Equal(Visibility.Collapsed, handle.Visibility);
            Assert.False(handle.IsVisible);
            if (!titleOnly)
            {
                var editor = (TextBox)window.FindName("BodyEditBox");
                var scrollViewer = Assert.Single(FindVisualChildren<ScrollViewer>(editor));
                Assert.Equal(Visibility.Visible, scrollViewer.ComputedHorizontalScrollBarVisibility);
            }

            InvokePrivate(window, "EnterViewMode");
            window.UpdateLayout();
            Assert.Equal(expectedViewing, spine.Visibility);
            Assert.Equal(expectedViewing, handle.Visibility);
            Assert.True(note.IsTitleBarHidden);
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
            Assert.True(handle.Margin.Left + handle.ActualWidth <= box.Padding.Left,
                $"grab handle reaches {handle.Margin.Left + handle.ActualWidth}px, text starts at {box.Padding.Left}px");
        }
        finally { window.Close(); }
    }

    // 両端は半円。太さを変えても丸みが追従する。
    [WpfTheory]
    [InlineData(3.0, 1.5)]
    [InlineData(8.0, 4.0)]
    public void TitleBarHiddenSpine_HasRoundedCaps(double width, double expectedRadius)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var settings = new AppSettings();
        settings.Layout.TitleBarHiddenSpineWidth = width;
        var note = new StickyNote { IsTitleBarHidden = true, Content = "body" };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, settings), new StorageService(temp.Path));
        try
        {
            window.Show();
            window.UpdateLayout();
            var spine = Assert.IsType<System.Windows.Shapes.Rectangle>(window.FindName("TitleBarHiddenSpine"));
            Assert.Equal(expectedRadius, spine.RadiusX);
            Assert.Equal(expectedRadius, spine.RadiusY);
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
            // 端に貼り付ける置き方は以前の見た目のまま。角も丸めない。
            Assert.Equal(0, spine.RadiusX);
            Assert.Equal(0, spine.RadiusY);
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

    // 畳んで1行になると画像ではなく画像パスの文字が出るので、余白は文字のときと
    // 同じに戻す。詰めたままだと文字が縁に貼り付き、1行ぶんの高さも余白のぶんだけ
    // 低くなって、隣に並べた普通の付箋と高さが揃わない。
    [WpfFact]
    public void FoldedImageOnlyNote_HasTheSameHeightAsAFoldedTextNote()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var settings = new AppSettings();

        StickyNoteWindow Open(string content)
        {
            var note = new StickyNote
            {
                Width = 300,
                Height = 200,
                IsFolded = true,
                IsTitleBarHidden = true,
                Content = content,
            };
            var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
            Directory.CreateDirectory(assetsDir);
            SavePng(System.IO.Path.Combine(assetsDir, "pasted.png"), CreateBitmapSource());
            var opened = new StickyNoteWindow(new StickyNoteViewModel(note, settings), storage);
            opened.Show();
            opened.UpdateLayout();
            return opened;
        }

        var textNote = Open("ああああ");
        var imageNote = Open("![](assets/pasted.png)");
        try
        {
            var imageBox = Assert.IsType<RichTextBox>(imageNote.FindName("ContentBox"));
            var textBox = Assert.IsType<RichTextBox>(textNote.FindName("ContentBox"));

            Assert.Equal(textBox.Padding, imageBox.Padding);
            Assert.Equal(textNote.Height, imageNote.Height);

            // 開くと画像が出るので、そこで初めて詰める。
            imageNote.ViewModel.IsFolded = false;
            imageNote.UpdateLayout();
            Assert.Equal(new Thickness(9, 0, 0, 0), imageBox.Padding);
        }
        finally { imageNote.Close(); textNote.Close(); }
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

    // 本文の文字を右クリックしたとき。当たった要素は Run で、そこから木を
    // 遡ると FlowDocument に行き当たる。FlowDocument は Visual ではないので、
    // VisualTreeHelper.GetParent は null ではなく例外を返す ―― 右クリック
    // するたびにアプリごと落ちていた。
    [WpfFact]
    public void ContentContextMenu_OnBodyText_DoesNotThrow()
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
            var run = box.Document.Blocks.OfType<Paragraph>()
                .SelectMany(p => p.Inlines).OfType<Run>()
                .First(r => r.Text.Contains("本文"));

            // 文字の上、段落、FlowDocument ―― どこから来ても落ちない。
            foreach (DependencyObject source in new DependencyObject[]
                     { run, run.Parent, box.Document })
            {
                OpenContentContextMenuOver(window, box, source);
                Assert.Equal(Visibility.Collapsed,
                    FindMenuItem(box.ContextMenu!, "画像のサイズ").Visibility);
                Assert.Contains("コピー", MenuHeaders(box.ContextMenu!));
            }

            // 画像の上から開けば、これまでどおり画像用の項目が出る。
            var image = Assert.Single(EnumerateImages(box.Document));
            OpenContentContextMenuOver(window, box, image);
            Assert.Equal(Visibility.Visible, FindMenuItem(box.ContextMenu!, "画像のサイズ").Visibility);
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

    // サイズ未指定の画像は縦横両方に収め、不要なスクロールバー幅を予約しない。
    [WpfTheory]
    // どちらも付箋より横に大きく、幅に合わせて縮小される画像。
    [InlineData(800, 300, false)]     // 横長。縮めても縦に収まるので右端まで使う
    [InlineData(800, 2000, true)]     // 縦長。高さに合わせて縮める
    public void ImageOnlyNote_FitsBothDimensionsWithoutReservingScrollBarRoom(
        int pixelWidth, int pixelHeight, bool heightLimited)
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
            if (heightLimited)
            {
                Assert.InRange(image.Height, 1, box.ActualHeight);
                Assert.Equal(box.ActualHeight, image.Height, 1);
                Assert.Equal((double)pixelHeight / pixelWidth, image.Height / image.Width, 6);
            }
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

    // 左右の余白を含め、本文の手前までを持ち手にする。
    [WpfTheory]
    [InlineData(3.0, 14.0)]
    [InlineData(0.0, 11.0)]
    [InlineData(8.0, 19.0)]
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
            Assert.Equal(new Thickness(0), handle.Margin);
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

            // Native capture is unavailable in some test hosts. Deliver the same
            // routed notification explicitly so this checks the cleanup handler.
            handle.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
            {
                RoutedEvent = UIElement.LostMouseCaptureEvent,
                Source = handle,
            });
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
    public void ToggleFold_ReadOnlyImageWithoutWidth_UpscalesAfterUnfold()
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
            Assert.Empty(EnumerateImages(contentBox.Document));

            InvokePrivate(window, "ToggleFold", (object?)null);
            var unfoldedImage = Assert.Single(EnumerateImages(contentBox.Document));

            Assert.True(unfoldedImage.Width > 2 / VisualTreeHelper.GetDpi(window).DpiScaleX);
            Assert.Equal(unfoldedImage.Width, unfoldedImage.Height, 8);
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

    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetPositionSeparation_PreservesFoldedWidth(bool folded)
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var note = new StickyNote
        {
            Title = "Title", Width = 420, Height = 320,
            FoldedWidth = 310, FoldedX = 100, FoldedY = 100,
            IsFolded = folded, IsPositionSeparated = true,
        };
        var window = new StickyNoteWindow(
            new StickyNoteViewModel(note, new AppSettings { EnableFoldAnimation = false }),
            new StorageService(temp.Path));
        try
        {
            window.Width = folded ? 310 : 420;
            InvokePrivate(window, "ResetPositionSeparation");
            Assert.Equal(folded ? 310 : 420, window.Width);
            if (folded) InvokePrivate(window, "ToggleFold", (object?)null);
            InvokePrivate(window, "ToggleFold", (object?)null);
            Assert.Equal(310, window.Width);
            Assert.Equal(310, note.FoldedWidth);
        }
        finally { window.Close(); }
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
            Assert.InRange(window.Width, window.MinWidth, 420);
            Assert.Equal(10, note.FoldedX);
            Assert.Equal(20, note.FoldedY);
            Assert.Equal(window.Width, note.FoldedWidth);
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

    /// <summary>幅だけ違う絵。貼り直されたかを大きさで見分けるのに使う。</summary>
    private static System.Windows.Media.Imaging.BitmapSource CreateBitmapSource(int width)
    {
        var pixels = new byte[width * 4];
        Array.Fill(pixels, (byte)255);
        return System.Windows.Media.Imaging.BitmapSource.Create(
            width, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);
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

    /// <summary>
    /// 画像以外のファイルは札になる。札は開く先を Tag に持ち、
    /// 本文のクリックを見ている側（ContentBox_PreviewMouseDown）がそれを拾う。
    /// </summary>
    [WpfFact]
    public void LoadContent_PlacesANonImageFileAsAChipThatKnowsWhatItOpens()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote { Content = "![report.pdf](assets/report.pdf)" };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        var report = System.IO.Path.Combine(assetsDir, "report.pdf");
        File.WriteAllText(report, "pdf");
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            InvokePrivate(window, "LoadContent", note.Content);
            var contentBox = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));

            // 画像としては描かれない。
            Assert.Empty(EnumerateImages(contentBox.Document));
            var chip = Assert.Single(EnumerateChips(contentBox.Document));
            // 開く先はクリックを見ている側が Tag から読む。
            Assert.Contains(report, chip.Tag?.ToString());
            Assert.Contains("report.pdf", EnumerateChipText(chip));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 札を右クリックしたときだけ、本文メニューの先頭に開く項目が出る。
    /// キーボードから開いたときは、直前の右クリックの記憶を持ち越さない。
    /// </summary>
    [WpfFact]
    public void FileChipMenu_AppearsOnlyOnAChip()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote { Content = "![report.pdf](assets/report.pdf)" };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        File.WriteAllText(System.IO.Path.Combine(assetsDir, "report.pdf"), "pdf");
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            InvokePrivate(window, "LoadContent", note.Content);
            var contentBox = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var chip = Assert.Single(EnumerateChips(contentBox.Document));
            var open = FindMenuItem(contentBox.ContextMenu!, LocalizationService.T("FileChipOpen"));
            var openWith = FindMenuItem(contentBox.ContextMenu!, LocalizationService.T("FileChipOpenWith"));

            // 本文の何も無いところを右クリックしたときは出ない。
            InvokePrivate(window, "CaptureContextMenuFileChip", contentBox);
            InvokePrivate(window, "UpdateFileChipMenuItems", false);
            Assert.Equal(Visibility.Collapsed, open.Visibility);
            Assert.Equal(Visibility.Collapsed, openWith.Visibility);

            InvokePrivate(window, "CaptureContextMenuFileChip", chip);
            InvokePrivate(window, "UpdateFileChipMenuItems", false);
            Assert.Equal(Visibility.Visible, open.Visibility);
            Assert.Equal(Visibility.Visible, openWith.Visibility);
            Assert.True(openWith.IsEnabled);

            InvokePrivate(window, "UpdateFileChipMenuItems", true);
            Assert.Equal(Visibility.Collapsed, open.Visibility);
        }
        finally
        {
            window.Close();
        }
    }

    private static MenuItem FindMenuItem(ContextMenu menu, string header)
        => menu.Items.OfType<MenuItem>().Single(item => (item.Header as string) == header);

    /// <summary>
    /// 札のアイコンは、画面の画素にちょうど合う大きさで描いてもらい、縮めずに1対1で出す。
    /// 縮めるとにじんで、文字に比べてアイコンだけが見にくくなる。白い下地の上に載せる。
    /// </summary>
    [WpfFact]
    public void FileChip_DrawsItsIconAtTheExactDisplaySizeOnAWhiteTile()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote { Content = "![report.pdf](assets/report.pdf)", FontSize = 13 };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        File.WriteAllText(System.IO.Path.Combine(assetsDir, "report.pdf"), "pdf");
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Show();
            InvokePrivate(window, "LoadContent", note.Content);
            var contentBox = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var chip = Assert.Single(EnumerateChips(contentBox.Document));

            var row = Assert.IsType<StackPanel>(chip.Child);
            var tile = Assert.IsType<Border>(row.Children[0]);
            var image = Assert.IsType<Image>(tile.Child);
            var dpi = VisualTreeHelper.GetDpi(window).DpiScaleX;
            var expectedPixels = (int)Math.Round(Math.Round(13 * 1.7) * dpi);

            // 描いてもらった大きさと、画面に出す大きさが1対1（縮めない）。
            var bitmap = Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(image.Source);
            Assert.Equal(expectedPixels, bitmap.PixelWidth);
            Assert.Equal(expectedPixels, image.Width * dpi, 3);
            // 白い下地は透けさせる（不透明の白い四角にはしない）。
            var brush = Assert.IsType<SolidColorBrush>(tile.Background);
            Assert.Equal(255, brush.Color.R);
            Assert.InRange(brush.Color.A, 1, 254);
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<Border> EnumerateChips(FlowDocument document)
        => document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines)
            .OfType<InlineUIContainer>()
            .Select(container => container.Child)
            .OfType<Border>();

    private static string EnumerateChipText(Border chip)
        => string.Concat(((Panel)chip.Child).Children.OfType<TextBlock>().Select(text => text.Text));

    /// <summary>
    /// draw.io へ渡した図が保存されたら、付箋の絵を貼り直す。
    /// </summary>
    [WpfFact]
    public void ReferencedImage_RedrawsOnExternalSaveWithoutLaunchingDrawio()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote { Content = "![zu](assets/zu.png)" };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        var diagram = System.IO.Path.Combine(assetsDir, "zu.png");
        SavePng(diagram, CreateBitmapSource());
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            window.Show();
            InvokePrivate(window, "LoadContent", note.Content);
            var contentBox = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var before = Assert.Single(EnumerateImages(contentBox.Document));
            var beforeWidth = ((System.Windows.Media.Imaging.BitmapSource)before.Source).PixelWidth;

            var watches = GetPrivateField<Dictionary<string, ExternalFileMonitor>>(window, "_referencedFileWatches");
            Assert.Single(watches);
            Assert.False(ExternalFilePoller.Shared.Contains(watches[diagram]));
            // draw.io が保存し直したつもりで、大きさの違う絵に入れ替える。
            SavePng(diagram, CreateBitmapSource(beforeWidth + 7));

            Assert.True(WaitForDispatcher(window, () =>
                EnumerateImages(contentBox.Document).Any(image =>
                    ((System.Windows.Media.Imaging.BitmapSource)image.Source).PixelWidth != beforeWidth),
                TimeSpan.FromSeconds(15)));
            // 原子的な置換保存や削除後の再作成でも同じパスの監視が残る。
            File.Delete(diagram);
            Assert.True(WaitForDispatcher(window, () => !EnumerateImages(contentBox.Document).Any(), TimeSpan.FromSeconds(5)));
            SavePng(diagram, CreateBitmapSource(beforeWidth + 9));
            Assert.True(WaitForDispatcher(window, () => EnumerateImages(contentBox.Document).Any(image =>
                ((System.Windows.Media.Imaging.BitmapSource)image.Source).PixelWidth == beforeWidth + 9), TimeSpan.FromSeconds(5)));
            InvokePrivate(window, "LoadContent", "no image");
            Assert.Empty(watches);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>ワーカースレッドからの知らせを待つ間も、UIスレッドを回しておく。</summary>
    private static bool WaitForDispatcher(System.Windows.Window window, Func<bool> condition, TimeSpan timeout)
    {
        var until = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < until)
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            if (condition()) return true;
            Thread.Sleep(50);
        }
        return condition();
    }

    /// <summary>
    /// 画像の右クリックから、絵としてもファイルとしてもコピーできる。
    /// 絵は貼り付け先に合わせて 2 つの形で渡す。
    /// </summary>
    [WpfFact]
    public void CopyImage_PutsThePictureAndTheFileOnTheClipboard()
    {
        EnsureApplication();
        using var temp = new TempDataDirectory();
        var storage = new StorageService(temp.Path);
        var note = new StickyNote { Content = "![shot](assets/shot.png)" };
        var assetsDir = storage.GetNoteAssetsDirectoryPath(note.Id);
        Directory.CreateDirectory(assetsDir);
        var image = System.IO.Path.Combine(assetsDir, "shot.png");
        SavePng(image, CreateBitmapSource());
        var vm = new StickyNoteViewModel(note, new AppSettings());
        var window = new StickyNoteWindow(vm, storage);
        try
        {
            InvokePrivate(window, "LoadContent", note.Content);
            var contentBox = Assert.IsType<RichTextBox>(window.FindName("ContentBox"));
            var rendered = Assert.Single(EnumerateImages(contentBox.Document));
            var contexts = (System.Collections.IDictionary)typeof(StickyNoteWindow)
                .GetField("_markdownImageContexts", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window)!;
            var context = contexts[rendered]!;

            // 実際のクリップボードは共有の場所なので、渡す中身だけを見る。
            var picture = (DataObject)InvokePrivateResult(window, "BuildImageDataObject",
                typeof(System.Windows.Media.Imaging.BitmapSource),
                InvokePrivateResult(window, "GetOrLoadNormalizedImage", typeof(string), image))!;
            Assert.True(picture.ContainsImage());
            // 透明を保てる形も添えておく。
            Assert.True(picture.GetDataPresent("PNG"));

            var file = (DataObject)InvokePrivateResult(window, "BuildFileDataObject", typeof(string), image)!;
            Assert.Equal(image, Assert.Single(file.GetFileDropList().Cast<string>()));
            // 貼り付け先で「移動」にならないよう、コピーを指定しておく。
            var effect = Assert.IsType<MemoryStream>(file.GetData("Preferred DropEffect"));
            Assert.Equal(1u, BitConverter.ToUInt32(effect.ToArray()));

            // 実クリップボードは触らないが、取れないときに黙って終わらないことは見る。
            File.Delete(image);
            InvokePrivate(window, "CopyMarkdownImage", context, true);
            var overlay = Assert.IsType<TextBlock>(window.FindName("SizeOverlayText"));
            Assert.Equal(LocalizationService.T("CopyImageFailed"), overlay.Text);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>戻り値のある非公開メソッドを、引数の型を指定して呼ぶ。</summary>
    private static object? InvokePrivateResult(object target, string name, Type parameterType, object argument)
        => target.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static,
                null, [parameterType], null)!
            .Invoke(target, [argument]);

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
