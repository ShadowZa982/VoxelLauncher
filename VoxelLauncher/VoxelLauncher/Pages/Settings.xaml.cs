using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using VoxelLauncher.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace VoxelLauncher.Pages
{
    public sealed partial class Settings : Page
    {
        private readonly AppViewModel _vm;

        public Settings()
        {
            this.InitializeComponent();
            _vm = (Application.Current as App)?.ViewModel ?? new AppViewModel();
            this.Loaded += Settings_Loaded;
            UpdateCheckToggle.Toggled += UpdateCheckToggle_Toggled;
            LoginNotifToggle.Toggled += LoginNotifToggle_Toggled;
            DisableAllNotifToggle.Toggled += DisableAllNotifToggle_Toggled;
        }

        private async void Settings_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadSettingsAsync();
            UpdateVersionInfo();
        }

        private async Task LoadSettingsAsync()
        {
            var config = await SettingsHelper.LoadAsync();

            UpdateCheckToggle.IsOn = config.CheckForUpdates;
            LoginNotifToggle.IsOn = config.ShowLoginNotification;
            DisableAllNotifToggle.IsOn = config.DisableAllNotifications;

            FolderPathText.Text = config.MinecraftFolder;
        }

        private void UpdateVersionInfo()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version ?? new Version(1, 0, 0);
            var buildDate = File.GetLastWriteTime(assembly.Location);

            VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
            BuildDateText.Text = $"Build: {buildDate:yyyy-MM-dd HH:mm}";
        }
        private async void ChangeFolder_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            folderPicker.FileTypeFilter.Add("*");

            var hwnd = WindowNative.GetWindowHandle(App.CurrentApp.MainWindowInstance!);
            InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder == null) return;

            var newPath = folder.Path;
            var config = await SettingsHelper.LoadAsync();
            config.MinecraftFolder = newPath;
            await SettingsHelper.SaveAsync(config);
            FolderPathText.Text = newPath;
            _vm.MinecraftFolder = newPath;
            await new ContentDialog
            {
                Title = "Thành công",
                Content = "Thư mục game đã được thay đổi.\n" +
                          "Vui lòng chọn lại profile để áp dụng.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            }.ShowAsync();
        }
        private async void DisableAllNotifToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (DisableAllNotifToggle.IsOn)
            {
                UpdateCheckToggle.IsOn = false;
                LoginNotifToggle.IsOn = false;
            }

            var config = await SettingsHelper.LoadAsync();
            config.DisableAllNotifications = DisableAllNotifToggle.IsOn;
            config.CheckForUpdates = UpdateCheckToggle.IsOn;
            config.ShowLoginNotification = LoginNotifToggle.IsOn;
            await SettingsHelper.SaveAsync(config);
        }
        private async void UpdateCheckToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var config = await SettingsHelper.LoadAsync();
            config.CheckForUpdates = UpdateCheckToggle.IsOn;
            await SettingsHelper.SaveAsync(config);
        }

        private async void LoginNotifToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var config = await SettingsHelper.LoadAsync();
            config.ShowLoginNotification = LoginNotifToggle.IsOn;
            await SettingsHelper.SaveAsync(config);
        }
    }
}