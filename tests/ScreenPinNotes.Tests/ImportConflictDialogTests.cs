using ScreenPinNotes.Models;
using ScreenPinNotes.Services;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

public class ImportConflictDialogTests
{
    [WpfFact]
    public void ClosingWithoutChoosingDefaultsToSkip()
    {
        WpfApplicationFixture.Ensure();
        var dialog = new ImportConflictDialog(new StickyNote { Title = "Incoming" });
        Assert.Equal(StorageService.ImportConflictAction.Skip, dialog.Action);
        var panel = Assert.IsType<System.Windows.Controls.StackPanel>(dialog.Content);
        Assert.Equal(3, panel.Children.OfType<System.Windows.Controls.Button>().Count());
        dialog.Close();
    }

    [Theory]
    [InlineData("ja", "ImportConflictOverwrite", "上書きする")]
    [InlineData("ja", "ImportConflictRename", "名前を変える（別名で追加）")]
    [InlineData("ja", "ImportConflictSkip", "インポートしない")]
    [InlineData("en", "ImportConflictOverwrite", "Overwrite")]
    [InlineData("en", "ImportConflictRename", "Rename (keep both)")]
    [InlineData("en", "ImportConflictSkip", "Do not import")]
    public void ChoicesAreLocalized(string culture, string key, string expected)
        => Assert.Equal(expected, LocalizationService.T(key, culture));
}
