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

using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Views;

public partial class AboutWindow : Window
{
    private const string RepoUrl = "https://github.com/umineko73/ScreenPinNotes";

    public AboutWindow()
    {
        InitializeComponent();
        Title = LocalizationService.T("AboutTitle");
        AppIconImage.Source = LoadAppIconImage();
        VersionText.Text = "v" + GetVersionString();
        DescriptionText.Text = LocalizationService.T("AboutDescription");
        LicenseText.Text = LocalizationService.T("AboutLicense");
        RepoLink.NavigateUri = new Uri(RepoUrl);
        CloseButton.Content = LocalizationService.T("Close");
    }

    private static string GetVersionString()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    // タスクトレイのアイコンと同じ app.ico を、画像として表示できる形に変換する
    private static BitmapSource LoadAppIconImage()
    {
        var uri = new Uri("pack://application:,,,/app.ico");
        using var stream = System.Windows.Application.GetResourceStream(uri)!.Stream;
        using var icon = new System.Drawing.Icon(stream, 48, 48);
        var bitmap = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        bitmap.Freeze();
        return bitmap;
    }

    private void RepoLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
