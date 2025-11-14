using CmlLib.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using VoxelLauncher.Pages;
using VoxelLauncher.Services;

namespace VoxelLauncher.ViewModels
{
    public partial class ModrinthMod : ObservableObject
    {
        [ObservableProperty] private bool _isEnabled;
        [ObservableProperty] private string _voxelFilePath = "";
        [ObservableProperty] private string _originalFilename = "";
        private readonly IModToggleService? _toggleService;
        public bool IsInstalled { get; set; } = false;
        public string InstalledVersion { get; set; } = "";
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string IconUrl { get; set; } = "";
        public string Description { get; set; } = "";
        public int Downloads { get; set; }
        public string LatestVersion { get; set; } = "";
        public string[] Loaders { get; set; } = Array.Empty<string>();
        public string Author { get; set; } = "Unknown";

        private List<ModrinthVersion>? _versions;
        public List<ModrinthVersion> Versions => _versions ??= new();
        public bool IsVersionsLoaded { get; private set; } = false;
        public async Task LoadVersionsAsync(HttpClient http, string selectedLoader, string selectedMinecraftVersion)
        {
            if (IsVersionsLoaded) return;
            try
            {
                var url = $"https://api.modrinth.com/v2/project/{Id}/version";
                var query = new List<string>();
                if (!string.IsNullOrEmpty(selectedLoader))
                    query.Add($"loaders=[\"{selectedLoader}\"]");
                if (!string.IsNullOrEmpty(selectedMinecraftVersion))
                    query.Add($"game_versions=[\"{selectedMinecraftVersion}\"]");
                if (query.Any())
                    url += $"?{string.Join("&", query)}";
                var json = await http.GetStringAsync(url);
                var arr = JsonSerializer.Deserialize<JsonElement>(json);
                var list = new List<ModrinthVersion>();
                foreach (var v in arr.EnumerateArray())
                {
                    var version = new ModrinthVersion
                    {
                        Id = v.GetProperty("id").GetString() ?? "",
                        Name = v.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                        VersionNumber = v.TryGetProperty("version_number", out var verNum) ? verNum.GetString() ?? "" : "",
                        GameVersions = v.TryGetProperty("game_versions", out var gv)
                            ? gv.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                            : Array.Empty<string>(),
                        Loaders = v.TryGetProperty("loaders", out var ld)
                            ? ld.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                            : Array.Empty<string>(),
                        DatePublished = v.TryGetProperty("date_published", out var dp)
                            ? DateTime.TryParse(dp.GetString(), out var date) ? date : DateTime.MinValue
                            : DateTime.MinValue,
                        Files = v.TryGetProperty("files", out var files)
                            ? files.EnumerateArray().Select(f => new ModrinthFile
                            {
                                Url = f.GetProperty("url").GetString() ?? "",
                                Filename = f.GetProperty("filename").GetString() ?? "",
                                Primary = f.TryGetProperty("primary", out var pri) && pri.GetBoolean()
                            }).ToList()
                            : new List<ModrinthFile>(),
                        Downloads = v.TryGetProperty("downloads", out var dw) ? dw.GetInt32() : 0
                    };
                    list.Add(version);
                }
                _versions = list
                    .Where(v =>
                        (string.IsNullOrEmpty(selectedLoader) || v.Loaders.Any(l => l.Equals(selectedLoader, StringComparison.OrdinalIgnoreCase))) &&
                        (string.IsNullOrEmpty(selectedMinecraftVersion) || v.GameVersions.Contains(selectedMinecraftVersion))
                    )
                    .OrderByDescending(v => v.DatePublished)
                    .ToList();
                IsVersionsLoaded = true;
            }
            catch { }
        }

        public string VersionsText => string.Join(" ", Versions.Take(6).Select(v => v.VersionNumber));

