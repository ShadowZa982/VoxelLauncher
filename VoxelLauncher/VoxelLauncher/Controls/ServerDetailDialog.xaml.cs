// Controls/ServerDetailDialog.xaml.cs
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using VoxelLauncher.Models;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;

namespace VoxelLauncher.Controls
{
    public sealed partial class ServerDetailDialog : UserControl
    {
        private ContentDialog _currentLinkDialog = null;
        public Server Server => (Server)DataContext;

        public ServerDetailDialog()
        {
            this.InitializeComponent();
        }

        private void CopyIpPC_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (Server?.IpPC != null)
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(Server.IpPC);
                Clipboard.SetContent(dataPackage);
            }
        }

        private void CopyIpPE_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (Server?.IpPE != null)
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(Server.IpPE);
                Clipboard.SetContent(dataPackage);
            }
        }

        private async void ShowLinkDialog(string title, string link)
        {
            if (string.IsNullOrWhiteSpace(link)) return;
            if (_currentLinkDialog != null)
            {
                try { _currentLinkDialog.Hide(); } catch { }
                _currentLinkDialog = null;
            }
            if (this.XamlRoot == null)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi: XamlRoot chưa sẵn sàng!");
                return;
            }

            this.DispatcherQueue.TryEnqueue(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    PrimaryButtonText = "Copy link",
                    CloseButtonText = "Đóng",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot,
                    Content = new TextBlock
                    {
                        Text = link,
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                        Margin = new Thickness(0, 8, 0, 8)
                    }
                };

                _currentLinkDialog = dialog;

                var result = await dialog.ShowAsync();

                _currentLinkDialog = null;

                if (result == ContentDialogResult.Primary)
                {
                    var dataPackage = new DataPackage();
                    dataPackage.SetText(link);
                    Clipboard.SetContent(dataPackage);
                }
            });
        }

        private void Website_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(Server?.Website))
                ShowLinkDialog("Website", Server.Website);
        }

        private void Facebook_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(Server?.Facebook))
                ShowLinkDialog("Facebook", Server.Facebook);
        }

        private void Discord_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(Server?.Discord))
                ShowLinkDialog("Discord", Server.Discord);
        }
    }
}