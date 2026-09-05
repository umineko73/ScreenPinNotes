using System.Windows;
using System.Windows.Controls;
using ScreenPinNotes.Views;

namespace ScreenPinNotes.Tests;

public class LinkEditDialogTests
{
    [WpfFact]
    public void LongUrlWrapsAndExpandsWithDialog()
    {
        if (Application.Current == null)
        {
            var app = new ScreenPinNotes.App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
        var owner = new Window();
        owner.Show();
        var target = "https://example.com/" + new string('a', 2000);
        var dialog = new LinkEditDialog(owner, "label", target);
        try
        {
            dialog.Show();
            dialog.UpdateLayout();
            var grid = Assert.IsType<Grid>(dialog.Content);
            var url = grid.Children.OfType<TextBox>().Single(box => box.Text == target);
            Assert.Equal(TextWrapping.Wrap, url.TextWrapping);
            Assert.Equal(ScrollBarVisibility.Disabled, url.HorizontalScrollBarVisibility);
            var width = url.ActualWidth;
            dialog.Width += 120;
            dialog.UpdateLayout();
            Assert.True(url.ActualWidth > width + 100);
            Assert.Equal(target, dialog.LinkTarget);
        }
        finally { dialog.Close(); owner.Close(); }
    }
}