        private bool _isToggling = false;
        private bool _hasBeenInitialized = false;
        public void MarkAsInitialized() => _hasBeenInitialized = true;
        partial void OnIsEnabledChanged(bool oldValue, bool newValue)
        {
            if (_isToggling) return;
            if (oldValue == false && !_hasBeenInitialized) return;
            WeakReferenceMessenger.Default.Send(new ModToggleMessage(this));
        }
        internal void SetToggling(bool value) => _isToggling = value;
        public class ModToggleMessage
        {
            public ModrinthMod Mod { get; }
            public ModToggleMessage(ModrinthMod mod) => Mod = mod;
        }
    }

    public class ModrinthVersion
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string VersionNumber { get; set; } = "";
        public string[] GameVersions { get; set; } = Array.Empty<string>();
        public string[] Loaders { get; set; } = Array.Empty<string>();
        public DateTime DatePublished { get; set; }
        public List<ModrinthFile> Files { get; set; } = new();
        public int Downloads { get; set; }

        public string GameVersionsString => string.Join(", ", GameVersions.Take(3));
    }

    public class ModrinthFile
    {
        public string Url { get; set; } = "";
        public string Filename { get; set; } = "";
        public bool Primary { get; set; }
    }

    public partial class ModsViewModel : ObservableObject
    {
        public HttpClient Http { get; } = new();
        private const string ApiBase = "https://api.modrinth.com/v2";
        private const int PageSize = 30;

        [ObservableProperty] private ObservableCollection<ModrinthMod> _mods = new();
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _selectedLoader = "";
        [ObservableProperty] private string _selectedCategory = "";
        [ObservableProperty] private string _selectedMinecraftVersion = "";
        [ObservableProperty] private ObservableCollection<string> _minecraftVersions = new();
        [ObservableProperty] private bool _isLoadingVersions;
        [ObservableProperty] private ObservableCollection<ModrinthMod> _installedMods = new();
        private readonly string _minecraftDir;
        private readonly string _voxelModsDir;
        private readonly string _activeModsDir;
        [ObservableProperty] private bool _isEnabled;
        public string VoxelFilePath { get; set; } = "";
        public string OriginalFilename { get; set; } = "";
        private MinecraftVersionManager? _versionManager;
        private string CacheFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoxelLauncher", "mods_versions_cache.json");

        public async Task LoadMinecraftVersionsAsync()
        {
            IsLoadingVersions = true;
            MinecraftVersions.Clear();

            try
            {
                _versionManager ??= new MinecraftVersionManager(
                    new MinecraftLauncher(new MinecraftPath()),
                    CacheFile);

                var versions = await _versionManager.GetVersionsAsync();

                if (!versions.Any())
                {
                    MinecraftVersions.Add("Không có phiên bản");
                    return;
                }

                var latest = versions.First().Name;

                var displayList = versions.Select(v =>
                    $"{v.Name} {(v.Name == latest ? "[Latest]" : "")}".Trim()
                ).ToList();

                foreach (var item in displayList)
                    MinecraftVersions.Add(item);
                if (string.IsNullOrEmpty(SelectedMinecraftVersion))
                {
                    SelectedMinecraftVersion = latest;
                }
            }
            catch (Exception ex)
            {
                MinecraftVersions.Add("Lỗi tải phiên bản");
                System.Diagnostics.Debug.WriteLine($"[ERROR] Load versions: {ex.Message}");
            }
            finally
            {
                IsLoadingVersions = false;
            }



        }

        public ModsViewModel()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _minecraftDir = Path.Combine(appData, ".minecraft");
            _voxelModsDir = Path.Combine(_minecraftDir, "voxelmods");
            _activeModsDir = Path.Combine(_minecraftDir, "mods");
            Directory.CreateDirectory(_voxelModsDir);
            Directory.CreateDirectory(_activeModsDir);
            LoadInstalledModsAsync();

            NotificationRequested += async (t, m) =>
            {

            };
        }

        public event Action<string, string>? NotificationRequested;

        private int _offset = 0;
        private bool _hasMore = true;

        public class MinecraftVersion
        {
            public string Id { get; set; } = "";
            public string Type { get; set; } = ""; 
            public DateTime ReleaseTime { get; set; }
        }

        public class MinecraftVersionManifest
        {
            public List<MinecraftVersion> Versions { get; set; } = new();
        }

        public static class MinecraftVersionService
        {
            private static readonly HttpClient _http = new();
            private const string ManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";

            public static async Task<List<MinecraftVersion>> GetVersionsAsync()
            {
                try
                {
                    var json = await _http.GetStringAsync(ManifestUrl);
                    var manifest = JsonSerializer.Deserialize<MinecraftVersionManifest>(json);
                    return manifest?.Versions
                        .Where(v => v.Type == "release")
                        .OrderByDescending(v => v.ReleaseTime)
                        .ToList() ?? new();
                }
                catch
                {
                    return new();
                }
            }
        }
        public async Task LoadModsAsync(bool refresh = false)
        {
            if (IsLoading) return;
            if (refresh)
            {
                _offset = 0;
                Mods.Clear();
                _hasMore = true;
            }
            if (!_hasMore) return;

            IsLoading = true;
            try
            {
                var facets = new List<string> { "[\"project_type:mod\"]" };

                if (!string.IsNullOrEmpty(SelectedLoader))
                    facets.Add($"[\"categories:{SelectedLoader}\"]");

                if (!string.IsNullOrEmpty(SelectedCategory))
                    facets.Add($"[\"categories:{SelectedCategory.ToLower()}\"]");

                if (!string.IsNullOrEmpty(SelectedMinecraftVersion))
                {
                    facets.Add($"[\"versions:{SelectedMinecraftVersion}\"]");
                }

                var facetsJson = $"[{string.Join(",", facets)}]";
                var url = $"{ApiBase}/search?limit={PageSize}&offset={_offset}&facets={Uri.EscapeDataString(facetsJson)}";

                var json = await Http.GetStringAsync(url);
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                var hits = result.GetProperty("hits").EnumerateArray();

                foreach (var hit in hits)
                {
                    var mod = new ModrinthMod
                    {
                        Id = hit.GetProperty("project_id").GetString() ?? "",
                        Title = hit.GetProperty("title").GetString() ?? "",
                        IconUrl = hit.TryGetProperty("icon_url", out var icon) ? icon.GetString() ?? "" : "ms-appx:///Assets/Icons/mod.png",
                        Description = hit.GetProperty("description").GetString() ?? "", // ← Summary
                        Author = hit.TryGetProperty("author", out var author) ? author.GetString() ?? "Unknown" : "Unknown",
                        Downloads = hit.GetProperty("downloads").GetInt32(),
                        LatestVersion = hit.GetProperty("latest_version").GetString() ?? "",
                        Loaders = hit.TryGetProperty("loaders", out var loaders)
                            ? loaders.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                            : Array.Empty<string>()
                    };
                    Mods.Add(mod);
                }

                var totalHits = result.GetProperty("total_hits").GetInt32();
                _offset += PageSize;
                _hasMore = totalHits > _offset;
            }
            catch (Exception ex)
            {
            }
            finally
            {
                IsLoading = false;
                
            }
        }

        public async Task LoadMoreModsAsync()
        {
            if (!IsLoading && _hasMore)
                await LoadModsAsync();
        }

        public async Task DownloadAndInstallModAsync(ModrinthMod mod, ModrinthVersion version)
        {
            var file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault();
            if (file == null || string.IsNullOrEmpty(file.Url)) return;

            var voxelFilePath = Path.Combine(_voxelModsDir, $"{file.Filename}.voxelX");
            var metaFilePath = voxelFilePath + ".meta";

            try
            {
                var bytes = await Http.GetByteArrayAsync(file.Url);
                await File.WriteAllBytesAsync(voxelFilePath, bytes);
                await File.WriteAllTextAsync(metaFilePath, $"{mod.Id}|{version.VersionNumber}");
                mod.IsInstalled = true;
                mod.InstalledVersion = version.VersionNumber;
                mod.VoxelFilePath = voxelFilePath;
                mod.OriginalFilename = file.Filename;
                await LoadInstalledModsAsync();
                NotificationRequested?.Invoke("Cài đặt thành công", $"{mod.Title} v{version.VersionNumber}");
            }
            catch { }
        }

        public async Task LoadInstalledModsAsync()
        {
            InstalledMods.Clear();
            var voxelFiles = Directory.GetFiles(_voxelModsDir, "*.voxelX");
            foreach (var voxelFile in voxelFiles)
            {
                var metaFile = voxelFile + ".meta";
                if (!File.Exists(metaFile)) continue;
                var meta = await File.ReadAllTextAsync(metaFile);
                var parts = meta.Split('|');
                if (parts.Length < 2) continue;

                var projectId = parts[0];
                var versionNumber = parts[1];
                var originalName = Path.GetFileNameWithoutExtension(voxelFile);

                var mod = await FetchModInfoAsync(projectId, voxelFile, originalName, versionNumber);
                if (mod != null)
                {
                    mod.SetToggling(true);
                    mod.IsEnabled = File.Exists(Path.Combine(_activeModsDir, originalName));
                    mod.SetToggling(false);

                    mod.MarkAsInitialized();

                    InstalledMods.Add(mod);
                }
            }
        }

        private async Task<ModrinthMod?> FetchModInfoAsync(string projectId, string voxelFilePath, string originalName, string versionNumber)
        {
            try
            {
                var url = $"{ApiBase}/project/{projectId}";
                var json = await Http.GetStringAsync(url);
                var root = JsonDocument.Parse(json).RootElement;

                return new ModrinthMod
                {
                    Id = projectId,
                    Title = root.GetProperty("title").GetString() ?? "Unknown Mod",
                    IconUrl = root.TryGetProperty("icon_url", out var icon) ? icon.GetString() ?? "" : "ms-appx:///Assets/Icons/mod.png",
                    Description = root.GetProperty("description").GetString() ?? "",
                    Author = root.TryGetProperty("author", out var author) ? author.GetString() ?? "Unknown" : "Unknown",
                    InstalledVersion = versionNumber,
                    VoxelFilePath = voxelFilePath,
                    OriginalFilename = originalName,
                    IsInstalled = true
                };
            }
            catch
            {
                return null;
            }
        }
        public async Task ToggleModAsync(ModrinthMod mod)
        {
            if (!mod.IsInstalled || string.IsNullOrEmpty(mod.VoxelFilePath)) return;

            var activePath = Path.Combine(_activeModsDir, mod.OriginalFilename);
            mod.SetToggling(true);

            try
            {
                if (mod.IsEnabled)
                {
                    var bytes = await File.ReadAllBytesAsync(mod.VoxelFilePath);
                    await File.WriteAllBytesAsync(activePath, bytes);
                    NotificationRequested?.Invoke("Đã bật mod", mod.Title);
                }
                else
                {
                    if (File.Exists(activePath)) File.Delete(activePath);
                    NotificationRequested?.Invoke("Đã tắt mod", mod.Title);
                }
            }
            catch (Exception ex)
            {
                NotificationRequested?.Invoke("Lỗi", ex.Message);
            }
            finally
            {
                mod.SetToggling(false);
            }
        }
        public async Task DeleteModAsync(ModrinthMod mod)
        {
            try
            {
                if (File.Exists(mod.VoxelFilePath))
                    File.Delete(mod.VoxelFilePath);
                var metaPath = mod.VoxelFilePath + ".meta";
                if (File.Exists(metaPath))
                    File.Delete(metaPath);
                var activePath = Path.Combine(_activeModsDir, mod.OriginalFilename);
                if (File.Exists(activePath))
                    File.Delete(activePath);
                InstalledMods.Remove(mod);

                NotificationRequested?.Invoke("Đã xóa mod", mod.Title);
            }
            catch (Exception ex)
            {
                NotificationRequested?.Invoke("Lỗi", "Không thể xóa: " + ex.Message);
            }
        }

        private bool _isShowingNotification = false;
        public async Task ShowNotificationAsync(string title, string message)
        {
            if (_isShowingNotification) return;

            _isShowingNotification = true;
        }
    }
}