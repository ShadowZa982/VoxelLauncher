using CmlLib.Core.Auth;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoxelLauncher.ViewModels
{
    public partial class AppViewModel : ObservableObject
    {
        // === THƯ MỤC CHÍNH (STATIC) ===
        private static readonly string VoxelClientFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxelLauncher");

        private static readonly string DefaultMinecraftFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

        private static readonly string ConfigFile = Path.Combine(VoxelClientFolder, "config.json");
        // === PROPERTY CHÍNH ===
        [ObservableProperty]
        private string _minecraftFolder;

        // === KHỞI TẠO ===
        public AppViewModel()
        {
            Directory.CreateDirectory(VoxelClientFolder);

            LoadConfig();

            var cachedDir = Path.Combine(MinecraftFolder, "cachedImages");
            Directory.CreateDirectory(cachedDir);
            RefreshAccountType(); 

        }

        private void LoadConfig()
        {
            if (!File.Exists(ConfigFile))
            {
                MinecraftFolder = DefaultMinecraftFolder; 
                SaveConfig();
                return;
            }

            try
            {
                var json = File.ReadAllText(ConfigFile);
                var config = JsonSerializer.Deserialize<LauncherConfig>(json);
                MinecraftFolder = config?.MinecraftFolder ?? DefaultMinecraftFolder;
                if (!Directory.Exists(MinecraftFolder))
                {
                    MinecraftFolder = DefaultMinecraftFolder;
                }
            }
            catch
            {
                MinecraftFolder = DefaultMinecraftFolder;
            }
        }

        private void SaveConfig()
        {
            var config = new LauncherConfig { MinecraftFolder = MinecraftFolder };
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFile, json);
        }

        // === CÁC PROPERTY KHÁC ===
        [ObservableProperty] private int _pendingUpdateCount;
        [ObservableProperty] private List<PendingUpdateInfo> _pendingUpdates = new();
        [ObservableProperty] private string _userName = "";
        [ObservableProperty] private bool _isLoggedIn = false;
        [ObservableProperty] private string _selectedVersion = "";
        [ObservableProperty] private string _currentProfileName = "";
        [ObservableProperty] private double _startupProgress = 0;
        [ObservableProperty] private bool _isInstalling = false;
        [ObservableProperty] private string? _xuid;
        [ObservableProperty] private string? _clientId;
        [ObservableProperty] private DateTime _loginTime = DateTime.MinValue;
        [ObservableProperty] private TimeSpan _totalPlayTime = TimeSpan.Zero;
        [ObservableProperty] private double _updateProgress;
        [ObservableProperty] private string _updateStatus = "";
        [ObservableProperty] private bool highPerformance = false;

        public List<string> FpsCapOptions => new() { "240", "60 FPS", "120 FPS", "144 FPS", "240 FPS", "360 FPS" };
        public List<string> GraphicsOptions => new() { "0", "1", "2" };

        [ObservableProperty]
        private string selectedFpsCapItem = "240";

        [ObservableProperty]
        private string selectedGraphicsItem = "1";

        private static readonly string VoxelSettingsFile = "voxel_settings.json";

        partial void OnSelectedFpsCapItemChanged(string value) => FpsCap = value;
        partial void OnSelectedGraphicsItemChanged(string value) => GraphicsMode = value;
        partial void OnFpsCapChanged(string value) => SelectedFpsCapItem = value;
        partial void OnGraphicsModeChanged(string value) => SelectedGraphicsItem = value;

        [ObservableProperty] private bool bootFpsEnabled;
        [ObservableProperty] private int renderDistance = 12;
        [ObservableProperty] private string fpsCap = "240";
        [ObservableProperty] private string graphicsMode = "1";
        [ObservableProperty] private Profile _currentProfile = new Profile { RamMb = 4096 };

        // === SESSION & AVATAR ===
        public MSession? Session { get; set; }


        public BitmapImage? GetLargeAvatar()
        {
            if (string.IsNullOrEmpty(UserName))
                return new BitmapImage(new Uri("ms-appx:///Assets/Icons/player.png"));

            var url = UserName.Contains("@") || UserName.Length > 3
                ? $"https://mc-heads.net/avatar/{UserName}/80"
                : "ms-appx:///Assets/Icons/player.png";

            return new BitmapImage(new Uri(url));
        }

        // === KIỂM TRA LOẠI TÀI KHOẢN ===
        [ObservableProperty]
        private bool _isMicrosoftAccount;

        [ObservableProperty]
        private bool _isOfflineAccount;

        // === CẬP NHẬT KHI MinecraftFolder THAY ĐỔI ===
        partial void OnMinecraftFolderChanged(string value)
        {
            SaveConfig(); 
            RefreshAccountType();
            var cachedDir = Path.Combine(value, "cachedImages");
            Directory.CreateDirectory(cachedDir);
        }

        public class LauncherConfig
        {
            public string? MinecraftFolder { get; set; }

        }

        // === CẬP NHẬT LOẠI TÀI KHOẢN ===
        public void RefreshAccountType()
        {
            IsMicrosoftAccount = File.Exists(Path.Combine(MinecraftFolder, "ms_accounts.json"));
            IsOfflineAccount = File.Exists(Path.Combine(MinecraftFolder, "player_name.txt"));
        }

        // === LẤY TÊN NGƯỜI DÙNG ===
        public string GetUserNameFromFile()
        {
            if (IsMicrosoftAccount)
            {
                var file = Path.Combine(MinecraftFolder, "ms_accounts.json");
                try
                {
                    var json = File.ReadAllText(file);
                    var session = JsonSerializer.Deserialize<MSession>(json);
                    return session?.Username ?? "";
                }
                catch { return ""; }
            }

            if (IsOfflineAccount)
            {
                var file = Path.Combine(MinecraftFolder, "player_name.txt");
                try { return File.ReadAllText(file).Trim(); }
                catch { return ""; }
            }

            return "";
        }

        // === RESET ===
        public void ResetAll()
        {
            UserName = "";
            IsLoggedIn = false;
            Session = null;
            Xuid = null;
            ClientId = null;
            LoginTime = DateTime.MinValue;
            TotalPlayTime = TimeSpan.Zero;
            IsMicrosoftAccount = false;
            IsOfflineAccount = false;
        }

        public async Task LoadVoxelSettingsAsync()
        {
            var filePath = Path.Combine(MinecraftFolder, VoxelSettingsFile);
            if (!File.Exists(filePath)) return;

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var settings = JsonSerializer.Deserialize<VoxelSettings>(json);

                if (settings == null) return;
                CurrentProfile.RamMb = settings.RamMb;
                FpsCap = settings.FpsCap;
                GraphicsMode = settings.Graphics;
                RenderDistance = settings.RenderDistance;
                BootFpsEnabled = settings.BootFps;
                HighPerformance = settings.HighPerformance;
                OnPropertyChanged(nameof(CurrentProfile));
                OnPropertyChanged(nameof(FpsCap));
                OnPropertyChanged(nameof(GraphicsMode));
                OnPropertyChanged(nameof(RenderDistance));
                OnPropertyChanged(nameof(BootFpsEnabled));
                OnPropertyChanged(nameof(HighPerformance));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VOXEL SETTINGS] Load error: {ex.Message}");
            }
        }

        public async Task SaveVoxelSettingsAsync()
        {
            var settings = new VoxelSettings
            {
                RamMb = CurrentProfile.RamMb,
                FpsCap = FpsCap,
                Graphics = GraphicsMode,       
                RenderDistance = RenderDistance,
                BootFps = BootFpsEnabled,
                HighPerformance = HighPerformance
            };

            var filePath = Path.Combine(MinecraftFolder, VoxelSettingsFile);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
        }
    }

    

    public partial class Profile : ObservableObject
    {

        [ObservableProperty] private int ramMb = 4096;
        [ObservableProperty] private string? jvmArgs;
    }

    public class PendingUpdateInfo
    {
        public string Version { get; set; } = "";
        public string NotesHtml { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public DateTime SkippedAt { get; set; }
    }

    public class VoxelSettings
    {
        public int RamMb { get; set; } = 4096;
        public string FpsCap { get; set; } = "Không giới hạn";
        public string Graphics { get; set; } = "1";
        public int RenderDistance { get; set; } = 12;
        public bool BootFps { get; set; } = false;
        public bool HighPerformance { get; set; } = false;
    }
}