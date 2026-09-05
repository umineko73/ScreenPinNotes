using System.Windows;
using System.Windows.Controls;
using ScreenPinNotes.Services;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;

namespace ScreenPinNotes.Views;

public sealed class LinkEditDialog : Window
{
    private readonly TextBox _label;
    private readonly TextBox _url;
    public string LinkLabel => _label.Text;
    public string LinkTarget => _url.Text.Trim();

    public LinkEditDialog(Window owner, string label, string url)
    {
        Owner = owner;
        Title = LocalizationService.T("EditMarkdownLink");
        Width = 560; Height = 340; MinWidth = 360; MinHeight = 280;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = owner.Topmost;
        var grid = new Grid { Margin = new Thickness(16) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            grid.RowDefinitions.Add(new RowDefinition { Height = height });
        void Add(UIElement element, int row) { Grid.SetRow(element, row); grid.Children.Add(element); }
        Add(new TextBlock { Text = LocalizationService.T("LinkDisplayText") }, 0);
        _label = new TextBox { Text = label, Margin = new Thickness(0, 6, 0, 12) };
        Add(_label, 1);
        Add(new TextBlock { Text = "URL" }, 2);
        _url = new TextBox { Text = url, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 6, 0, 12) };
        Add(_url, 3);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(LinkTarget) || LinkTarget.IndexOfAny(['\r', '\n', '<', '>']) >= 0 || !LinkDetector.IsLink(LinkTarget))
            {
                System.Windows.MessageBox.Show(this, LocalizationService.T("InvalidLinkTarget"), Title);
                _url.Focus();
                return;
            }
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = LocalizationService.T("Cancel"), IsCancel = true, MinWidth = 80 });
        Add(buttons, 4);
        Content = grid;
        Loaded += (_, _) => _label.Focus();
    }
}
