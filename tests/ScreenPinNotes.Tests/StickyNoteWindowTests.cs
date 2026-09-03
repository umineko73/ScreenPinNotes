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
            AssertMenuHasIconAndColorOptions(bodyEditBox.ContextMenu);
            AssertMenuHasIconAndColorOptions(contentBox.ContextMenu);

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

    private static void AssertMenuHasIconAndColorOptions(ContextMenu contextMenu)
    {
        Assert.Contains(
            contextMenu.Items.OfType<MenuItem>(),
            item => Equals(item.Header, "アイコンを変更") || Equals(item.Header, "Change icon"));
        Assert.Contains(
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

    private static void EnsureApplication()
    {
        if (Application.Current == null)
            _ = new App();
        if (Application.Current!.Dispatcher.CheckAccess())
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

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
