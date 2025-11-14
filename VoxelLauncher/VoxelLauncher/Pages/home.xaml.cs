using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge.Versions;
using CmlLib.Core.Installer.NeoForge.Versions;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.Version;
using CmlLib.Core.VersionMetadata;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VoxelLauncher.Pages;

namespace VoxelLauncher.Pages
{
    public sealed partial class home : Page
    {
        private readonly TimeSpan _fadeDuration = TimeSpan.FromSeconds(1.5);
        private readonly TimeSpan _displayDuration = TimeSpan.FromSeconds(4);
        private readonly HttpClient _fabricHttpClient = new(new HttpClientHandler())
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        private List<FabricLoader> _fabricLoaders = new();
        private List<string> _forgeVersions = new();

        private MinecraftPath _mcPath;
        private MinecraftLauncher _launcher;
        private MinecraftVersionManager _versionManager = null!;

        private string AppDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxelLauncher");
        private string ProfilesFile => Path.Combine(AppDataFolder, "profiles.json");
        private List<MinecraftProfile> _profiles = new();
        private MinecraftProfile? _currentProfile;
        private readonly IComparer<string> _versionComparer = new VersionComparer();

        public home()
        {
            this.InitializeComponent();
            this.Loaded += Home_Loaded;

            _mcPath = new MinecraftPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft"));
            _launcher = new MinecraftLauncher(_mcPath);

            var cacheFile = Path.Combine(AppDataFolder, "versions_cache_v2.json");
            _versionManager = new MinecraftVersionManager(_launcher, cacheFile);
        }

        private void Home_Loaded(object sender, RoutedEventArgs e)
        {
            StartImageSlideshow();
            _ = LoadProfilesAsync();
        }

        private async void ManageProfiles_Click(object sender, RoutedEventArgs e)
        {
            await ShowProfileListDialog();
        }

        #region === SLIDESHOW ===
        private async void StartImageSlideshow()
        {
            while (true)
            {
                await Fade(ImageBorder1, 1, 0);
                await Fade(ImageBorder2, 0, 1);
                await Task.Delay(_displayDuration);
                await Fade(ImageBorder2, 1, 0);
                await Fade(ImageBorder1, 0, 1);
                await Task.Delay(_displayDuration);
            }
        }

        private async Task Fade(FrameworkElement element, double fromOpacity, double toOpacity)
        {
            var animation = new DoubleAnimation
            {
                From = fromOpacity,
                To = toOpacity,
                Duration = _fadeDuration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(animation, element);
            Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);
            var tcs = new TaskCompletionSource<bool>();
            storyboard.Completed += (s, e) => tcs.SetResult(true);
            storyboard.Begin();
            await tcs.Task;
        }
        #endregion

        #region === PROFILE DIALOGS ===
        private async Task LoadProfilesAsync()
        {
            try
            {
                Directory.CreateDirectory(AppDataFolder);
                if (File.Exists(ProfilesFile))
                {
                    var json = await File.ReadAllTextAsync(ProfilesFile);
                    _profiles = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListMinecraftProfile) ?? new();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadProfilesAsync error: {ex}");
            }
        }

        private async Task ShowProfileListDialog()
        {
            var tcs = new TaskCompletionSource<bool>();
            var enqueued = DispatcherQueue.TryEnqueue(async () =>
            {
                try { await ShowProfileListDialogInternal(); }
                finally { tcs.SetResult(true); }
            });
            if (!enqueued) await ShowProfileListDialogInternal();
            else await tcs.Task;
        }

        private async Task ShowProfileListDialogInternal()
        {
            var dialog = new ContentDialog
            {
                Title = "Quản lý Profile",
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
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
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
                        Background = new SolidColorBrush(Microsoft.UI.Colors.DimGray),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(12),
                        Margin = new Thickness(0, 6, 0, 6),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                    };

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
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    try
                    {
                        selectPanel.Children.Add(new Image
                        {
                            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(profile.IconPath)),
                            Width = 40,
                            Height = 40,
                            Stretch = Stretch.UniformToFill
                        });
                    }
                    catch { /* ignore */ }

