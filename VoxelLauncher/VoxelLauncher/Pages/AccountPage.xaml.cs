using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Security.Principal;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Windows.Security;
using System.Text.Json;
using System.Threading.Tasks;
using VoxelLauncher.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace VoxelLauncher.Pages
{
    public sealed partial class AccountPage : Page, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private AppViewModel _vm = null!;
        private const string SettingsFile = "voxel_settings.json";
        private bool _isLoadingFromFile = false;

        public AccountPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is AppViewModel vm)
            {
                _vm = vm;
                this.DataContext = _vm;
                _vm.RefreshAccountType();

                _ = _vm.LoadVoxelSettingsAsync().ContinueWith(_ =>
                {
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        LoadAccountInfo();
                    });
                });

                this.Loaded += async (s, e) =>
                {
                    await Task.Delay(100);
                    LoadHighPerformanceState();
                };
            }
        }

        #region === TẢI THÔNG TIN ===
        private async void LoadAccountInfo()
        {
            if (_vm == null || !_vm.IsLoggedIn) return;

            var nameFromFile = _vm.GetUserNameFromFile();
            if (!string.IsNullOrEmpty(nameFromFile))
                _vm.UserName = nameFromFile;

            UsernameText.Text = _vm.UserName;
            UuidText.Text = _vm.Session?.UUID ?? "Offline Mode";
            LoginTimeText.Text = _vm.LoginTime != DateTime.MinValue
                ? _vm.LoginTime.ToString("HH:mm:ss - dd/MM/yyyy")
                : "Chưa ghi nhận";
            PlayTimeText.Text = FormatPlayTime(_vm.TotalPlayTime);

            var isMicrosoft = _vm.IsMicrosoftAccount;
            TierText.Text = isMicrosoft ? "Microsoft" : "Offline";
            TierLogo.Source = new BitmapImage(new Uri(
                isMicrosoft
                    ? "ms-appx:///Assets/Icons/microsoft.png"
                    : "ms-appx:///Assets/Icons/offline.png"
            ));
            XuidText.Text = _vm.Xuid ?? "N/A";
            ClientIdText.Text = _vm.ClientId ?? "N/A";
            FolderPathText.Text = _vm.MinecraftFolder;

            if (_vm.CurrentProfile != null)
            {
                RamSlider.Value = _vm.CurrentProfile.RamMb;
            }

            await Load3DSkinAsync();
        }


        #endregion

        #region === SLIDER & TOGGLE ===
        private async void RamSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_vm?.CurrentProfile != null && e.NewValue != e.OldValue)
            {
                _vm.CurrentProfile.RamMb = (int)e.NewValue;
                await _vm.SaveVoxelSettingsAsync();
                OnPropertyChanged(nameof(FormattedRam));
            }
        }

        private async void HighPerformanceToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoadingFromFile) { _isLoadingFromFile = false; return; }

            _vm.HighPerformance = HighPerformanceToggle.IsOn; 

            if (HighPerformanceToggle.IsOn)
            {
                await ApplyHighPerformanceAsync();
                StatusText.Text = "HOÀN TẤT! HIỆU NĂNG CAO ĐÃ BẬT";
                StatusBorder.Background = new SolidColorBrush(Microsoft.UI.Colors.Green) { Opacity = 0.2 };
            }
            else
            {
                await ResetToDefaultAsync();
                StatusText.Text = "ĐÃ TẮT HIỆU NĂNG CAO";
                StatusBorder.Background = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.2 };
            }

            await _vm.SaveVoxelSettingsAsync(); 
            await Task.Delay(2000);
            if (!HighPerformanceToggle.IsOn)
            {
                StatusText.Text = "Mặc định: Chưa bật tối ưu";
                StatusBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(32, 16, 185, 129));
            }
        }

        private async void ApplyNowButton_Click(object sender, RoutedEventArgs e)
        {
            HighPerformanceToggle.IsOn = true;
            await Task.Delay(100);
            HighPerformanceToggle_Toggled(sender, e);
        }
        #endregion

        #region === TỐI ƯU HIỆU NĂNG ===
        private async Task ApplyHighPerformanceAsync()
        {
            if (_vm.CurrentProfile == null) return;
            int ramMb = (int)RamSlider.Value;
            _vm.CurrentProfile.RamMb = ramMb;

            // === JVM ARGS ===
            var jvmArgs = new List<string>
            {
                "-XX:+UnlockExperimentalVMOptions", "-XX:+UnlockDiagnosticVMOptions",
                "-XX:+UseZGC", "-XX:+DisableExplicitGC", "-XX:+AlwaysPreTouch",
                "-XX:+ParallelRefProcEnabled", "-XX:+UseCompressedOops",
                "-XX:+UseStringDeduplication", "-XX:ReservedCodeCacheSize=512m",
                "-XX:+UseCodeCacheFlushing", "-XX:+UseFMA", "-XX:+UseAES",
                "-XX:+UseAESIntrinsics", "-XX:+TieredCompilation", "-XX:MaxInlineLevel=18",
                "-Dsun.java2d.opengl=true", "-Dsun.java2d.d3d=false", "-Dsun.java2d.noddraw=true",
                "-Djava.awt.headless=false", "-Dminecraft.launcher.brand=VOXELLAUNCHER",
                "-Dminecraft.launcher.version=3.0", "-Dfml.earlyprogresswindow=false",
                "-Dlog4j2.formatMsgNoLookups=true",
                "-javaagent:BoostAgent.jar",
                "-Dfml.coreMods.load=com.voxel.core.VoxelCoremod",
                $"-Xms128M", $"-Xmx{ramMb}M"
            };
            _vm.CurrentProfile.JvmArgs = string.Join(" ", jvmArgs);

            // === OPTIONS.TXT ===
            var options = new[]
            {
                "autoJump:false", "maxFps:260", "renderDistance:12", "simulationDistance:5",
                "graphicsMode:fabulous", "fancyGraphics:true", "enableVsync:false", "vsync:false",
                "fboEnable:true", "particles:2", "mipmapLevels:0", "entityDistanceScaling:0.5",
                "guiScale:0", "cloudHeight:0", "ao:true", "biomeBlendRadius:0",
                "entityShadows:false", "fullscreen:false", "gamma:1.0", "fov:70",
                "chunkBuilder:threaded", "useVbo:true", "resourcePacks:[]", "language:vi_vn"
            };
            await File.WriteAllLinesAsync(Path.Combine(_vm.MinecraftFolder, "options.txt"), options);

            ForceJava21AndGPU();

            await File.WriteAllTextAsync(
                Path.Combine(_vm.MinecraftFolder, "voxel_settings.json"),
                JsonSerializer.Serialize(new { Ram = ramMb, Fps = "UNLIMITED", Mode = "GOD" })
            );

            await SaveProfilesAsync();
            AppendLog("TỐI ƯU FPS HOÀN TẤT!", "#00FF00");
        }

        private void ForceJava21AndGPU()
        {
            try
            {
                var javaBin = Path.Combine(_vm.MinecraftFolder, "runtime", "bin");
                if (Directory.Exists(javaBin))
                {
                    var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                    if (!path.Contains(javaBin, StringComparison.OrdinalIgnoreCase))
                    {
                        Environment.SetEnvironmentVariable("PATH", javaBin + ";" + path);
                    }
                }

                try
                {
                    var key = Microsoft.Win32.Registry.CurrentUser
                        .CreateSubKey(@"Software\NVIDIA Corporation\Global\NVTweak");
                    key.SetValue("VsyncControl", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    key.Close();
                }
                catch { }

                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;

                if (IsRunningAsAdministrator())
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "powercfg",
                        Arguments = "-setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        Verb = "runas" 
                    };
                    Process.Start(startInfo)?.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OPTIMIZE ERROR] {ex.Message}");
            }
        }

        private bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private async Task SaveProfilesAsync()
        {
            await Task.CompletedTask;
        }

        private void AppendLog(string message, string color)
        {
            Debug.WriteLine($"[OPTIMIZE] {message}");
        }

        private async Task ResetToDefaultAsync()
        {
            try
            {
                var mcFolder = _vm.MinecraftFolder;

                var optionsPath = Path.Combine(mcFolder, "options.txt");
                if (File.Exists(optionsPath))
                    File.Delete(optionsPath);

                if (_vm.CurrentProfile != null)
                    _vm.CurrentProfile.JvmArgs = null;

                var javaBin = Path.Combine(mcFolder, "runtime", "bellsoft-jdk21", "bin");
                var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                if (path.Contains(javaBin, StringComparison.OrdinalIgnoreCase))
                {
                    path = path.Replace(javaBin + ";", "", StringComparison.OrdinalIgnoreCase)
                               .Replace(javaBin, "");
                    Environment.SetEnvironmentVariable("PATH", path);
                }

                try
                {
                    var key = Microsoft.Win32.Registry.CurrentUser
                        .OpenSubKey(@"Software\NVIDIA Corporation\Global\NVTweak", true);
                    if (key?.GetValue("VsyncControl") != null)
                        key.DeleteValue("VsyncControl");
                    key?.Close();
                }
                catch { }

                if (IsRunningAsAdministrator())
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "powercfg",
                        Arguments = "-setactive 381b4222-f694-41f0-9685-ff5bb260df2e",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        Verb = "runas"
                    };
                    Process.Start(startInfo)?.WaitForExit(5000);
                }

                AppendLog("ĐÃ KHÔI PHỤC MẶC ĐỊNH!", "#FF0000");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RESET ERROR] {ex.Message}");
            }
        }
        #endregion

        #region === GIAO DIỆN ===
        public string FormattedRam => _vm?.CurrentProfile != null
            ? $"{_vm.CurrentProfile.RamMb} MB"
            : "4096 MB";

        private async Task Load3DSkinAsync()
        {
            try
            {
                string bodyUrl = _vm.Session?.UUID != null
                    ? $"https://mc-heads.net/body/{_vm.Session.UUID}"
                    : Path.Combine(_vm.MinecraftFolder, "cachedImages", "skin.png");

                if (!File.Exists(bodyUrl) && string.IsNullOrEmpty(_vm.Session?.UUID))
                    bodyUrl = "https://mc-heads.net/body/steve";

                var bitmap = new BitmapImage();
                if (bodyUrl.StartsWith("http"))
                    bitmap.UriSource = new Uri(bodyUrl);
                else
                {
                    var file = await StorageFile.GetFileFromPathAsync(bodyUrl);
                    using var stream = await file.OpenAsync(FileAccessMode.Read);
                    await bitmap.SetSourceAsync(stream);
                }

                var tcs = new TaskCompletionSource<bool>();
                bitmap.ImageOpened += (s, e) => tcs.SetResult(true);
                bitmap.ImageFailed += (s, e) => tcs.SetResult(false);
                SkinImage.Source = bitmap;
                await tcs.Task;
            }
            catch
            {
                SkinImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/Icons/player.png"));
            }
        }

        private string FormatPlayTime(TimeSpan ts)
            => ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes}m" : $"{ts.Minutes}m {ts.Seconds}s";

        private void BackButton_Click(object sender, RoutedEventArgs e)
            => Frame?.GoBack();

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(_vm.MinecraftFolder) { UseShellExecute = true, Verb = "open" }); }
            catch { }
        }

        private async void CopyUuidButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(UuidText.Text) && UuidText.Text != "Offline Mode")
            {
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                data.SetText(UuidText.Text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                CopyUuidTeachingTip.IsOpen = true;
                await Task.Delay(2000);
                CopyUuidTeachingTip.IsOpen = false;
            }
        }

        private async void ChangeSkinButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.IsMicrosoftAccount)
                await Launcher.LaunchUriAsync(new Uri("https://www.minecraft.net/en-us/msaprofile/mygames/editskin"));
            else if (_vm.IsOfflineAccount)
                await PickAndSetSkinAsync();
            else
                await new ContentDialog { Title = "Chưa đăng nhập", Content = "Vui lòng đăng nhập trước.", CloseButtonText = "OK", XamlRoot = this.Content.XamlRoot }.ShowAsync();
        }

        private async Task PickAndSetSkinAsync()
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            picker.FileTypeFilter.Add(".png");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle((Application.Current as App)?.Window?.CoreWindow));

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            try
            {
                var skinPath = Path.Combine(_vm.MinecraftFolder, "cachedImages", "skin.png");
                Directory.CreateDirectory(Path.GetDirectoryName(skinPath)!);
                await file.CopyAsync(await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(skinPath)!), "skin.png", NameCollisionOption.ReplaceExisting);
                await Load3DSkinAsync();

                await new ContentDialog { Title = "Thành công", Content = "Skin đã được thay đổi!", CloseButtonText = "OK", XamlRoot = this.Content.XamlRoot }.ShowAsync();
            }
            catch (Exception ex)
            {
                await new ContentDialog { Title = "Lỗi", Content = $"Không thể thay skin: {ex.Message}", CloseButtonText = "OK", XamlRoot = this.Content.XamlRoot }.ShowAsync();
            }
        }

        private void PerformanceModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
        #endregion

        #region === TẢI TRẠNG THÁI TOGGLE ===
        private void LoadHighPerformanceState()
        {
            if (HighPerformanceToggle == null) return;

            _isLoadingFromFile = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                HighPerformanceToggle.IsOn = _vm.HighPerformance;
                _isLoadingFromFile = false;

                StatusText.Text = _vm.HighPerformance
                    ? "HOÀN TẤT! HIỆU NĂNG CAO ĐÃ BẬT"
                    : "Mặc định: Chưa bật tối ưu";

                StatusBorder.Background = new SolidColorBrush(
                    _vm.HighPerformance ? Microsoft.UI.Colors.Green : Windows.UI.Color.FromArgb(32, 16, 185, 129)
                )
                { Opacity = 0.2 };
            });
        }
        #endregion

        private class GameSettings
        {
            public bool BootFps { get; set; }
            public int RamMb { get; set; } = 4096;
            public string FpsCap { get; set; } = "240";
            public string Graphics { get; set; } = "1";
            public int RenderDistance { get; set; } = 12;
            public bool HighPerformance { get; set; } = false;
        }
    }


}