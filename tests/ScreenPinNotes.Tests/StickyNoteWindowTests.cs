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
            InvokePrivate(window, "Color_Click", colorButton, new RoutedEventArgs());
            Assert.False(colorPopup.IsOpen);

            InvokePrivate(window, "Color_Click", colorButton, new RoutedEventArgs());
            Assert.True(colorPopup.IsOpen);

            InvokePrivate(window, "Icon_Click", iconButton, new RoutedEventArgs());
            Assert.True(iconPopup.IsOpen);
            Assert.False(colorPopup.IsOpen);
            InvokePrivate(window, "Icon_Click", iconButton, new RoutedEventArgs());
            Assert.False(iconPopup.IsOpen);
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