                    selectPanel.Children.Add(new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = profile.Name, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) },
                            new TextBlock { Text = $"{profile.Version} • {profile.Loader.ToUpper()}", FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.Colors.LightGray) }
                        }
                    });

                    selectPanel.Tapped += async (s, e) =>
                    {
                        dialog.Hide();
                        await NavigateToClientPage(profile);
                    };

                    Grid.SetColumn(selectPanel, 0);
                    grid.Children.Add(selectPanel);

                    var deleteBtn = new Button
                    {
                        Content = new SymbolIcon { Symbol = Symbol.Delete },
                        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red),
                        Width = 36,
                        Height = 36,
                        CornerRadius = new CornerRadius(18),
                        Padding = new Thickness(8)
                    };

                    deleteBtn.Click += async (s, e) =>
                    {
                        dialog.Hide();
                        await ConfirmDelete(profile);
                        await ShowProfileListDialog();
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
                await Task.Delay(100);
                await CreateNewProfileDialog();
            }
        }

        private async Task CreateNewProfileDialog()
        {
            var tcs = new TaskCompletionSource<bool>();
            var enqueued = DispatcherQueue.TryEnqueue(async () =>
            {
                try { await CreateNewProfileDialogInternal(); }
                finally { tcs.SetResult(true); }
            });
            if (!enqueued) await CreateNewProfileDialogInternal();
            else await tcs.Task;
        }

        private async Task CreateNewProfileDialogInternal()
        {
            var dialog = new ContentDialog
            {
                Title = "Tạo Profile Mới",
                XamlRoot = this.XamlRoot,
                PrimaryButtonText = "Tạo",
                CloseButtonText = "Hủy",
                DefaultButton = ContentDialogButton.Primary
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
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
                },
                Margin = new Thickness(20),
                RowSpacing = 16,
                ColumnSpacing = 12
            };

            mainGrid.Children.Add(new TextBlock { Text = "Tên profile:", FontWeight = FontWeights.SemiBold });
            var nameBox = new TextBox { PlaceholderText = "Để trống = tự động" };
            Grid.SetRow(nameBox, 0); Grid.SetColumn(nameBox, 1);
            mainGrid.Children.Add(nameBox);
            var loaderLabel = new TextBlock { Text = "Mod Loader:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 0) };
            Grid.SetRow(loaderLabel, 1); mainGrid.Children.Add(loaderLabel);
            var loaderBox = new ComboBox
            {
                ItemsSource = new[] { "Vanilla", "Fabric", "Quilt", "Forge", "NeoForge" },
                SelectedIndex = 0,
                Margin = new Thickness(0, 20, 0, 0)
            };
            Grid.SetRow(loaderBox, 1); Grid.SetColumn(loaderBox, 1);
            mainGrid.Children.Add(loaderBox);
            var versionLabel = new TextBlock { Text = "Phiên bản Minecraft:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 0) };
            var versionBox = new ComboBox
            {
                PlaceholderText = "Đang tải...",
                IsEnabled = false,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var searchBox = new TextBox
            {
                PlaceholderText = "Tìm kiếm phiên bản...",
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed
            };
            var filterComboBox = new ComboBox
            {
                PlaceholderText = "Tất cả phiên bản",
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Visibility = Visibility.Collapsed
            };
            foreach (var item in new[] { ("Tất cả", "all"), ("Release", "release"), ("Snapshot", "snapshot"), ("Pre-release", "-pre"), ("RC", "-rc"), ("Old", "old_") })
                filterComboBox.Items.Add(new ComboBoxItem { Content = item.Item1, Tag = item.Item2 });
            filterComboBox.SelectedIndex = 1;

            var loadingPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var loadingRing = new ProgressRing { IsActive = true, Width = 16, Height = 16 };
            var loadingText = new TextBlock { Text = "Đang tải phiên bản...", FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            loadingPanel.Children.Add(loadingRing);
            loadingPanel.Children.Add(loadingText);

            var leftStack = new StackPanel { Spacing = 8 };
            leftStack.Children.Add(versionLabel);
            leftStack.Children.Add(filterComboBox);
            leftStack.Children.Add(searchBox);
            leftStack.Children.Add(loadingPanel);
            leftStack.Children.Add(versionBox);
            Grid.SetRow(leftStack, 2); Grid.SetColumn(leftStack, 0);
            mainGrid.Children.Add(leftStack);
            var loaderVersionLabel = new TextBlock { Text = "Loader Version:", FontWeight = FontWeights.SemiBold, Opacity = 0.5, Margin = new Thickness(0, 20, 0, 0) };
            Grid.SetRow(loaderVersionLabel, 3); mainGrid.Children.Add(loaderVersionLabel);
            var loaderVersionBox = new ComboBox
            {
                PlaceholderText = "Chọn phiên bản trước...",
                IsEnabled = false,
                Opacity = 0.5
            };
            Grid.SetRow(loaderVersionBox, 3); Grid.SetColumn(loaderVersionBox, 1);
            mainGrid.Children.Add(loaderVersionBox);

            mainGrid.Children.Add(new TextBlock { Text = "RAM (MB):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 0) });
            Grid.SetRow((FrameworkElement)mainGrid.Children[^1], 4); Grid.SetColumn((FrameworkElement)mainGrid.Children[^1], 0);
            var ramBox = new NumberBox { Value = 4096, Minimum = 1024, Maximum = 32768, SmallChange = 512, LargeChange = 2048, Margin = new Thickness(0, 20, 0, 0) };
            Grid.SetRow(ramBox, 4); Grid.SetColumn(ramBox, 1);
            mainGrid.Children.Add(ramBox);

            dialog.Content = new ScrollViewer { Content = mainGrid };
            var showTask = dialog.ShowAsync();
            List<MinecraftVersionInfo> versions = new();
            _ = Task.Run(async () =>
            {
                try
                {
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    versions = await _versionManager.GetVersionsAsync(cts.Token);

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        leftStack.Children.Remove(loadingPanel);
                        if (!versions.Any())
                        {
                            versionBox.Items.Add("Không có phiên bản");
                            versionBox.IsEnabled = false;
                            return;
                        }

                        var allData = versions.ToList();
                        var latestRelease = allData.Where(v => v.Type == "release").MaxBy(v => v.ReleaseTime)?.Name;
                        var latestSnapshot = allData.Where(v => v.Type == "snapshot").MaxBy(v => v.ReleaseTime)?.Name;

                        void ApplyFilterAndSearch()
                        {
                            var filter = ((ComboBoxItem)filterComboBox.SelectedItem)?.Tag?.ToString() ?? "release";
                            var search = searchBox.Text?.Trim().ToLower() ?? "";

                            var filtered = allData.AsEnumerable();

                            filtered = filter switch
                            {
                                "all" => filtered,
                                "release" => filtered.Where(v => v.Type == "release"),
                                "snapshot" => filtered.Where(v => v.Type == "snapshot"),
                                "-pre" => filtered.Where(v => v.Name.Contains("-pre")),
                                "-rc" => filtered.Where(v => v.Name.Contains("-rc")),
                                "old_" => filtered.Where(v => v.Type.Contains("old")),
                                _ => filtered.Where(v => v.Type == "release")
                            };

                            if (!string.IsNullOrEmpty(search))
                                filtered = filtered.Where(v => v.Name.Contains(search, StringComparison.OrdinalIgnoreCase));

                            var list = filtered
                                .OrderByDescending(v => v.ReleaseTime ?? DateTime.MinValue)
                                .ThenByDescending(v => v.Name, _versionComparer)
                                .Select(v => new
                                {
                                    v.Name,
                                    Display = $"{v.Name} {(v.Name == latestRelease ? "[Latest]" : v.Name == latestSnapshot ? "[Latest Snapshot]" : "")}".Trim()
                                })
                                .ToList();

                            versionBox.ItemsSource = list.Select(x => x.Display).ToList();
                            versionBox.Tag = list.Select(x => x.Name).ToList();
                            versionBox.IsEnabled = true;
                            versionBox.PlaceholderText = "Chọn phiên bản...";
                            if (list.Any())
                            {
                                var defaultVer = filter == "snapshot" ? latestSnapshot : latestRelease;
                                var idx = list.FindIndex(x => x.Name == defaultVer);
                                versionBox.SelectedIndex = idx >= 0 ? idx : 0;
                            }
                        }

                        void UpdateUIVisibility()
                        {
                            var isVanilla = loaderBox.SelectedItem?.ToString()?.Contains("Vanilla") == true;
                            filterComboBox.Visibility = isVanilla ? Visibility.Visible : Visibility.Collapsed;
                            searchBox.Visibility = isVanilla ? Visibility.Visible : Visibility.Collapsed;
                            loaderVersionBox.IsEnabled = !isVanilla;
                            loaderVersionBox.Opacity = isVanilla ? 0.5 : 1;
                        }

                        filterComboBox.SelectionChanged += (s, e) => ApplyFilterAndSearch();
                        searchBox.TextChanged += (s, e) => ApplyFilterAndSearch();
                        loaderBox.SelectionChanged += (s, e) =>
                        {
                            UpdateUIVisibility();
                            ApplyFilterAndSearch();
                            var idx = versionBox.SelectedIndex;
                            if (idx >= 0)
                            {
                                var mcVer = ((List<string>)versionBox.Tag)[idx];
                                if (!string.IsNullOrEmpty(mcVer) && !loaderBox.SelectedItem?.ToString()?.Contains("Vanilla") == true)
                                    _ = LoadLoaderVersions(mcVer, loaderBox.SelectedItem?.ToString()?.ToLower(), loaderVersionBox);
                            }
                        };
                        versionBox.SelectionChanged += async (s, e) =>
                        {
                            var idx = versionBox.SelectedIndex;
                            if (idx < 0) return;
                            var mcVer = ((List<string>)versionBox.Tag)[idx];
                            var sel = loaderBox.SelectedItem?.ToString()?.ToLower();
                            if (sel != null && !sel.Contains("vanilla"))
                                await LoadLoaderVersions(mcVer, sel, loaderVersionBox);
                        };

                        UpdateUIVisibility();
                        ApplyFilterAndSearch();
                    });
                }
                catch (Exception ex)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        leftStack.Children.Remove(loadingPanel);
                        versionBox.Items.Add($"Lỗi: {ex.Message}");
                        versionBox.IsEnabled = false;
                    });
                }
            });
            async Task LoadLoaderVersions(string mcVersion, string loader, ComboBox box)
            {
                box.ItemsSource = null;
                box.PlaceholderText = "Đang tải...";
                box.IsEnabled = false;

                if (string.IsNullOrEmpty(mcVersion) || loader == null || loader.Contains("vanilla")) return;

                try
                {
                    if (loader == "forge")
                    {
                        var forge = new ForgeVersionLoader(_fabricHttpClient);
                        var versions = await forge.GetForgeVersions(mcVersion);
                        if (!versions.Any()) { box.Items.Add("Không hỗ trợ"); }
                        else
                        {
                            var list = versions.OrderByDescending(v => v.ForgeVersionName, _versionComparer).ToList();
                            _forgeVersions = list.Select(v => v.ForgeVersionName).ToList();
                            var display = list.Select(v => $"{v.ForgeVersionName} {(v.IsRecommendedVersion ? "Recommended" : v.IsLatestVersion ? "Latest" : "")}").ToList();
                            box.ItemsSource = display;
                            var rec = list.FirstOrDefault(v => v.IsRecommendedVersion);
                            box.SelectedIndex = rec != null ? list.IndexOf(rec) : 0;
                        }
                    }
                    else if (loader == "neoforge")
                    {
                        var neo = new NeoForgeVersionLoader(_fabricHttpClient);
                        var versions = await neo.GetNeoForgeVersions(mcVersion);
                        if (!versions.Any()) { box.Items.Add("Không hỗ trợ"); }
                        else
                        {
                            var list = versions.OrderByDescending(v => v.VersionName).ToList();
                            _forgeVersions = list.Select(v => v.VersionName).ToList();
                            var display = list.Select(v => $"{v.VersionName} {(v.VersionName == list[0].VersionName ? "Recommended" : "")}").ToList();
                            box.ItemsSource = display;
                            box.SelectedIndex = 0;
                        }
                    }
                    else if (loader == "fabric" || loader == "quilt")
                    {
                        var fabric = new FabricInstaller(_fabricHttpClient);
                        var loaders = await fabric.GetLoaders(mcVersion);
                        _fabricLoaders = loaders.ToList();
                        var display = _fabricLoaders.Select(l => $"{l.Version} {(l.Stable ? "Stable" : "Beta")}").ToList();
                        box.ItemsSource = display;
                        var stable = _fabricLoaders.FirstOrDefault(l => l.Stable);
                        box.SelectedIndex = stable != null ? _fabricLoaders.IndexOf(stable) : 0;
                    }

                    box.IsEnabled = true;
                    box.PlaceholderText = "Chọn loader version...";
                }
                catch (Exception ex)
                {
                    box.Items.Add($"Lỗi: {ex.Message}");
                    Debug.WriteLine(ex);
                }
            }
            var result = await showTask;
            if (result != ContentDialogResult.Primary) return;

            var idx = versionBox.SelectedIndex;
            if (idx < 0)
            {
                await new ContentDialog
                {
                    Title = "Lỗi",
                    Content = "Chưa chọn phiên bản.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                }.ShowAsync();
                return;
            }

            var nameList = (List<string>)versionBox.Tag;
            var selectedVersion = nameList[idx];
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
                    loaderVersion = (string?)loaderVersionBox.SelectedItem ?? "";
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
            await NavigateToClientPage(profile);
        }

        private async Task SaveProfilesAsync()
        {
            var json = JsonSerializer.Serialize(_profiles, AppJsonContext.Default.ListMinecraftProfile);
            await File.WriteAllTextAsync(ProfilesFile, json);
        }

        private async Task NavigateToClientPage(MinecraftProfile profile)
        {
            await File.WriteAllTextAsync(Path.Combine(AppDataFolder, "current_profile.txt"), profile.Id);
            Frame?.Navigate(typeof(ClientPage), null, new DrillInNavigationTransitionInfo());
        }

        private async Task ConfirmDelete(MinecraftProfile profile)
        {
            var confirm = new ContentDialog
            {
                Title = "Xóa Profile?",
                Content = $"Xóa vĩnh viễn \"{profile.Name}\"?",
                PrimaryButtonText = "Xóa",
                CloseButtonText = "Hủy",
                XamlRoot = this.XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            _profiles.Remove(profile);
            await SaveProfilesAsync();
            try { if (Directory.Exists(profile.InstallPath)) Directory.Delete(profile.InstallPath, true); }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }
        #endregion

        #region === HELPER CLASSES ===
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
                    var px = partsX[i]; var py = partsY[i];
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

        public class MinecraftVersionManager
        {
            private readonly MinecraftLauncher _launcher;
            private readonly string _cacheFile;
            private List<MinecraftVersionInfo> _cached = new();
            private DateTime _lastUpdated = DateTime.MinValue;
            private readonly TimeSpan _cacheDuration = TimeSpan.FromHours(2);

            public MinecraftVersionManager(MinecraftLauncher launcher, string cacheFile)
            {
                _launcher = launcher;
                _cacheFile = cacheFile;
            }

            public async Task<List<MinecraftVersionInfo>> GetVersionsAsync(CancellationToken ct = default)
            {
                if (_cached.Any() && DateTime.Now - _lastUpdated < _cacheDuration)
                    return _cached;

                try
                {
                    if (File.Exists(_cacheFile))
                    {
                        var json = await File.ReadAllTextAsync(_cacheFile, ct);
                        var cached = JsonSerializer.Deserialize<List<MinecraftVersionInfo>>(json);
                        if (cached != null && cached.Any())
                        {
                            _cached = cached;
                            _lastUpdated = File.GetLastWriteTime(_cacheFile);
                            return _cached;
                        }
                    }

                    ct.ThrowIfCancellationRequested();
                    var versions = await _launcher.GetAllVersionsAsync();

                    _cached = versions.Select(v => new MinecraftVersionInfo
                    {
                        Name = v.Name,
                        Type = v.GetVersionType().ToString().ToLower(),
                        ReleaseTime = v.ReleaseTime.UtcDateTime
                    }).ToList();

                    _lastUpdated = DateTime.Now;
                    Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
                    await File.WriteAllTextAsync(_cacheFile, JsonSerializer.Serialize(_cached));
                }
                catch
                {
                    if (!_cached.Any() && File.Exists(_cacheFile))
                    {
                        var json = await File.ReadAllTextAsync(_cacheFile);
                        _cached = JsonSerializer.Deserialize<List<MinecraftVersionInfo>>(json) ?? new();
                    }
                }

                return _cached;
            }
        }

        public class MinecraftVersionInfo
        {
            public string Name { get; set; } = "";
            public string Type { get; set; } = "";
            public DateTime? ReleaseTime { get; set; }
        }
        #endregion
    }
}