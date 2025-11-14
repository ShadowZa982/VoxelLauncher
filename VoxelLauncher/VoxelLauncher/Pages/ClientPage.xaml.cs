using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.Forge.Versions;
using CmlLib.Core.Installer.NeoForge.Versions;
using CmlLib.Core.Java;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Rules;
using CmlLib.Core.Version;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VoxelLauncher.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace VoxelLauncher.Pages
{
    public partial class ClientPageViewModel : ObservableObject
    {
        
        [ObservableProperty] private bool isLoggedIn;
        [ObservableProperty] private string userName = "";
        [ObservableProperty] private string selectedVersion = "";
    }
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => (value is bool b) && (b ^ (parameter?.ToString() == "inverse")) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
    public sealed partial class ClientPage : Page
    {

        private readonly List<string> _clientHeroImages = new()
        {
            "ms-appx:///Assets/backgrounds/bg.jpg",
            "ms-appx:///Assets/backgrounds/bg1.jpg",
            "ms-appx:///Assets/backgrounds/bg2.jpg",
            "ms-appx:///Assets/backgrounds/bg3.jpg",
            "ms-appx:///Assets/backgrounds/bg4.jpg",
            "ms-appx:///Assets/backgrounds/bg5.jpg",
            "ms-appx:///Assets/backgrounds/bg6.jpg"
        };

        // === TIMER & ANIMATION ===
        private int _clientHeroIndex = 0;
        private DispatcherTimer _clientHeroTimer = null!;

        private MinecraftJavaPathResolver? _javaResolver;
        private MinecraftJavaManifestResolver? _javaManifestResolver;
        private readonly HttpClient _javaHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        private bool _isClientMode = false;
        // === LAUNCHER CORE ===
        private MinecraftPath _mcPath = null!;
        private MinecraftJavaPathResolver? _javaPathResolver;
        private IJavaPathResolver? _customJavaPathResolver;
        private JavaVersion? _javaVersion;
        private JavaVersion? _customJavaVersion;
        private string? _customJavaPath;
        private string? _javaPath;
        private MinecraftJavaManifestResolver minecraftJava;
        private MinecraftLauncher _launcher = null!;
        private MSession? _session;
        private Run? _currentDownloadRun;
        // === PROFILE SYSTEM ===
        private readonly List<MinecraftProfile> _profiles = new();
        private MinecraftProfile? _currentProfile;
        private Task _loadProfileTask = Task.CompletedTask;
        // === DATA FOLDER ===
        private string AppDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxelLauncher");
        private string ProfilesFolder => Path.Combine(AppDataFolder, "profiles");
        private string ProfilesFile => Path.Combine(AppDataFolder, "profiles.json");
        private string CurrentProfileIdFile => Path.Combine(AppDataFolder, "current_profile.txt");
        // === UI & VM ===
        private AppViewModel _appVm = null!;
        private readonly List<string> _logCache = new();
        private bool _isFullLogOpen = false;
        private readonly ConcurrentDictionary<string, Task> _javaDownloadTasks = new();
        
        private List<CmlLib.Core.ModLoaders.FabricMC.FabricLoader> _fabricLoaders = new();
        private List<string> _forgeVersions = new();

        public ClientPage()
        {
            InitializeComponent();
            
            this.Loaded += ClientPage_Loaded;
            this.Loaded += async (s, e) => await LoadSavedSettingsAsync();
        }

        private async void ClientPage_Loaded(object sender, RoutedEventArgs e)
        {

            await LoadProfilesAsync();

            await TryLoadSessionAsync();
            await CheckInstallationStatusAsync();

            if (_currentProfile != null)
            {
                _ = EnsureJavaForVersionAsync(_currentProfile.Version);
            }

            await _appVm.LoadVoxelSettingsAsync();

            if (RamSlider != null)
                RamSlider.Value = _appVm.CurrentProfile.RamMb;

            _ = Task.Run(async () =>
            {
                var validIds = _profiles.Select(x => x.Id).ToHashSet();
                foreach (var dir in Directory.GetDirectories(ProfilesFolder))
                {
                    var id = Path.GetFileName(dir);
                    if (!validIds.Contains(id))
                        await ForceDeleteDirectoryAsync(dir);
                }
            });

            _clientHeroTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _clientHeroTimer.Tick += ClientHeroTimer_Tick;
            _clientHeroTimer.Start();

            _isClientMode = true;
            ClientHeroImage.Source = new BitmapImage(new Uri(_clientHeroImages[0]));
            await FadeElementAsync(ClientHeroImage, 1);

            await LoadGameSettingsAsync();
        }

        private async Task LoadGameSettingsAsync()
        {
            var file = Path.Combine(_appVm.MinecraftFolder, "voxel_settings.json");
            if (!File.Exists(file)) return;

            try
            {
                var json = File.ReadAllText(file);
                var settings = JsonSerializer.Deserialize<GameSettings>(json);

                _appVm.BootFpsEnabled = settings.BootFps;
                _appVm.RenderDistance = settings.RenderDistance;
                _appVm.FpsCap = settings.FpsCap;
                _appVm.GraphicsMode = settings.Graphics;

                _appVm.CurrentProfile.RamMb = settings.RamMb;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadGameSettingsAsync error: {ex.Message}");
            }
        }
        #region === LAUNCHER INITIALIZATION ===
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.Parameter is AppViewModel vm)
            {
                _appVm = vm;
                this.DataContext = _appVm;

                _appVm.PropertyChanged += AppVm_PropertyChanged;

                RefreshGamePath();

                _ = TryLoadSessionAsync();

            }
            base.OnNavigatedTo(e);
        }

        private void AppVm_PropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(AppViewModel.MinecraftFolder))
            {
                RefreshGamePath();

                if (_currentProfile != null)
                    _ = LoadProfileAsync(_currentProfile);
            }
        }

        private async Task InitializeLauncherAsync()
        {
            Directory.CreateDirectory(_appVm.MinecraftFolder);
            _mcPath = new MinecraftPath(_appVm.MinecraftFolder);
            _launcher = new MinecraftLauncher(_mcPath);

            InitializeJavaResolver();
        }
        #endregion
        private async void ClientHeroTimer_Tick(object sender, object e)
        {
            if (!_isClientMode) return;
            // 1. Fade out ảnh hiện tại
            await FadeElementAsync(ClientHeroImage, 0);
            // 2. Đổi ảnh
            _clientHeroIndex = (_clientHeroIndex + 1) % _clientHeroImages.Count;
            ClientHeroImage.Source = new BitmapImage(new Uri(_clientHeroImages[_clientHeroIndex]));
            // 3. Fade in ảnh mới
            await FadeElementAsync(ClientHeroImage, 1);
        }
        private Task FadeElementAsync(UIElement element, double to)
        {
            var anim = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(1200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var sb = new Storyboard();
            Storyboard.SetTarget(anim, element);
            Storyboard.SetTargetProperty(anim, "Opacity");
            sb.Children.Add(anim);
            var tcs = new TaskCompletionSource<bool>();
            sb.Completed += (s, e) => tcs.SetResult(true);
            sb.Begin();
            return tcs.Task;
        }
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _clientHeroTimer?.Stop();
            _isClientMode = false;
            base.OnNavigatedFrom(e);
        }
        #region === PROFILE SYSTEM ===
        private async Task LoadProfilesAsync()
        {
            Directory.CreateDirectory(ProfilesFolder);
            _profiles.Clear();
            if (File.Exists(ProfilesFile))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(ProfilesFile);
                    var loaded = JsonSerializer.Deserialize<List<MinecraftProfile>>(json) ?? new List<MinecraftProfile>();
                    _profiles.AddRange(loaded);
                }
                catch (Exception ex)
                {
                    AppendLog($"Lỗi đọc profiles.json: {ex.Message}", "#EF4444");
                    await File.WriteAllTextAsync(ProfilesFile, "[]");
                }
            }
            if (File.Exists(CurrentProfileIdFile))
            {
                var id = await File.ReadAllTextAsync(CurrentProfileIdFile);
                _currentProfile = _profiles.FirstOrDefault(p => p.Id == id);
            }
            if (_currentProfile != null && !_profiles.Contains(_currentProfile))
            {
                _currentProfile = null;
                await File.WriteAllTextAsync(CurrentProfileIdFile, "");
            }
            if (_currentProfile != null)
                await LoadProfileAsync(_currentProfile);
            else
                UpdateHeroQuickPlay();
        }
        private async Task LoadProfileAsync(MinecraftProfile profile)
        {
            var tcs = new TaskCompletionSource<bool>();
            _loadProfileTask = tcs.Task;

            this.DispatcherQueue.TryEnqueue(() =>
            {
                InstallButton.Content = "ĐANG TẢI PROFILE...";
                InstallButton.IsEnabled = false;
                PlayButton.IsEnabled = false;
                _appVm.CurrentProfileName = profile.Name;
                UpdateHeroQuickPlay();
                InitializeLauncherAsync();
            });

            var mcDir = _appVm.MinecraftFolder;
            var versionName = GetVersionName(profile);
            var currentVersionFile = Path.Combine(mcDir, "version_name.txt");
            var lastVersion = File.Exists(currentVersionFile) ? await File.ReadAllTextAsync(currentVersionFile) : "";

            if (lastVersion != versionName && !string.IsNullOrEmpty(lastVersion))
            {
                AppendLog($"Chuyển từ {lastVersion} → {versionName}: dọn dẹp phiên bản cũ...", "#F59E0B");

                var keepDirs = new HashSet<string> { "runtime", "assets", "libraries", "mods", "resourcepacks", "shaderpacks", "saves", "config" };
                var keepFiles = new HashSet<string> { "options.txt", "voxel_settings.json", "servers.dat", "player_name.txt", "version_name.txt", "ms_accounts.json" };

                var versionsDir = Path.Combine(mcDir, "versions");
                if (Directory.Exists(versionsDir))
                {
                    foreach (var dir in Directory.GetDirectories(versionsDir))
                    {
                        var name = Path.GetFileName(dir);
                        if (name != versionName && (name.StartsWith("1.") || name.Contains("fabric-loader") || name.Contains("-forge") || name.Contains("-neoforge")))
                        {
                            try
                            {
                                Directory.Delete(dir, true);
                                AppendLog($"Đã xóa versions/{name}", "#8B5CF6");
                            }
                            catch (Exception ex)
                            {
                                AppendLog($"Không xóa được {name}: {ex.Message}", "#EF4444");
                            }
                        }
                    }
                }

                foreach (var file in Directory.GetFiles(mcDir))
                {
                    var name = Path.GetFileName(file);
                    if (!keepFiles.Contains(name) && (name.EndsWith(".jar") || name.EndsWith(".json")))
                    {
                        try { File.Delete(file); }
                        catch { }
                    }
                }
            }

            if (Directory.Exists(profile.InstallPath))
            {
                var profileVersionsDir = Path.Combine(profile.InstallPath, "versions");
                var targetVersionsDir = Path.Combine(mcDir, "versions");

                if (Directory.Exists(profileVersionsDir))
                {
                    Directory.CreateDirectory(targetVersionsDir);
                    var versionFolder = Path.Combine(profileVersionsDir, versionName);
                    var targetVersionFolder = Path.Combine(targetVersionsDir, versionName);

                    if (Directory.Exists(versionFolder))
                    {
                        if (!Directory.Exists(targetVersionFolder))
                        {
                            CopyDirectory(versionFolder, targetVersionFolder);
                            
                        }
                        else
                        {
                            foreach (var file in Directory.GetFiles(versionFolder, "*", SearchOption.AllDirectories))
                            {
                                var relPath = Path.GetRelativePath(versionFolder, file);
                                var destFile = Path.Combine(targetVersionFolder, relPath);
                                var srcInfo = new FileInfo(file);
                                if (!File.Exists(destFile) || srcInfo.LastWriteTime > new FileInfo(destFile).LastWriteTime)
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                                    File.Copy(file, destFile, true);
                                }
                            }
                            
                        }
                    }
                }

                if (profile.Loader != "vanilla")
                {
                    var modsSrc = Path.Combine(profile.InstallPath, "mods");
                    var modsDest = Path.Combine(mcDir, "mods");
                    if (Directory.Exists(modsSrc))
                    {
                        Directory.CreateDirectory(modsDest);
                        foreach (var mod in Directory.GetFiles(modsSrc))
                        {
                            var dest = Path.Combine(modsDest, Path.GetFileName(mod));
                            if (!File.Exists(dest) || new FileInfo(mod).LastWriteTime > new FileInfo(dest).LastWriteTime)
                                File.Copy(mod, dest, true);
                        }
                    }
                }
            }

            var correctIcon = profile.Loader switch
            {
                "forge" or "neoforge" => "ms-appx:///Assets/Icons/forge.png",
                "fabric" or "quilt" => "ms-appx:///Assets/Icons/fabric.png",
                _ => "ms-appx:///Assets/Icons/vanilla.png"
            };
            if (profile.IconPath != correctIcon)
            {
                profile.IconPath = correctIcon;
                await SaveProfilesAsync();
               
            }
            _mcPath = new MinecraftPath(_appVm.MinecraftFolder);
            _launcher = new MinecraftLauncher(_mcPath);
            RamSlider.Value = profile.RamMb;

            await File.WriteAllTextAsync(currentVersionFile, versionName);
            _appVm.SelectedVersion = versionName;
            _appVm.CurrentProfileName = profile.Name;

            UpdateHeroQuickPlay();
            profile.LastPlayed = DateTime.Now;
            await File.WriteAllTextAsync(CurrentProfileIdFile, profile.Id);
            await SaveProfilesAsync();
            await CheckInstallationStatusAsync();

            AppendLog($"PROFILE ĐÃ SẴN SÀNG: {profile.Name} ({versionName}) [Checkmark]", "#10B981");

            this.DispatcherQueue.TryEnqueue(() =>
            {
                InstallButton.Content = "CÀI ĐẶT PROFILE";
                InstallButton.IsEnabled = true;
            });

            tcs.SetResult(true);
        }
        private string GetVersionName(MinecraftProfile p) => p.Loader switch
        {
            "forge" => $"{p.Version}-forge-{p.LoaderVersion}",
            "fabric" => $"fabric-loader-{p.LoaderVersion}-{p.Version}",
            _ => p.Version
        };
        private async Task CheckInstallationStatusAsync()
        {
            if (_currentProfile == null)
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    InstallButton.Content = "CHỌN PROFILE";
                    InstallButton.IsEnabled = false;
                    PlayButton.IsEnabled = false;
                    _appVm.SelectedVersion = "";
                    _appVm.CurrentProfileName = "Chưa chọn profile";
                    UpdateHeroQuickPlay();
                });
                return;
            }

            this.DispatcherQueue.TryEnqueue(() =>
            {
                InstallButton.Content = "CÀI ĐẶT PROFILE";
                InstallButton.IsEnabled = true;
                PlayButton.IsEnabled = false;
                PlayButton.Visibility = Visibility.Visible;
                _appVm.CurrentProfileName = _currentProfile.Name;
                UpdateHeroQuickPlay();
            });

            try
            {
                var versionName = GetVersionName(_currentProfile);
                var versions = await _launcher.GetAllVersionsAsync();

                this.DispatcherQueue.TryEnqueue(() =>
                {
                    if (versions.Any(v => v.Name == versionName))
                    {
                        InstallButton.Content = "ĐÃ CÀI";
                        InstallButton.IsEnabled = false;
                        PlayButton.IsEnabled = true;
                        _appVm.SelectedVersion = versionName;
                        AppendLog($"ĐÃ CÀI: {versionName}", "#10B981");
                    }
                    else
                    {
                        _appVm.SelectedVersion = "";
                        AppendLog($"CHƯA CÀI: {versionName} → Nhấn 'CÀI ĐẶT PROFILE'", "#F59E0B");
                    }
                    UpdateHeroQuickPlay();
                });
            }
            catch (Exception ex)
            {
                AppendLog($"Lỗi kiểm tra phiên bản: {ex.Message}", "#EF4444");
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    InstallButton.Content = "CÀI ĐẶT PROFILE";
                    InstallButton.IsEnabled = true;
                    PlayButton.IsEnabled = false;
                    _appVm.SelectedVersion = "";
                });
            }
        }

        private async Task SaveProfilesAsync()
        {
            var json = JsonSerializer.Serialize(_profiles, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(ProfilesFile, json);
        }
        #endregion
        #region === CÀI ĐẶT PROFILE ===
        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProfile == null) return;
            InstallButton.IsEnabled = false;
            PlayButton.Visibility = Visibility.Collapsed;
            InstallProgress.Visibility = Visibility.Visible;
            InstallProgress.IsIndeterminate = true;
            InstallProgress.Value = 0;
            ClearLog();
            AppendLog($"Bắt đầu cài đặt {_currentProfile.Version} ({_currentProfile.Loader.ToUpper()})...", "#60A5FA");
            try
            {
                var tempPath = new MinecraftPath(_currentProfile.InstallPath);
                var tempLauncher = new MinecraftLauncher(tempPath);
                tempLauncher.ByteProgressChanged += (s, ev) =>
                {
                    if (ev.TotalBytes > 0)
                    {
                        double percent = (double)ev.ProgressedBytes / ev.TotalBytes * 100;
                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            InstallProgress.IsIndeterminate = false;
                            InstallProgress.Value = percent;
                            AppendLog($"Đang tải... {ev.ProgressedBytes / 1024 / 1024:F1}MB / {ev.TotalBytes / 1024 / 1024:F1}MB ({percent:F1}%)", "#60A5FA");
                        });
                    }
                };
                // === LOG THỦ CÔNG CHO TỪNG BƯỚC (thay thế FileChanged/ProgressChanged) ===
                AppendLog("Đang tải danh sách phiên bản...", "#94A3B8");
                if (_currentProfile.Loader == "vanilla")
                {
                    AppendLog("Cài đặt Vanilla...", "#8B5CF6");
                    await tempLauncher.InstallAsync(_currentProfile.Version);
                    AppendLog("Tải client.jar + libraries + assets...", "#94A3B8");
                }
                else if (_currentProfile.Loader == "forge")
                {
                    AppendLog("Cài đặt Forge...", "#F59E0B");
                    var forge = new ForgeInstaller(tempLauncher);
                    AppendLog("Tải Forge installer...", "#94A3B8");
                    var result = await forge.Install(_currentProfile.Version);
                    _currentProfile.LoaderVersion = result;
                    AppendLog($"Forge phiên bản: {result} ✓", "#10B981");
                }

                else if (_currentProfile.Loader == "fabric")
                {
                    AppendLog("Cài đặt Fabric...", "#8B5CF6");
                    var fabric = new FabricInstaller(_javaHttpClient);
                    AppendLog("Lấy danh sách Fabric loader...", "#94A3B8");
                    var loaders = await fabric.GetLoaders(_currentProfile.Version);
                    var latest = loaders.FirstOrDefault(l => l.Stable) ?? loaders.First();
                    _currentProfile.LoaderVersion = latest.Version;
                    AppendLog($"Fabric Loader: {latest.Version} ✓", "#10B981");
                    AppendLog("Tải Fabric API + libraries...", "#94A3B8");
                    await fabric.Install(_currentProfile.Version, latest.Version, tempPath);
                }
                await SaveProfilesAsync();
                await LoadProfileAsync(_currentProfile);
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    InstallProgress.Value = 100;
                    InstallProgress.IsIndeterminate = false;
                });
                await Task.Delay(800);
                AppendLog("✓ CÀI ĐẶT HOÀN TẤT! ĐÃ SẴN SÀNG CHƠI", "#10B981");
                InstallButton.Content = "ĐÃ CÀI";
                InstallButton.IsEnabled = false;
                PlayButton.IsEnabled = true;
                PlayButton.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                AppendLog($"✗ LỖI: {ex.Message}", "#EF4444");
                AppendLog($"Chi tiết: {ex}", "#EF4444");
                InstallButton.IsEnabled = true;
                PlayButton.Visibility = Visibility.Visible;
            }
            finally
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    InstallProgress.Visibility = Visibility.Collapsed;
                    InstallProgress.Value = 0;
                });
            }
            var versionName = GetVersionName(_currentProfile);
            var versions = await _launcher.GetAllVersionsAsync();
            AppendLog($"Tìm thấy: {versions.FirstOrDefault(v => v.Name == versionName)?.Name ?? "KHÔNG TÌM THẤY"}",
                      versions.Any(v => v.Name == versionName) ? "#10B981" : "#EF4444");
        }
        #endregion
        #region === CHƠI PROFILE ===
        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            var vm = (AppViewModel)DataContext;
            if (vm == null) return;
            vm.IsInstalling = true;
            vm.StartupProgress = 0;

            await _loadProfileTask;
            if (_currentProfile == null)
            {
                AppendLog("Lỗi: Không có profile nào được chọn!", "#EF4444");
                return;
            }

            if (!_appVm.IsLoggedIn || _session == null || string.IsNullOrEmpty(_session.AccessToken))
            {
                AppendLog("Lỗi: Chưa đăng nhập!", "#EF4444");
                await ShowErrorAsync("Chưa đăng nhập", "Vui lòng đăng nhập trước.");
                return;
            }

            KillExistingMinecraft();

            var versionName = GetVersionName(_currentProfile);
            var javaPath = GetJavaPath();

            if (!File.Exists(javaPath))
            {
                AppendLog($"Không tìm thấy Java: {javaPath}", "#EF4444");
                await ShowErrorAsync("Thiếu Java", $"Không tìm thấy:\n{javaPath}");
                return;
            }

            InstallProgress.Visibility = Visibility.Visible;
            InstallProgress.IsIndeterminate = true;
            InstallProgress.Value = 0;
            PlayButton.Content = "ĐANG KHỞI ĐỘNG...";
            PlayButton.IsEnabled = false;

            await VoxelLauncher.Services.VoxelXClient.EnsureVoxelXAsync(_appVm.MinecraftFolder);

            var jarPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "VoxelXTweaker.jar");
            var configPath = Path.Combine(_appVm.MinecraftFolder, "VoxelX", "config.json");

            var option = new MLaunchOption
            {
                Session = _session,
                MaximumRamMb = _currentProfile.RamMb,
                JavaPath = javaPath
            };

            var jvmArgs = new List<string>();
            if (!string.IsNullOrWhiteSpace(_currentProfile.JvmArgs))
            {
                jvmArgs.AddRange(_currentProfile.JvmArgs
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim()));
            }

            if (File.Exists(jarPath))
            {
                var agentArg = $"-javaagent:{jarPath}=--voxelxConfig={configPath}";
                jvmArgs.Add(agentArg);
                AppendLog($"VOXELX: Đã thêm javaagent: {agentArg}", "#8B5CF6");

                AppendLog($"JVM ARGS: {string.Join(" ", jvmArgs)}", "#8B5CF6");
            }
            else
            {
                AppendLog("LỖI: Không tìm thấy VoxelXTweaker.jar!", "#EF4444");
                return;
            }
            
            AppendLog($"VOXELX: Config: {configPath}", "#8B5CF6");

            var progressReporter = new Progress<ByteProgress>(OnByteProgress);

            try
            {
                AppendLog($"Đang khởi động {versionName}...", "#10B981");

                var byteProgress = new Progress<ByteProgress>(OnByteProgress);

                var process = await _launcher.InstallAndBuildProcessAsync(
                    versionName,
                    option,
                    fileProgress: null,          
                    byteProgress: byteProgress,  
                    cancellationToken: default
                );

                this.DispatcherQueue.TryEnqueue(() =>
                {
                    InstallProgress.IsIndeterminate = false;
                    InstallProgress.Value = 100;
                    AppendLog("Khởi tạo thành công! Đang chạy Minecraft...", "#10B981");
                });

                process.EnableRaisingEvents = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.UseShellExecute = false;

                process.OutputDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data))
                        AppendLog(ev.Data, GetLogColor(ev.Data));
                };

                process.ErrorDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data))
                    {
                        if (ev.Data.Contains("[VOXEL") || ev.Data.Contains("Loading class"))
                            AppendLog(ev.Data, "#8B5CF6"); 
                        else
                            AppendLog($"[ERROR] {ev.Data}", "#EF4444");
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await Task.Delay(1000);
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    InstallProgress.Visibility = Visibility.Collapsed;
                    PlayButton.Content = "ĐANG CHẠY";

                    var vm = (AppViewModel)DataContext;
                    vm.StartupProgress = 100;

                    Task.Delay(800).ContinueWith(_ =>
                    {
                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            vm.IsInstalling = false;
                        });
                    }, TaskScheduler.Default);
                });

                process.Exited += (s, ev) => this.DispatcherQueue.TryEnqueue(() =>
                {
                    PlayButton.Content = "PLAY";
                    PlayButton.IsEnabled = true;
                    InstallProgress.Visibility = Visibility.Collapsed;
                    AppendLog("Minecraft đã dừng.", "#8B5CF6");
                });
            }
            catch (Exception ex)
            {
                AppendLog($"Lỗi: {ex.Message}", "#EF4444");
                await ShowErrorAsync("Lỗi khởi động", ex.Message);
                vm.IsInstalling = false;

                this.DispatcherQueue.TryEnqueue(() =>
                {
                    InstallProgress.Visibility = Visibility.Collapsed;
                    PlayButton.Content = "PLAY";
                    PlayButton.IsEnabled = true;
                });
            }
        }

        private void OnByteProgress(ByteProgress e)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                var vm = (AppViewModel)DataContext;
                if (vm == null) return;

                if (e.TotalBytes > 0)
                {
                    double percent = (double)e.ProgressedBytes / e.TotalBytes * 100;
                    InstallProgress.IsIndeterminate = false;
                    InstallProgress.Value = percent;

                    vm.StartupProgress = percent;
                    vm.IsInstalling = true;

                    string size = $"{e.ProgressedBytes / 1024f / 1024:F1}MB / {e.TotalBytes / 1024f / 1024:F1}MB";
                    string logText = $"[Downloading] Tải file: {size} - {percent:F1}%";

                    if (_currentDownloadRun == null)
                    {
                        _currentDownloadRun = new Run
                        {
                            Text = logText + "\n",
                            Foreground = new SolidColorBrush(ColorFromHex("#60A5FA"))
                        };
                        ProgressLogText.Inlines.Add(_currentDownloadRun);
                    }
                    else
                    {
                        _currentDownloadRun.Text = logText + "\n";
                    }

                    if (ProgressLogText.Parent is ScrollViewer sv)
                        sv.ChangeView(null, sv.ExtentHeight, null, true);
                }
                else
                {
                    InstallProgress.IsIndeterminate = true;
                    vm.IsInstalling = true;

                    string logText = "[Downloading] Đang tải file...";

                    if (_currentDownloadRun == null)
                    {
                        _currentDownloadRun = new Run
                        {
                            Text = logText + "\n",
                            Foreground = new SolidColorBrush(ColorFromHex("#94A3B8"))
                        };
                        ProgressLogText.Inlines.Add(_currentDownloadRun);
                    }
                    else
                    {
                        _currentDownloadRun.Text = logText + "\n";
                    }
                }
            });
        }
        #endregion
        private string GetLogColor(string line)
        {
            if (line.Contains("ERROR") || line.Contains("Exception") || line.Contains("Failed"))
                return "#EF4444";
            if (line.Contains("INFO") || line.Contains("Loaded") || line.Contains("Starting"))
                return "#10B981";
            if (line.Contains("WARN") || line.Contains("Warning"))
                return "#F59E0B";
            if (line.Contains("DEBUG"))
                return "#8B5CF6";
            return "#E0E7FF";
        }
        #region === JAVA & OPTIMIZATION (ĐÃ SỬA 100%) ===
        private string GetJavaPath()
        {
            if (_currentProfile == null || _launcher == null || _javaResolver == null) return "java";

            var versionName = GetVersionName(_currentProfile);
            var mcVersion = _launcher.GetVersionAsync(versionName).Result;

            var javaVersion = mcVersion.GetInheritedProperty(v => v.JavaVersion);
            if (javaVersion != null)
            {
                var javaPath = _javaResolver.GetJavaBinaryPath(javaVersion, new RulesEvaluatorContext(LauncherOSRule.Current));
                if (File.Exists(javaPath) && IsValidJavaExecutable(javaPath))
                {
                    AppendLog($"Sử dụng Java từ Mojang: {javaPath}", "#8B5CF6");
                    return javaPath;
                }
            }

            var major = GetRequiredJavaMajorVersion(_currentProfile.Version);
            var fallbackVersion = major switch
            {
                21 => new JavaVersion("java-runtime-alpha", "21"),
                17 => new JavaVersion("java-runtime-alpha", "17"),
                _ => MinecraftJavaPathResolver.JreLegacyVersion
            };

            var fallbackPath = _javaResolver.GetJavaBinaryPath(fallbackVersion, new RulesEvaluatorContext(LauncherOSRule.Current));
            if (File.Exists(fallbackPath) && IsValidJavaExecutable(fallbackPath))
            {
                AppendLog($"Sử dụng Java fallback: {fallbackPath}", "#8B5CF6");
                return fallbackPath;
            }

            return GetSystemJavaFallback();
        }

        #region === JAVA RESOLVER & DOWNLOADER (THEO CMLLIB CHUẨN) ===
        public static readonly string JavaManifest = "https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";
        private void InitializeJavaResolver()
        {
            const string newJavaManifest = "https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

            _javaManifestResolver = new MinecraftJavaManifestResolver(_javaHttpClient)
            {
                ManifestServer = newJavaManifest
            };
            _javaResolver = new MinecraftJavaPathResolver(_mcPath);
        }

        private async Task EnsureJavaForVersionAsync(string? mcVersion)
        {
            if (string.IsNullOrEmpty(mcVersion))
            {
                AppendLog("Không có phiên bản Minecraft → Bỏ qua tải Java.", "#94A3B8");
                return;
            }

            if (_javaResolver == null || _javaManifestResolver == null)
            {
                AppendLog("Java resolver chưa khởi tạo.", "#EF4444");
                return;
            }

            var requiredMajor = GetRequiredJavaMajorVersion(mcVersion);
            var javaComponent = GetJavaComponent(requiredMajor);

            var osRule = LauncherOSRule.Current;
            var javaExe = _javaResolver.GetJavaBinaryPath(javaComponent, new RulesEvaluatorContext(osRule));

            if (File.Exists(javaExe) && IsValidJavaExecutable(javaExe))
            {
                AppendLog($"Java {requiredMajor} đã sẵn sàng: {javaExe}", "#10B981");
                return;
            }

            AppendLog($"Đang tải Java {requiredMajor} từ Mojang...", "#F59E0B");
            await DownloadAndInstallJavaAsync(javaComponent, requiredMajor);
        }

        private string ComputeSHA1(string filePath)
        {
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha1.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private async Task DownloadAndInstallJavaAsync(JavaVersion javaVersion, int majorVersion)
        {
            var osRule = LauncherOSRule.Current;
            var osName = MinecraftJavaManifestResolver.GetOSNameForJava(osRule);

            var manifests = await _javaManifestResolver.GetManifestsForOS(osName);
            var targetManifest = manifests
                .FirstOrDefault(m =>
                    m.Component == javaVersion.Component &&
                    m.VersionReleased?.StartsWith(majorVersion.ToString()) == true);

            if (targetManifest?.Metadata?.Url == null)
            {
                AppendLog($"Không tìm thấy Java {majorVersion} cho {osName}", "#EF4444");
                return;
            }

            var javaFiles = await _javaManifestResolver.GetFilesFromManifest(targetManifest, CancellationToken.None);
            var javaDir = _javaResolver.GetJavaDirPath(javaVersion, new RulesEvaluatorContext(osRule));
            Directory.CreateDirectory(javaDir);

            var total = javaFiles.Count();
            var completed = 0;

            foreach (var file in javaFiles)
            {
                var filePath = Path.Combine(javaDir, file.Type);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                if (File.Exists(filePath))
                {
                    var localHash = ComputeSHA1(filePath);
                    if (localHash.Equals(file.Sha1, StringComparison.OrdinalIgnoreCase) && new FileInfo(filePath).Length == file.Size)
                    {
                        completed++;
                        AppendLog($"Bỏ qua: {file.Type} (đã tồn tại)", "#94A3B8");
                        continue;
                    }
                }

                AppendLog($"Tải: {file.Type} ({file.Size / 1024 / 1024}MB)", "#60A5FA");
                await DownloadFileAsync(file.Url, filePath, file.Sha1);
                completed++;
                this.DispatcherQueue.TryEnqueue(() =>
                    AppendLog($"Tải Java {majorVersion}: {completed}/{total}", "#60A5FA"));
            }

            var javaExe = _javaResolver.GetJavaBinaryPath(javaVersion, new RulesEvaluatorContext(osRule));
            if (File.Exists(javaExe) && IsValidJavaExecutable(javaExe))
            {
                AppendLog($"Java {majorVersion} đã cài đặt thành công!", "#10B981");
            }
            else
            {
                AppendLog($"Cài Java {majorVersion} thất bại: không tìm thấy executable", "#EF4444");
            }
        }

        private JavaVersion GetJavaComponent(int major)
        {
            return major switch
            {
                8 => new JavaVersion("java-runtime-beta"),
                17 => new JavaVersion("java-runtime-gamma"),
                >= 21 => new JavaVersion("java-runtime-gamma"),
                _ => new JavaVersion("java-runtime-gamma")
            };
        }

        private async Task DownloadFileAsync(string url, string path, string? expectedSha1)
        {
            using var response = await _javaHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
            }

            if (!string.IsNullOrEmpty(expectedSha1))
            {
                var hash = ComputeSHA1(path);
                if (!hash.Equals(expectedSha1, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"SHA1 không khớp: {path}");
            }
        }

        private int GetRequiredJavaMajorVersion(string mcVersion)
        {
            if (int.TryParse(mcVersion.Split('.')[1], out var minor))
            {
                if (minor >= 21) return 21;
                if (minor >= 17) return 17;
            }
            return 8;
        }

        #endregion
        private string GetSystemJavaFallback()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "java",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(startInfo);
                var output = proc?.StandardOutput.ReadToEnd().Trim();
                if (!string.IsNullOrEmpty(output))
                {
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var valid = lines.FirstOrDefault(l => File.Exists(l.Trim()));
                    if (valid != null) return valid.Trim();
                }
            }
            catch { }
            return "java";
        }
        private bool IsValidJavaExecutable(string javaPath)
        {
            try
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = javaPath,
                        Arguments = "-version",
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                p.Start();
                p.WaitForExit(8000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }
        
        private string GetJavaVersion(string javaPath)
        {
            if (!File.Exists(javaPath)) return "Unknown";
            try
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = javaPath,
                        Arguments = "-version",
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                p.Start();
                var output = p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
                var match = Regex.Match(output, @"version ""([^""]+)""");
                return match.Success ? match.Groups[1].Value : "Unknown";
            }
            catch { return "Unknown"; }
        }
        
        #endregion
        private void KillExistingMinecraft()
        {
            foreach (var proc in Process.GetProcessesByName("javaw"))
            {
                try
                {
                    if (proc.MainModule?.FileName?.Contains(_appVm.MinecraftFolder) == true)
                    {
                        AppendLog("Đang đóng Minecraft cũ...", "#F59E0B");
                        proc.Kill();
                        proc.WaitForExit(5000);
                    }
                }
                catch { }
            }
        }
        #region === ĐĂNG NHẬP ===
        private async Task TryLoadSessionAsync()
        {
            var cacheFile = Path.Combine(_appVm.MinecraftFolder, "ms_accounts.json");
            var storage = new MSessionFileStorage(cacheFile);
            var msSession = await storage.LoadAsync();

            if (msSession != null && !string.IsNullOrEmpty(msSession.AccessToken))
            {
                _session = msSession;
                _appVm.IsLoggedIn = true;
                _appVm.UserName = msSession.Username;
                AppendLog($"Tự động đăng nhập Microsoft: {msSession.Username}", "#10B981");
                return;
            }

            var nameFile = Path.Combine(_appVm.MinecraftFolder, "player_name.txt");
            if (File.Exists(nameFile))
            {
                var name = await File.ReadAllTextAsync(nameFile);
                name = name.Trim();
                if (!string.IsNullOrEmpty(name) && name != "Player")
                {
                    _session = MSession.GetOfflineSession(name);
                    _appVm.UserName = name;
                    _appVm.IsLoggedIn = true;
                    AppendLog($"Tự động đăng nhập Offline: {name}", "#10B981");
                }
            }
        }

        public void TriggerPlayFromMainWindow()
        {
            PlayButton_Click(PlayButton, new RoutedEventArgs());
        }

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            var text = string.Join("", ProgressLogText.Inlines.Select(i => (i as Run)?.Text ?? ""));
            var dp = new DataPackage();
            dp.SetText(text);
            Clipboard.SetContent(dp);
            AppendLog("Đã copy log hiển thị!", "#10B981");
        }
        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            ClearLog();
            AppendLog("Log đã được xóa.", "#F59E0B");
        }
        private void ClearLog()
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                ProgressLogText.Inlines.Clear();
                lock (_logCache) _logCache.Clear();
                if (_isFullLogOpen)
                    FullLogText.Text = "";
            });
        }
        private async void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(_appVm.MinecraftFolder);
                await Launcher.LaunchFolderAsync(folder);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("Lỗi", "Không thể mở thư mục: " + ex.Message);
            }
        }
        
        #endregion
        #region === QUẢN LÝ PROFILE ===
        private async void SelectVersionButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Chọn hoặc Tạo Profile",
                XamlRoot = this.XamlRoot,
                PrimaryButtonText = "Tạo mới",
                CloseButtonText = "Đóng",
                DefaultButton = ContentDialogButton.Close
            };
            var stack = new StackPanel { Spacing = 8 };
            var scroll = new ScrollViewer
            {
                Content = stack,
                MaxHeight = 520,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            if (_profiles.Count == 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "Chưa có profile nào.",
                    Foreground = new SolidColorBrush(Colors.Gray),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                });
            }
            else
            {
                foreach (var p in _profiles.OrderByDescending(x => x.LastPlayed))
                {
                    var profile = p;
                    var border = new Border
                    {
                        Background = new SolidColorBrush(Colors.DimGray),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(12),
                        Margin = new Thickness(0, 6, 0, 6),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(Colors.Transparent)
                    };
                    border.PointerEntered += (s, ev) => border.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 64, 64, 64));
                    border.PointerExited += (s, ev) => border.Background = new SolidColorBrush(Colors.DimGray);
                    var grid = new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                            new ColumnDefinition { Width = GridLength.Auto }
                        }
                    };
                    var selectPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    selectPanel.PointerEntered += SelectPanel_PointerEntered;
                    selectPanel.PointerExited += SelectPanel_PointerExited;
                    selectPanel.Children.Add(new Image
                    {
                        Source = new BitmapImage(new Uri(profile.IconPath)),
                        Width = 40,
                        Height = 40,
                        Stretch = Stretch.UniformToFill
                    });
                    selectPanel.Children.Add(new StackPanel
                    {
                        Children =
                {
                    new TextBlock { Text = profile.Name, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White) },
                    new TextBlock { Text = $"{profile.Version} • {profile.Loader.ToUpper()} • {profile.RamMb}MB", FontSize = 12, Foreground = new SolidColorBrush(Colors.LightGray) }
                }
                    });
                    selectPanel.Tapped += async (s2, e2) =>
                    {
                        e2.Handled = true;
                        _currentProfile = profile;
                        dialog.Hide();
                        await LoadProfileAsync(profile);
                    };
                    Grid.SetColumn(selectPanel, 0);
                    grid.Children.Add(selectPanel);
                    var deleteBtn = new Button
                    {
                        Content = new SymbolIcon { Symbol = Symbol.Delete },
                        Background = new SolidColorBrush(Colors.Transparent),
                        Foreground = new SolidColorBrush(Colors.Red),
                        Width = 36,
                        Height = 36,
                        CornerRadius = new CornerRadius(18),
                        Padding = new Thickness(8),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    ToolTipService.SetToolTip(deleteBtn, "Xóa profile");
                    deleteBtn.PointerEntered += (s, ev) => deleteBtn.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(50, 239, 68, 68));
                    deleteBtn.PointerExited += (s, ev) => deleteBtn.Background = new SolidColorBrush(Colors.Transparent);
                    deleteBtn.Click += async (s2, e2) =>
                    {
                        dialog.Hide();
                        await Confirm_delete(profile);
                        this.DispatcherQueue.TryEnqueue(() => SelectVersionButton_Click(sender, e));
                    };
                    Grid.SetColumn(deleteBtn, 1);
                    grid.Children.Add(deleteBtn);
                    border.Child = grid;
                    stack.Children.Add(border);
                }
            }
            dialog.Content = scroll;
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await CreateNewProfileAsync();
            }
        }

        private void SelectPanel_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse)
            {
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
            }
        }

        private void SelectPanel_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse)
            {
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
            }
        }
        private async Task<bool> ForceDeleteDirectoryAsync(string path)
        {
            if (!Directory.Exists(path)) return true;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Directory.Delete(path, true);
                    return true;
                }
                catch { await Task.Delay(300); }
            }
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C rmdir /S /Q \"{path}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(8000);
                if (!Directory.Exists(path)) return true;
            }
            catch { }
            try
            {
                var emptyTemp = Path.Combine(Path.GetTempPath(), "VoxelEmpty_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(emptyTemp);
                var psi = new ProcessStartInfo
                {
                    FileName = "robocopy",
                    Arguments = $"\"{emptyTemp}\" \"{path}\" /PURGE /MIR /R:0 /W:0 /NFL /NDL /NJH /NJS",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(10000);
                Directory.Delete(emptyTemp, true);
                try { Directory.Delete(path, true); return true; }
                catch { }
            }
            catch { }
            try
            {
                [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
                static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, int dwFlags);
                const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;
                MoveFileEx(path, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                return true;
            }
            catch { }
            return false;
        }
        private async Task Confirm_delete(MinecraftProfile profile)
        {
            var confirmDialog = new ContentDialog
            {
                Title = "XÓA PROFILE VĨNH VIỄN?",
                Content = new TextBlock
                {
                    Text = $"Bạn có chắc chắn muốn xóa profile:\n\"{profile.Name}\"\nTẤT CẢ dữ liệu (mods, world, config) sẽ bị xóa HOÀN TOÀN và KHÔNG THỂ KHÔI PHỤC!",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Colors.OrangeRed)
                },
                PrimaryButtonText = "XÓA NGAY",
                CloseButtonText = "Hủy",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            var result = await confirmDialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
            var profileId = profile.Id;
            var profilePath = profile.InstallPath;
            KillExistingMinecraft();
            _profiles.Remove(profile);
            await SaveProfilesAsync();
            if (_currentProfile?.Id == profileId)
            {
                _currentProfile = null;
                await File.WriteAllTextAsync(CurrentProfileIdFile, "");
                await CheckInstallationStatusAsync();
                UpdateHeroQuickPlay();
            }
            var deleted = await ForceDeleteDirectoryAsync(profilePath);
            AppendLog(deleted
                ? $"ĐÃ XÓA HOÀN TOÀN profile \"{profile.Name}\" ✓"
                : $"Profile \"{profile.Name}\" sẽ bị xóa khi khởi động lại.",
                deleted ? "#10B981" : "#F59E0B");
        }
        private async Task CreateNewProfileAsync()
        {
            var dialog = new ContentDialog
            {
                Title = "TẠO PROFILE MỚI",
                PrimaryButtonText = "TẠO NGAY",
                CloseButtonText = "Hủy",
                XamlRoot = this.XamlRoot,
                Width = 900,
                MaxHeight = 750
            };
            var mainGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
                },
                Margin = new Thickness(20)
            };
            mainGrid.Children.Add(new TextBlock { Text = "Tên profile:", FontWeight = FontWeights.SemiBold });
            var nameBox = new TextBox { PlaceholderText = "Để trống = tự động" };
            Grid.SetRow(nameBox, 0); Grid.SetColumn(nameBox, 1);
            mainGrid.Children.Add(nameBox);
            var loaderLabel = new TextBlock { Text = "Mod Loader:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 0) };
            Grid.SetRow(loaderLabel, 1);
            mainGrid.Children.Add(loaderLabel);
            var loaderBox = new ComboBox
            {
                ItemsSource = new[] { "Vanilla", "Fabric", "Quilt", "Forge", "NeoForge" },
                SelectedIndex = 0,
                Margin = new Thickness(0, 20, 0, 0)
            };
            Grid.SetRow(loaderBox, 1); Grid.SetColumn(loaderBox, 1);
            mainGrid.Children.Add(loaderBox);
            var versionLabel = new TextBlock
            {
                Text = "Phiên bản Minecraft:",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 20, 0, 0)
            };
            var versionBox = new ComboBox
            {
                PlaceholderText = "Chọn phiên bản...",
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var filterComboBox = new ComboBox
            {
                PlaceholderText = "Tất cả phiên bản",
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Visibility = Visibility.Collapsed
            };
            var filterItems = new[]
            {
                ("Tất cả phiên bản", "all"),
                ("Release (Ổn định)", "release"),
                ("Snapshot (Thử nghiệm)", "snapshot"),
                ("Pre-release", "-pre"),
                ("Release Candidate", "-rc"),
                ("Old Beta/Alpha", "old_")
            };
            foreach (var item in filterItems)
            {
                filterComboBox.Items.Add(new ComboBoxItem
                {
                    Content = item.Item1,
                    Tag = item.Item2
                });
            }
            filterComboBox.SelectedIndex = 1;
            var leftStack = new StackPanel { Spacing = 8 };
            leftStack.Children.Add(versionLabel);
            leftStack.Children.Add(filterComboBox);
            leftStack.Children.Add(versionBox);
            Grid.SetRow(leftStack, 2);
            Grid.SetColumn(leftStack, 0);
            mainGrid.Children.Add(leftStack);
            var loaderVersionLabel = new TextBlock
            {
                Text = "Loader Version:",
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.5,
                Margin = new Thickness(0, 20, 0, 0)
            };
            var loaderVersionBox = new ComboBox { IsEnabled = false, Opacity = 0.5 };
            Grid.SetRow(loaderVersionLabel, 3);
            Grid.SetRow(loaderVersionBox, 3); Grid.SetColumn(loaderVersionBox, 1);
            mainGrid.Children.Add(loaderVersionLabel);
            mainGrid.Children.Add(loaderVersionBox);
            var ramBox = new NumberBox
            {
                Value = 4096,
                Minimum = 1024,
                Maximum = 32768,
                SmallChange = 512,
                LargeChange = 2048,
                Margin = new Thickness(0, 20, 0, 0)
            };
            mainGrid.Children.Add(new TextBlock
            {
                Text = "RAM (MB):",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 20, 0, 0)
            });
            Grid.SetRow((FrameworkElement)mainGrid.Children[^1], 4);
            Grid.SetColumn((FrameworkElement)mainGrid.Children[^1], 0);
            Grid.SetRow(ramBox, 4);
            Grid.SetColumn(ramBox, 1);
            mainGrid.Children.Add(ramBox);
            dialog.Content = new ScrollViewer { Content = mainGrid };
            var versions = await _launcher.GetAllVersionsAsync();
            var allVersionData = versions.Select(v => new
            {
                Name = v.Name,
                Type = v.Type.ToLower(),
                ReleaseTime = v.ReleaseTime
            }).OrderByDescending(v => v.Name, _versionComparer).ToList();
            filterComboBox.SelectionChanged += (s, e) => ApplyVanillaFilter();
            var initialLoader = (string)loaderBox.SelectedItem;
            if (initialLoader.Contains("Vanilla"))
            {
                ApplyVanillaFilter();
            }
            else
            {
                versionBox.ItemsSource = allVersionData
                    .Where(v => v.Type == "release")
                    .Select(v => v.Name)
                    .OrderByDescending(v => v, _versionComparer)
                    .ToList();
                if (versionBox.Items.Count > 0) versionBox.SelectedIndex = 0;
            }
            void ApplyVanillaFilter()
            {
                var selectedFilter = ((ComboBoxItem)filterComboBox.SelectedItem)?.Tag?.ToString() ?? "release";
                var filtered = allVersionData.AsEnumerable();
                if (selectedFilter == "all")
                {
                }
                else if (selectedFilter == "release")
                    filtered = filtered.Where(v => v.Type == "release");
                else if (selectedFilter == "snapshot")
                    filtered = filtered.Where(v => v.Type == "snapshot");
                else if (selectedFilter == "-pre")
                    filtered = filtered.Where(v => v.Name.Contains("-pre"));
                else if (selectedFilter == "-rc")
                    filtered = filtered.Where(v => v.Name.Contains("-rc"));
                else if (selectedFilter == "old_")
                    filtered = filtered.Where(v => v.Type.Contains("old"));
                var list = filtered.Select(v => v.Name).ToList();
                versionBox.ItemsSource = list;
                if (list.Any()) versionBox.SelectedIndex = 0;
            }
            UpdateUIVisibility();
            if (((string)loaderBox.SelectedItem).Contains("Vanilla"))
                ApplyVanillaFilter();
            void UpdateUIVisibility()
            {
                var isVanilla = ((string)loaderBox.SelectedItem)?.Contains("Vanilla") == true;
                filterComboBox.Visibility = isVanilla ? Visibility.Visible : Visibility.Collapsed;
                versionLabel.Text = isVanilla ? "Phiên bản Minecraft:" : "Phiên bản Minecraft";
                loaderVersionLabel.Opacity = isVanilla ? 0.5 : 1;
                loaderVersionBox.Opacity = isVanilla ? 0.5 : 1;
                loaderVersionBox.IsEnabled = !isVanilla;
            }
            loaderBox.SelectionChanged += (s, e) =>
            {
                var selectedLoaderRaw = (string)loaderBox.SelectedItem;
                var isVanilla = selectedLoaderRaw?.Contains("Vanilla") == true;
                UpdateUIVisibility();
                IEnumerable<string> filteredVersions;
                if (isVanilla)
                {
                    ApplyVanillaFilter();
                    return;
                }
                else
                {
                    filteredVersions = allVersionData
                        .Where(v => v.Type == "release")
                        .Select(v => v.Name)
                        .OrderByDescending(v => v, _versionComparer);
                    versionBox.ItemsSource = filteredVersions.ToList();
                    if (versionBox.Items.Count > 0)
                        versionBox.SelectedIndex = 0;
                }
                var mcVer = (string)versionBox.SelectedItem;
                if (!string.IsNullOrEmpty(mcVer) && !isVanilla)
                {
                    var loaderLower = selectedLoaderRaw?.ToLower();
                    _ = LoadLoaderVersions(mcVer, loaderLower);
                }
            };
            UpdateUIVisibility();
            ApplyVanillaFilter();
            async Task LoadLoaderVersions(string mcVersion, string loader)
            {
                loaderVersionBox.ItemsSource = null;
                loaderVersionBox.IsEnabled = false;
                if (string.IsNullOrEmpty(mcVersion) || loader.Contains("vanilla")) return;
                try
                {
                    if (loader == "forge")
                    {
                        var forge = new ForgeVersionLoader(_javaHttpClient);
                        var versions = await forge.GetForgeVersions(mcVersion);

                        if (!versions.Any())
                        {
                            loaderVersionBox.Items.Add("Không hỗ trợ");
                        }
                        else
                        {
                            var list = versions
                                .OrderByDescending(v => v.ForgeVersionName, _versionComparer)
                                .ToList();

                            _forgeVersions = list.Select(v => v.ForgeVersionName).ToList();

                            var display = list.Select(v =>
                            {
                                string label = v.ForgeVersionName;
                                if (v.IsRecommendedVersion) label += " (Recommended)";
                                else if (v.IsLatestVersion) label += " (Latest)";
                                return label;
                            }).ToList();

                            loaderVersionBox.ItemsSource = display;

                            var recommended = list.FirstOrDefault(v => v.IsRecommendedVersion);
                            var latest = list.FirstOrDefault(v => v.IsLatestVersion);
                            var selected = recommended ?? latest ?? list.First();
                            loaderVersionBox.SelectedIndex = list.IndexOf(selected);
                        }
                    }
                    else if (loader == "neoforge")
                    {
                        var neo = new NeoForgeVersionLoader(_javaHttpClient);
                        var versions = await neo.GetNeoForgeVersions(mcVersion);

                        if (!versions.Any())
                        {
                            loaderVersionBox.Items.Add("Không hỗ trợ");
                        }
                        else
                        {
                            var list = versions.OrderByDescending(v => v.VersionName).ToList();
                            _forgeVersions = list.Select(v => v.VersionName).ToList();

                            var display = list.Select(v =>
                                $"{v.VersionName} {(v.VersionName == list[0].VersionName ? " (Recommended)" : "")}"
                            ).ToList();

                            loaderVersionBox.ItemsSource = display;
                            loaderVersionBox.SelectedIndex = 0;
                        }
                    }
                    else if (loader == "fabric" || loader == "quilt")
                    {
                        var fabric = new FabricInstaller(_javaHttpClient);
                        var loaders = await fabric.GetLoaders(mcVersion);
                        _fabricLoaders = loaders.ToList();
                        var display = _fabricLoaders.Select(l => $"{l.Version} {(l.Stable ? "✓ Stable" : "Beta")}").ToList();
                        loaderVersionBox.ItemsSource = display;
                        var stableIdx = _fabricLoaders.FindIndex(l => l.Stable);
                        loaderVersionBox.SelectedIndex = stableIdx >= 0 ? stableIdx : 0;
                    }
                    loaderVersionBox.IsEnabled = true;
                }
                catch (Exception ex)
                {
                    loaderVersionBox.Items.Add($"Lỗi: {ex.Message}");
                }
            }
            versionBox.SelectionChanged += async (s, e) =>
            {
                var mcVer = (string)versionBox.SelectedItem;
                if (string.IsNullOrEmpty(mcVer)) return;
                var selectedLoaderRaw = (string)loaderBox.SelectedItem;
                var isVanilla = selectedLoaderRaw?.Contains("Vanilla") == true;
                if (!isVanilla)
                {
                    var loaderLower = selectedLoaderRaw?.ToLower();
                    await LoadLoaderVersions(mcVer, loaderLower);
                }
            };
            loaderBox.SelectionChanged += async (s, e) =>
            {
                var loader = ((string)loaderBox.SelectedItem)?.ToLower();
                var mcVer = (string)versionBox.SelectedItem;
                UpdateUIVisibility();
                if (!string.IsNullOrEmpty(mcVer) && loader != "vanilla")
                    await LoadLoaderVersions(mcVer, loader);
            };
            // === TẠO PROFILE ===
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
            var selectedVersion = (string)versionBox.SelectedItem ?? "1.21.4";
            var selectedLoaderRaw = (string)loaderBox.SelectedItem ?? "Vanilla";
            var selectedLoader = selectedLoaderRaw.ToLower();
            var profileName = string.IsNullOrWhiteSpace(nameBox.Text)
                ? $"{selectedVersion} {selectedLoaderRaw}"
                : nameBox.Text.Trim();
            var iconPath = selectedLoader switch
            {
                "forge" or "neoforge" => "ms-appx:///Assets/Icons/forge.png",
                "fabric" or "quilt" => "ms-appx:///Assets/Icons/fabric.png",
                _ => "ms-appx:///Assets/Icons/vanilla.png"
            };
            string loaderVersion = "";
            if (!selectedLoader.Contains("vanilla"))
            {
                if (selectedLoader == "fabric" || selectedLoader == "quilt")
                    loaderVersion = loaderVersionBox.SelectedIndex >= 0 ? _fabricLoaders[loaderVersionBox.SelectedIndex].Version : "";
                else
                    loaderVersion = (string)loaderVersionBox.SelectedItem ?? "";
            }
            var profile = new MinecraftProfile
            {
                Id = Guid.NewGuid().ToString(),
                Name = profileName,
                Version = selectedVersion,
                Loader = selectedLoader,
                LoaderVersion = loaderVersion,
                RamMb = (int)ramBox.Value,
                LastPlayed = DateTime.Now,
                IconPath = iconPath,
                JvmArgs = ""
            };
            Directory.CreateDirectory(profile.InstallPath);
            _profiles.Add(profile);
            await SaveProfilesAsync();
            _currentProfile = profile;
            await LoadProfileAsync(profile);
            await CheckInstallationStatusAsync();
            AppendLog($"TẠO PROFILE THÀNH CÔNG: {profile.Name} ({selectedVersion}{(string.IsNullOrEmpty(loaderVersion) ? "" : $" - {loaderVersion}")}) ✓", "#10B981");
        }
        #endregion
        public void AppendLogFromMain(string logLine, string colorHex)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                var run = new Run
                {
                    Text = logLine,
                    Foreground = new SolidColorBrush(ColorFromHex(colorHex))
                };
                ProgressLogText.Inlines.Add(run);
                if (ProgressLogText.Parent is ScrollViewer sv)
                    sv.ChangeView(null, sv.ExtentHeight, null, true);
            });
        }
        private class VersionComparer : IComparer<string>
        {
            private static readonly Regex _splitRegex = new(@"(?<=\D)(?=\d)|(?<=\d)(?=\D)");
            public int Compare(string? x, string? y)
            {
                if (x == null || y == null) return 0;
                var partsX = _splitRegex.Split(x);
                var partsY = _splitRegex.Split(y);
                for (int i = 0; i < Math.Min(partsX.Length, partsY.Length); i++)
                {
                    var px = partsX[i];
                    var py = partsY[i];
                    if (int.TryParse(px, out int nx) && int.TryParse(py, out int ny))
                    {
                        if (nx != ny) return nx.CompareTo(ny);
                    }
                    else
                    {
                        int cmp = string.Compare(px, py, StringComparison.OrdinalIgnoreCase);
                        if (cmp != 0) return cmp;
                    }
                }
                return partsX.Length.CompareTo(partsY.Length);
            }
        }
        private readonly IComparer<string> _versionComparer = new VersionComparer();
        #region === HERO & LOG ===
        private void UpdateHeroQuickPlay(MinecraftProfile? profile = null)
        {
            profile ??= _currentProfile;

            if (profile == null)
            {
                HeroTitle.Text = "Chưa chọn profile";
                QuickPlayButton.IsEnabled = false;
                return;
            }

            var displayName = profile.Loader switch
            {
                "vanilla" => profile.Version,
                "forge" or "neoforge" => $"{profile.Version} Forge",
                "fabric" or "quilt" => $"{profile.Version} Fabric",
                _ => profile.Version
            };

            HeroTitle.Text = $"Sẵn sàng: {displayName}";
            QuickPlayButton.IsEnabled = PlayButton.IsEnabled;
        }
        private void QuickPlayButton_Click(object sender, RoutedEventArgs e) => PlayButton_Click(sender, e);
        private void AppendLog(string text, string color = "#FFFFFF")
        {
            var logLine = $"[{DateTime.Now:HH:mm:ss}] {text}\n";
            lock (_logCache)
            {
                _logCache.Add(logLine);
            }
            this.DispatcherQueue.TryEnqueue(() =>
            {
                var run = new Run
                {
                    Text = logLine,
                    Foreground = new SolidColorBrush(ColorFromHex(color))
                };
                ProgressLogText.Inlines.Add(run);
                if (ProgressLogText.Parent is ScrollViewer svProgress)
                    svProgress.ChangeView(null, svProgress.ExtentHeight, null, true);
                if (_isFullLogOpen && FullLogText != null)
                {
                    FullLogText.Text += logLine;
                    if (FullLogText.Parent is ScrollViewer svFull)
                        svFull.ChangeView(null, svFull.ExtentHeight, null, true);
                }
            });
        }
        private Windows.UI.Color ColorFromHex(string hex)
        {
            hex = hex.TrimStart('#');
            return Windows.UI.Color.FromArgb(255,
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        }
        private void OpenFullLog_Click(object sender, RoutedEventArgs e)
        {
            _isFullLogOpen = true;
            FullLogGrid.Visibility = Visibility.Visible;
            ClientContentBorder.Visibility = Visibility.Collapsed;
            lock (_logCache)
            {
                FullLogText.Text = string.Join("", _logCache);
            }
            if (FullLogText.Parent is ScrollViewer sv)
                sv.ChangeView(null, sv.ExtentHeight, null, true);
        }
        private void BackToClient_Click(object sender, RoutedEventArgs e)
        {
            FullLogGrid.Visibility = Visibility.Collapsed;
            ClientContentBorder.Visibility = Visibility.Visible;
        }
        private void CopyFullLog_Click(object sender, RoutedEventArgs e)
        {
            string fullLog;
            lock (_logCache)
            {
                fullLog = string.Join("", _logCache);
            }
            var dp = new DataPackage();
            dp.SetText(fullLog);
            Clipboard.SetContent(dp);
            AppendLog("Đã copy FULL LOG!", "#10B981");
        }
        #endregion
        #region === TỐI ƯU HÓA ===
        private async Task LoadSavedSettingsAsync()
        {
            var file = Path.Combine(_appVm.MinecraftFolder, "voxel_settings.json");
            if (!File.Exists(file)) return;
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (settings.TryGetValue("Ram", out var ramElement) && ramElement.TryGetInt32(out var ram))
                    RamSlider.Value = ram;
                
                AppendLog("Đã tải cấu hình GOD MODE từ lần trước!", "#00FF00");
            }
            catch (Exception ex)
            {
                AppendLog($"Lỗi load settings: {ex.Message}", "#EF4444");
            }
        }
        #endregion
        #region === HÀM HỖ TRỢ ===
        private void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
                File.Move(file, Path.Combine(dest, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        private async Task ShowErrorAsync(string title, string message) => await new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot
        }.ShowAsync();

        private void RefreshGamePath()
        {
            var mcDir = _appVm.MinecraftFolder;
            Directory.CreateDirectory(mcDir);

            _mcPath = new MinecraftPath(mcDir);          
            _launcher = new MinecraftLauncher(_mcPath); 

            AppendLog($"Đường dẫn game: {_appVm.MinecraftFolder}", "#8B5CF6");
        }

        private class GameSettings
        {
            public bool BootFps { get; set; }
            public int RamMb { get; set; }
            public string FpsCap { get; set; } = "Không giới hạn";
            public string Graphics { get; set; } = "fabulous";
            public int RenderDistance { get; set; } = 12;
        }

        #endregion
    }
    public class MSessionFileStorage
    {
        private readonly string _filePath;
        private static readonly JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
        public MSessionFileStorage(string filePath) => _filePath = filePath;
        public async Task<MSession?> LoadAsync() => File.Exists(_filePath) ? JsonSerializer.Deserialize<MSession>(await File.ReadAllTextAsync(_filePath), _options) : null;
        public async Task SaveAsync(MSession session) => await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(session, _options));
    }
    public class MinecraftProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Profile";
        public string Version { get; set; } = "1.21";
        public string Loader { get; set; } = "vanilla";
        public string LoaderVersion { get; set; } = "";
        public int RamMb { get; set; } = 4096;
        public string JvmArgs { get; set; } = "";
        public DateTime LastPlayed { get; set; } = DateTime.Now;
        public string IconPath { get; set; } = "ms-appx:///Assets/Icons/vanilla.png";
        public bool AllowMultiInstance { get; set; } = false;
        public string InstallPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoxelLauncher", "profiles", Id
        );
    }

}

