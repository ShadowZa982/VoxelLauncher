// Pages/ModsPage.xaml.cs
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using VoxelLauncher.ViewModels;

namespace VoxelLauncher.Pages
{
    public sealed partial class ModsPage : Page
    {
        private ModsViewModel VM => (ModsViewModel)DataContext;
        private bool _isSidebarOpen = false;
        private readonly TimeSpan _duration = TimeSpan.FromMilliseconds(250);
        private readonly CubicEase _ease = new() { EasingMode = EasingMode.EaseOut };
        private bool _isManageMode = false;
        private bool _hasLoadedInstalledMods = false;
        public ModsPage()
        {
            this.InitializeComponent();
            this.Loaded += ModsPage_Loaded;
            VM.NotificationRequested += (title, message) =>
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    _ = ShowNotificationAsync(title, message);
                });
            };
            WeakReferenceMessenger.Default.Register<ModrinthMod.ModToggleMessage>(this, async (r, m) =>
            {
                await VM.ToggleModAsync(m.Mod);
            });
        }
        private async void ModsPage_Loaded(object sender, RoutedEventArgs e)
        {
            this.Loaded -= ModsPage_Loaded;

            _currentView = ViewMode.Browse;
            BrowseContentContainer.Visibility = Visibility.Visible;
            ManageContentContainer.Visibility = Visibility.Collapsed;
            BrowseButton.Style = (Style)Resources["ActiveSourceButtonStyle"];
            ManageButton.Style = (Style)Resources["SourceButtonStyle"];
            UpdateCategoryButtonStyles();
            if (VM.MinecraftVersions.Count == 0)
                await VM.LoadMinecraftVersionsAsync();

            await VM.LoadModsAsync(refresh: true);
        }
        private async void LoaderFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LoaderFilter.SelectedItem is ComboBoxItem item && item.Tag is string loader)
            {
                VM.SelectedLoader = loader;
                await VM.LoadModsAsync(refresh: true);
            }
        }
        private async void ScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (e.IsIntermediate) return;
            if (sender is ScrollViewer scrollViewer &&
                scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight * 0.8)
            {
                await VM.LoadMoreModsAsync();
            }
        }

        private async Task<string?> ShowMinecraftVersionPickerAsync()
        {
            var dialog = new ContentDialog
            {
                Title = "Chọn phiên bản Minecraft",
                XamlRoot = this.XamlRoot,
                PrimaryButtonText = "Chọn",
                CloseButtonText = "Hủy",
                DefaultButton = ContentDialogButton.Primary
            };

            var searchBox = new TextBox { PlaceholderText = "Tìm kiếm..." };
            var listView = new ListView
            {
                ItemsSource = VM.MinecraftVersions,
                Margin = new Thickness(0, 8, 0, 0),
                MaxHeight = 200
            };

            searchBox.TextChanged += (s, e) =>
            {
                var text = searchBox.Text.Trim().ToLower();
                listView.ItemsSource = string.IsNullOrEmpty(text)
                    ? VM.MinecraftVersions
                    : VM.MinecraftVersions.Where(v => v.ToLower().Contains(text));
            };

            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(new TextBlock
            {
                Text = "Chỉ hiển thị phiên bản chính thức (release)",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.Gray)
            });
            stack.Children.Add(searchBox);
            stack.Children.Add(new ScrollViewer { Content = listView, MaxHeight = 500 });
            dialog.Content = stack;

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary || listView.SelectedItem is not string selected)
                return null;

            return selected.Replace(" [Latest]", "").Trim();
        }

        private async void ModItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (((Grid)sender).Tag is not ModrinthMod mod) return;

            if (!mod.IsVersionsLoaded)
            {
                var loadingDialog = new ContentDialog
                {
                    Title = "Đang tải phiên bản...",
                    XamlRoot = this.XamlRoot,
                    Content = new ProgressRing { IsActive = true, Width = 48, Height = 48 },
                    CloseButtonText = "Hủy"
                };
                var dialogTask = loadingDialog.ShowAsync();
                try
                {

                    await mod.LoadVersionsAsync(VM.Http, VM.SelectedLoader, VM.SelectedMinecraftVersion);
                }
                finally
                {
                    loadingDialog.Hide();
                }
            }

            if (!mod.Versions.Any())
            {
                await new ContentDialog
                {
                    Title = "Thông báo",
                    Content = "Không có phiên bản phù hợp với bộ lọc đã chọn.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                }.ShowAsync();
                return;
            }

            await ShowModVersionsDialog(mod);
        }

        private async Task ShowModVersionsDialog(ModrinthMod mod)
        {
            var dialog = new ContentDialog
            {
                Title = mod.Title,
                XamlRoot = this.XamlRoot,
                CloseButtonText = "Hủy"
            };

            var listView = new ListView
            {
                ItemsSource = mod.Versions,
                SelectionMode = ListViewSelectionMode.None,
                IsItemClickEnabled = true,
                ItemTemplate = this.Resources["VersionItemTemplate"] as DataTemplate
            };

            listView.ItemClick += async (s, args) =>
            {
                if (args.ClickedItem is ModrinthVersion version)
                {
                    dialog.Hide();
                    await VM.DownloadAndInstallModAsync(mod, version);
                    await ShowNotificationAsync("Đã cài mod", $"{mod.Title} v{version.VersionNumber}");
                }
            };

            dialog.Content = new ScrollViewer
            {
                Content = listView,
                MaxHeight = 300,
                Padding = new Thickness(0, 8, 0, 0)
            };

            await dialog.ShowAsync();
        }
        public async Task ShowNotificationAsync(string title, string message)
        {
            NotificationTitle.Text = title;
            NotificationMessage.Text = message;
            NotificationBorder.Opacity = 0;
            NotificationBorder.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(200) };
            var sbIn = new Storyboard();
            sbIn.Children.Add(fadeIn);
            Storyboard.SetTarget(fadeIn, NotificationBorder);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            await sbIn.BeginAsync();

            await Task.Delay(3000);

            var fadeOut = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(300) };
            var sbOut = new Storyboard();
            sbOut.Children.Add(fadeOut);
            Storyboard.SetTarget(fadeOut, NotificationBorder);
            Storyboard.SetTargetProperty(fadeOut, "Opacity");
            sbOut.Completed += (s, e) => NotificationBorder.Visibility = Visibility.Collapsed;
            await sbOut.BeginAsync();
        }

        private async void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Content is string category)
            {
                var newCategory = VM.SelectedCategory == category ? "" : category;
                VM.SelectedCategory = newCategory;

                UpdateCategoryButtonStyles();

                await VM.LoadModsAsync(refresh: true);
            }
        }

        private void UpdateCategoryButtonStyles()
        {
            var buttons = FindCategoryButtons(this);
            foreach (var btn in buttons)
            {
                if (btn.Content is string cat && cat == VM.SelectedCategory)
                    btn.Style = (Style)Resources["ActiveFilterButtonStyle"];
                else
                    btn.Style = (Style)Resources["FilterButtonStyle"];
            }
        }

        private List<Button> FindCategoryButtons(DependencyObject parent)
        {
            var buttons = new List<Button>();
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Button btn &&
                    (btn.Style == (Style)Resources["FilterButtonStyle"] ||
                     btn.Style == (Style)Resources["ActiveFilterButtonStyle"]))
                {
                    buttons.Add(btn);
                }
                else
                {
                    buttons.AddRange(FindCategoryButtons(child));
                }
            }
            return buttons;
        }

        private async void MenuToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isSidebarOpen = !_isSidebarOpen;
            SidebarPanel.Visibility = Visibility.Visible;
            SidebarPanel.Visibility = _isSidebarOpen ? Visibility.Visible : Visibility.Collapsed;

            double sidebarX = _isSidebarOpen ? 0 : -260;
            double browseX = _isSidebarOpen ? 260 : 0;

            await Task.WhenAll(
                AnimateTransform(SidebarTransform, sidebarX)
                
            );

            if (!_isSidebarOpen)
                SidebarPanel.Visibility = Visibility.Collapsed;
        }

        private async Task AnimateTransform(TranslateTransform t, double toX)
        {
            var anim = new DoubleAnimation
            {
                From = t.X,
                To = toX,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var sb = new Storyboard();
            sb.Children.Add(anim);
            Storyboard.SetTarget(anim, t);
            Storyboard.SetTargetProperty(anim, "X");
            sb.Begin();

            await Task.Delay(300);
        }


        private async void VersionButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = await ShowMinecraftVersionPickerAsync();
            if (selected != null)
            {
                var cleanName = selected.Replace(" [Latest]", "").Trim();
                VM.SelectedMinecraftVersion = cleanName;
                await VM.LoadModsAsync(refresh: true);
            }
        }

        private async void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            VM.SelectedLoader = "";
            VM.SelectedMinecraftVersion = "";
            VM.SelectedCategory = "";
            LoaderFilter.SelectedIndex = 0;

            await VM.LoadModsAsync(refresh: true);
        }
        private enum ViewMode
        {
            Browse,
            Manage
        }

        private ViewMode _currentView = ViewMode.Browse;

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentView == ViewMode.Browse) return;
            _currentView = ViewMode.Browse;
            ManageContentContainer.Visibility = Visibility.Collapsed;
            BrowseContentContainer.Visibility = Visibility.Visible;
            BrowseButton.Style = (Style)Resources["ActiveSourceButtonStyle"];
            ManageButton.Style = (Style)Resources["SourceButtonStyle"];
        }

        private async void ManageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentView == ViewMode.Manage) return;

            _currentView = ViewMode.Manage;
            ManageContentContainer.Visibility = Visibility.Visible;
            BrowseContentContainer.Visibility = Visibility.Collapsed;

            if (!_hasLoadedInstalledMods)
            {
                await VM.LoadInstalledModsAsync();
                _hasLoadedInstalledMods = true;
            }

            ManageButton.Style = (Style)Resources["ActiveSourceButtonStyle"];
            BrowseButton.Style = (Style)Resources["SourceButtonStyle"];
        }

        private async void DeleteModButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ModrinthMod mod)
            {
                var dialog = new ContentDialog
                {
                    Title = "Xóa mod?",
                    Content = $"Bạn có chắc muốn xóa {mod.Title} v{mod.InstalledVersion}?",
                    PrimaryButtonText = "Xóa",
                    CloseButtonText = "Hủy",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary) return;

                await VM.DeleteModAsync(mod);
            }
        }

        private void ManageModItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (e.OriginalSource is ToggleButton ||
                e.OriginalSource is Button ||
                e.OriginalSource is Ellipse)
            {
                return;
            }
            if ((sender as Grid)?.DataContext is ModrinthMod mod)
            {
               
            }
        }
    }

    public static class StoryboardExtensions
    {
        public static Task BeginAsync(this Storyboard storyboard)
        {
            var tcs = new TaskCompletionSource<bool>();
            void handler(object s, object e) { storyboard.Completed -= handler; tcs.SetResult(true); }
            storyboard.Completed += handler;
            storyboard.Begin();
            return tcs.Task;
        }
    }
}