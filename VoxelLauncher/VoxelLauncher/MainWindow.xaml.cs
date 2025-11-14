using AutoUpdaterDotNET;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.FileExtractors;
using CmlLib.Core.Files;
using CmlLib.Core.Installers;
using CmlLib.Core.Java;
using CmlLib.Core.Rules;
using CmlLib.Core.Version;
using CmlLib.Core.VersionLoader;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Squirrel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using VoxelLauncher.Pages;
using VoxelLauncher.ViewModels;
using Windows.Storage.Streams;
using Windows.UI;
using WinRT.Interop;
using XboxAuthNet.Game.Msal;
using XboxAuthNet.Game.Msal.OAuth;

namespace VoxelLauncher
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        => (value is bool b) && (b ^ (parameter?.ToString() == "inverse")) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
    }
    public sealed partial class MainWindow : Window
    {
        private bool _isExpanded = false;
        private AppWindow? _appWindow;
        private readonly AppViewModel _vm = new();
        private MSession? _session;
        private readonly List<string> _logCache = new();
        private const string CLIENT_ID = "562d66cb-9c20-49c3-bdd8-3e101f79a8ab";
        private const string MS_ACCOUNTS_FILE = "ms_accounts.json";
        private readonly Button[] _tabButtons;
        private XamlRoot? _sharedXamlRoot;
        private CancellationTokenSource? _updateCts;
        private double _lastReportedProgress = -1;
        private DateTime _lastUiUpdate = DateTime.MinValue;
        public XamlRoot? SharedXamlRoot => _sharedXamlRoot;
        // ---------- Sidebar animation ----------
        private readonly CompositeTransform _sidebarTransform = new();
        private bool _sidebarOpen = false;
        private readonly TimeSpan _sidebarDuration = TimeSpan.FromMilliseconds(280);
        private readonly CubicEase _ease = new() { EasingMode = EasingMode.EaseOut };
        public CompositeTransform SidebarTransform { get; } = new CompositeTransform();
        private CancellationTokenSource? _chatGuideCts;
        private bool _isChatGuideShowing = false;
        public AppViewModel ViewModel => _vm;
        private static readonly string VoxelClientFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxelLauncher");
        private static readonly string AccountsFolder = Path.Combine(VoxelClientFolder, "accounts");
        private static readonly string WelcomeShownFile = Path.Combine(VoxelClientFolder, "welcome_shown.json");
        private static readonly string PendingUpdatesFile = Path.Combine(VoxelClientFolder, "pending_updates.json");

        public MainWindow()
        {

            InitializeComponent();              
            Directory.CreateDirectory(AccountsFolder);
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            if (this.Content is FrameworkElement root)  
            {
                root.DataContext = _vm;            
            }
            _tabButtons = new[] { ChangelogButton, PlayButton, SettingsButton };
            this.Activated += MainWindow_Activated;
            InitializeWindow();
            InitializeWebView2();
            SetupCustomTitleBar();
            (App.Current as App)!.ViewModel = _vm;
            StartLoading();
            MainFrame.Navigate(typeof(Pages.home));
            MainFrame.Navigated += MainFrame_Navigated;
            SidebarTransform.TranslateX = -260;
            StartupLoadingProgressBar.Height = 24;

            this.DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(500);
                if (MainFrame.Content is ClientPage clientPage)
                {
                    var method = clientPage.GetType().GetMethod("TryLoadSessionAsync",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (method != null)
                        await (Task)method.Invoke(clientPage, null)!;
                }
            });

            this.DispatcherQueue.TryEnqueue(() =>
            {
                StartLoading(); 
            });
        }

        private async Task CheckForUpdatesAsync()
        {
            if (_updateCts != null) return;

            try
            {
                _updateCts = new CancellationTokenSource();
                var client = new HttpClient();
                var appcastUrl = "https://github.com/ShadowZa982/VoxelLauncher/releases/latest/download/appcast.xml";
                AppendLog("Đang kiểm tra cập nhật ...", "#60A5FA");

                string xmlContent;
                try
                {
                    xmlContent = await client.GetStringAsync(appcastUrl, _updateCts.Token);
                }
                catch (HttpRequestException ex)
                {
                    AppendLog("Không thể kết nối đến máy chủ cập nhật.", "#F59E0B");
                    return;
                }

                var doc = new System.Xml.XmlDocument();
                try
                {
                    doc.LoadXml(xmlContent);
                }
                catch (XmlException)
                {
                    AppendLog("File appcast không hợp lệ.", "#F59E0B");
                    return;
                }

                var nsmgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("sparkle", "http://www.andymatuschak.org/xml-namespaces/sparkle");

                var itemNode = doc.SelectSingleNode("//item");
                if (itemNode == null)
                {
                    AppendLog("Không có bản cập nhật nào hiện tại.", "#94A3B8");
                    return;
                }

                var versionNode = itemNode.SelectSingleNode("sparkle:version", nsmgr) ?? itemNode.SelectSingleNode("version");
                if (versionNode == null || string.IsNullOrWhiteSpace(versionNode.InnerText))
                {
                    AppendLog("Không tìm thấy thông tin phiên bản.", "#F59E0B");
                    return;
                }

                var onlineVersionStr = versionNode.InnerText.Trim();
                if (!Version.TryParse(onlineVersionStr, out _))
                {
                    AppendLog($"Phiên bản không hợp lệ: {onlineVersionStr}", "#F59E0B");
                    return;
                }

                var enclosureNode = itemNode.SelectSingleNode("enclosure");
                if (enclosureNode?.Attributes["url"] == null || string.IsNullOrWhiteSpace(enclosureNode.Attributes["url"].Value))
                {
                    AppendLog("Không tìm thấy URL tải về.", "#F59E0B");
                    return;
                }

                var downloadUrl = enclosureNode.Attributes["url"].Value;
                var currentVersionStr = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

                if (!Version.TryParse(currentVersionStr, out var currentVersion))
                    currentVersion = new Version(0, 0, 0);

                if (!Version.TryParse(onlineVersionStr, out var onlineVersion))
                {
                    AppendLog("Không thể phân tích phiên bản online.", "#F59E0B");
                    return;
                }

                if (onlineVersion <= currentVersion)
                {
                    AppendLog("Bạn đang dùng phiên bản mới nhất.", "#94A3B8");
                    return;
                }

                var newVersion = onlineVersionStr;
                AppendLog($"Đã tìm thấy bản cập nhật v{newVersion}!", "#10B981");
                var descNode = itemNode.SelectSingleNode("description");
                var updateNotesHtml = descNode?.InnerText?.Trim() ?? $"<h3>Phiên bản mới: v{newVersion}</h3>";
                var webView = new Microsoft.UI.Xaml.Controls.WebView2 { Height = 250 };
                var dialog = new ContentDialog
                {
                    Title = "Cập nhật sẵn sàng!",
                    PrimaryButtonText = "Cập nhật ngay",
                    CloseButtonText = "Để sau",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.Content.XamlRoot,
                    Content = new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = new StackPanel
                        {
                            Spacing = 12,
                            Children =
                    {
                        webView,
                        new TextBlock
                        {
                            Text = "Tự động cài đặt sau khi tải xong.",
                            FontSize = 13,
                            Foreground = new SolidColorBrush(Colors.Gray),
                            HorizontalAlignment = HorizontalAlignment.Center
                        }
                    }
                        }
                    }
                };

                await webView.EnsureCoreWebView2Async();
                webView.CoreWebView2.NavigateToString($@"
            <!DOCTYPE html><html><head><meta charset='utf-8'><style>
                body {{font-family:'Segoe UI',sans-serif;padding:16px;margin:0;color:#e2e8f0;line-height:1.5;}}
                h3 {{color:#10b981;margin:0 0 12px 0;font-size:18px;font-weight:600;}}
                ul {{margin:8px 0;padding-left:20px;}} li {{margin:6px 0;}}
                a {{color:#60a5fa;text-decoration:none;}} a:hover {{text-decoration:underline;}}
            </style></head><body>{updateNotesHtml}</body></html>");

                webView.CoreWebView2.DOMContentLoaded += async (s, e) =>
                {
                    try
                    {
                        var h = await webView.CoreWebView2.ExecuteScriptAsync("document.body.scrollHeight;");
                        if (int.TryParse(h, out int height))
                            this.DispatcherQueue.TryEnqueue(() => webView.Height = Math.Min(height + 32, 350));
                    }
                    catch { }
                };

                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    AppendLog("Người dùng hủy cập nhật.", "#F59E0B");
                    _vm.PendingUpdates.Add(new PendingUpdateInfo
                    {
                        Version = newVersion,
                        NotesHtml = updateNotesHtml,
                        DownloadUrl = downloadUrl,
                        SkippedAt = DateTime.Now
                    });
                    _vm.PendingUpdateCount = _vm.PendingUpdates.Count;
                    await SavePendingUpdatesAsync();
                    return;
                }
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateProgressOverlay.Visibility = Visibility.Visible;
                    ViewModel.UpdateStatus = "Đang tải file cài đặt...";
                    CancelUpdateButton.Visibility = Visibility.Visible;
                    UpdateProgressBar.IsIndeterminate = true;
                });

                var exePath = Path.Combine(Path.GetTempPath(), $"VoxelLauncher_v{newVersion}.exe");
                if (File.Exists(exePath)) try { File.Delete(exePath); } catch { }
                using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, _updateCts.Token);
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                await DownloadFileAsync(response, exePath, _updateCts.Token, totalBytes);
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateProgressBar.IsIndeterminate = false;
                    ViewModel.UpdateStatus = "Đang khởi động trình cài đặt...";
                });

                if (!File.Exists(exePath) || new FileInfo(exePath).Length == 0)
                    throw new Exception("File tải về bị lỗi hoặc rỗng.");

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                var process = Process.Start(startInfo);
                if (process == null) throw new Exception("Không thể khởi động file cài đặt.");

                AppendLog($"Đã khởi động trình cài đặt (PID: {process.Id})", "#10B981");
                await Task.Delay(2000);
                this.DispatcherQueue.TryEnqueue(() => UpdateProgressOverlay.Visibility = Visibility.Collapsed);
                Environment.Exit(0);
            }
            catch (OperationCanceledException)
            {
                AppendLog("Đã hủy tải cập nhật.", "#F59E0B");
            }
            catch (Exception ex)
            {
                AppendLog($"Lỗi cập nhật: {ex.Message}", "#EF4444");
                await ShowErrorDialogAsync("Lỗi cập nhật", ex.Message);
            }
            finally
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateProgressOverlay.Visibility = Visibility.Collapsed;
                    CancelUpdateButton.Visibility = Visibility.Collapsed;
                });
                _updateCts?.Dispose();
                _updateCts = null;
            }
        }

        private async Task DownloadFileAsync( HttpResponseMessage response, string filePath, CancellationToken ct, long totalBytes)
        {
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);

            var buffer = new byte[65536];
            int bytesRead;
            var sw = Stopwatch.StartNew();

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);

                // Cập nhật trạng thái mỗi 500ms
                if (sw.ElapsedMilliseconds > 500)
                {
                    var downloadedMB = fileStream.Length / 1024.0 / 1024.0;
                    var speed = (fileStream.Length / 1024.0 / 1024.0) / (sw.Elapsed.TotalSeconds);
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        ViewModel.UpdateStatus = $"Đang tải: {downloadedMB:F1} MB ({speed:F1} MB/s)...";
                    });
                    sw.Restart();
                }
            }
        }
        
        private async Task SavePendingUpdatesAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_vm.PendingUpdates, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(PendingUpdatesFile, json);
            }
            catch { }
        }

        private async Task LoadPendingUpdatesAsync()
        {
            try
            {
                if (File.Exists(PendingUpdatesFile))
                {
                    var json = await File.ReadAllTextAsync(PendingUpdatesFile);
                    var updates = JsonSerializer.Deserialize<List<PendingUpdateInfo>>(json);
                    if (updates != null)
                    {
                        _vm.PendingUpdates = updates;
                        _vm.PendingUpdateCount = updates.Count;
                    }
                }
            }
            catch { }
        }
        private async Task ShowErrorDialogAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                MainFrame.Navigate(typeof(Pages.AccountPage), _vm, new DrillInNavigationTransitionInfo());
            });
        }
        // ---------- Toggle ----------
        private void MenuToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _sidebarOpen = !_sidebarOpen;
            SidebarOverlay.Visibility = Visibility.Visible;
            var toX = _sidebarOpen ? 0 : -260;
            AnimateSidebar(toX);
        }

        private void SidebarOverlay_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (_sidebarOpen && e.OriginalSource == SidebarOverlay)
            {
                _sidebarOpen = false;
                AnimateSidebar(-SidebarPanel.Width);
            }
        }

        // ---------- Animation ----------
        private async void AnimateSidebar(double toX)
        {
            var anim = new DoubleAnimation
            {
                From = SidebarTransform.TranslateX,
                To = toX,
                Duration = _sidebarDuration,
                EasingFunction = _ease
            };

            var sb = new Storyboard();
            Storyboard.SetTarget(anim, SidebarTransform); 
            Storyboard.SetTargetProperty(anim, "TranslateX");
            sb.Children.Add(anim);

            var tcs = new TaskCompletionSource<bool>();
            sb.Completed += (s, ev) => tcs.SetResult(true);
            sb.Begin();

            await tcs.Task;

            if (!_sidebarOpen)
                SidebarOverlay.Visibility = Visibility.Collapsed;
        }

        // ---------- Sidebar item navigation ----------
        private void SidebarItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tag) return;

            var pageType = Type.GetType($"VoxelLauncher.Pages.{tag}");
            if (pageType != null)
                MainFrame.Navigate(pageType, _vm, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromLeft });
            _sidebarOpen = false;
            AnimateSidebar(-SidebarPanel.Width);
        }
        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState == WindowActivationState.CodeActivated)
            {
                _appWindow = this.AppWindow;
                SetupCustomTitleBar();
                CenterWindow();
                _sharedXamlRoot = this.Content.XamlRoot;

                this.DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(500);

                    var config = await SettingsHelper.LoadAsync();
                    if (config.CheckForUpdates)
                        await CheckForUpdatesAsync();

                    await LoadPendingUpdatesAsync();
                });
                this.Activated -= MainWindow_Activated;

            }
        }

        private void InitializeWindow()
        {
            Title = "Voxel Launcher";
            CenterWindow();
            this.Closed += MainWindow_Closed;
        }

        private void SetupCustomTitleBar()
        {
            this.ExtendsContentIntoTitleBar = true;
            if (_appWindow?.TitleBar != null)
            {
                _appWindow.TitleBar.SetDragRectangles(new[]
                {
            new Windows.Graphics.RectInt32(0, 0, 10000, 36)
        });
            }
            UpdateTitleBarTextColor();
        }
        private void UpdateTitleBarTextColor()
        {
            TitleText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 46, 139, 87));
        }

        private async void InitializeWebView2()
        {
            try { await CoreWebView2Environment.CreateAsync(); }
            catch { }
        }

        private void TabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button clickedButton) return;

            foreach (var btn in _tabButtons)
            {
                btn.Background = btn == PlayButton
                    ? (SolidColorBrush)Application.Current.Resources["AccentButtonBackground"]
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 43, 43, 43));
            }

            clickedButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 88, 101, 242));

            if (clickedButton.Tag is not string tag || string.IsNullOrWhiteSpace(tag)) return;

            var pageType = Type.GetType($"VoxelLauncher.Pages.{tag}");
            if (pageType == null) return;

            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (MainFrame.Content?.GetType() != pageType)
                {
                    MainFrame.Navigate(pageType, _vm, new DrillInNavigationTransitionInfo());
                }
            });
        }
 
        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            var confirm = new ContentDialog
            {
                Title = "Đăng xuất?",
                Content = "Bạn có chắc chắn muốn đăng xuất?",
                PrimaryButtonText = "Đăng xuất",
                CloseButtonText = "Hủy",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;

            await PerformLogoutAsync();
        }

        private async Task PerformLogoutAsync()
        {
            var mcFolder = _vm.MinecraftFolder;
            var msFile = Path.Combine(mcFolder, "ms_accounts.json");
            var extraFile = Path.Combine(mcFolder, "account_extra.json");
            var nameFile = Path.Combine(mcFolder, "player_name.txt");

            try { File.Delete(msFile); } catch { }
            try { File.Delete(extraFile); } catch { }
            try { File.Delete(nameFile); } catch { }

            _vm.ResetAll();
            AppendLog("Đã đăng xuất.", "#F59E0B");
            UpdateAccountUI();
        }
        private async void ChangeAccount_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Chọn tài khoản",
                CloseButtonText = "Hủy",
                XamlRoot = this.Content.XamlRoot,
            };

            var rootGrid = new Grid
            {
                MaxWidth = 500,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(24, 16, 24, 16)
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 500
            };

            var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            var accounts = await LoadAllAccountsAsync();

            foreach (var acc in accounts.OrderByDescending(a => a.LastLogin))
            {
                var grid = new Grid
                {
                    ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
                    Padding = new Thickness(12, 8, 12, 8),
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Colors.Transparent)
                };
                grid.PointerEntered += (s, e) => grid.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
                grid.PointerExited += (s, e) => grid.Background = new SolidColorBrush(Colors.Transparent);
                var avatarBorder = new Border
                {
                    Width = 40,
                    Height = 40,
                    Margin = new Thickness(0, 0, 12, 0),
                    CornerRadius = new CornerRadius(8),
                    BorderBrush = new SolidColorBrush(Colors.LightGray),
                    BorderThickness = new Thickness(1),
                    Child = new Image
                    {
                        Source = new BitmapImage(new Uri(
                        acc.Type == "Microsoft" || acc.Name.Length > 3
                            ? $"https://mc-heads.net/avatar/{acc.Name}/32"
                            : "ms-appx:///Assets/Icons/player.png")),
                                    Stretch = Stretch.UniformToFill
                    }
                };
                Grid.SetColumn(avatarBorder, 0);
                grid.Children.Add(avatarBorder);
                var info = new StackPanel { Spacing = 2 };
                var nameText = new TextBlock
                {
                    Text = acc.Name,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 15
                };
                var detailText = new TextBlock
                {
                    Text = $"{acc.Type} • {acc.LastLogin:dd/MM/yyyy HH:mm}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.LightGray)
                };
                info.Children.Add(nameText);
                info.Children.Add(detailText);
                Grid.SetColumn(info, 1);
                grid.Children.Add(info);
                var deleteBtn = new Button
                {
                    Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(8)
                };
                deleteBtn.Click += (s, ev) =>
                {
                    dialog.Hide();
                    _ = DeleteFromListAsync(acc);
                };
                Grid.SetColumn(deleteBtn, 2);
                grid.Children.Add(deleteBtn);
                grid.Tapped += async (s, ev) =>
                {
                    dialog.Hide();
                    await SelectAndSaveToMinecraftAsync(acc);
                };

                stack.Children.Add(grid);
            }
            var addBtn = new Button
            {
                Content = "Thêm tài khoản mới",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 12, 0, 0),
                Style = (Style)Application.Current.Resources["AccentButtonStyle"]
            };
            addBtn.Click += (s, e) =>
            {
                dialog.Hide();
                LoginButton_Click(s, e);
            };
            stack.Children.Add(addBtn);

            scroll.Content = stack;
            rootGrid.Children.Add(scroll);
            dialog.Content = rootGrid;

            await dialog.ShowAsync();
        }

        private async Task SelectAndSaveToMinecraftAsync(AccountInfo acc)
        {
            var mcFolder = _vm.MinecraftFolder;
            if (acc.Type == "Microsoft")
            {
                var msFile = Path.Combine(mcFolder, "ms_accounts.json");
                var extraFile = Path.Combine(mcFolder, "account_extra.json");

                await new MSessionFileStorage(msFile).SaveAsync(acc.Session!);
                await File.WriteAllTextAsync(extraFile, JsonSerializer.Serialize(new
                {
                    Xuid = "N/A",
                    ClientId = "N/A",
                    LoginTime = DateTime.Now.ToString("o"),
                    TotalPlayTime = TimeSpan.Zero.ToString()
                }));

                _vm.Session = acc.Session;
                _vm.UserName = acc.Name;
                _vm.IsLoggedIn = true;
                _vm.LoginTime = DateTime.Now;
            }
            else
            {
                var nameFile = Path.Combine(mcFolder, "player_name.txt");
                await File.WriteAllTextAsync(nameFile, acc.Name);

                _vm.UserName = acc.Name;
                _vm.IsLoggedIn = true;
                _vm.LoginTime = DateTime.Now;
            }

            _vm.RefreshAccountType();
            UpdateAccountUI();
            AppendLog($"Đã chọn: {acc.Name}");
        }

        private async Task DeleteFromListAsync(AccountInfo acc)
        {
            var confirm = new ContentDialog
            {
                Title = "Xóa tài khoản?",
                Content = $"Xóa vĩnh viễn \"{acc.Name}\"?",
                PrimaryButtonText = "Xóa",
                CloseButtonText = "Hủy",
                XamlRoot = this.Content.XamlRoot
            };

            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;

            try
            {
                File.Delete(acc.FilePath);
                if (acc.ExtraFile != null && File.Exists(acc.ExtraFile))
                    File.Delete(acc.ExtraFile);
            }
            catch { }
            this.DispatcherQueue.TryEnqueue(() =>
            {
                ChangeAccount_Click(null, null);
            });
        }

        private void MainFrame_Navigated(object sender, NavigationEventArgs e)
        {
            this.DispatcherQueue.TryEnqueue(UpdateAccountUI);

            if (e.Content is Page page)
            {
                try
                {
                    var vm = page.GetType().GetProperty("VM")?.GetValue(page);
                    if (vm is INotifyPropertyChanged npc)
                    {
                        npc.PropertyChanged += (s, args) =>
                        {
                            if (args.PropertyName is "IsLoggedIn" or "UserName")
                                this.DispatcherQueue.TryEnqueue(UpdateAccountUI);
                        };
                    }
                }
                catch { }
            }
        }

        private void UpdateAccountUI()
        {
            if (_vm.IsLoggedIn && !string.IsNullOrEmpty(_vm.UserName))
            {
                UserNameText.Text = _vm.UserName;
                UserAvatar.Source = _vm.UserName.Contains("@") || _vm.UserName.Length > 3
                    ? new BitmapImage(new Uri($"https://mc-heads.net/avatar/{_vm.UserName}/28"))
                    : new BitmapImage(new Uri("ms-appx:///Assets/Icons/player.png"));
            }
            else
            {
                UserNameText.Text = "Đăng nhập";
                UserAvatar.Source = null;
            }

        }

        private void MainFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            var dialog = new ContentDialog()
            {
                Title = "Navigation Failed",
                Content = $"Could not navigate to {e.SourcePageType.FullName}\n{e.Exception.Message}",
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            _ = dialog.ShowAsync();
            e.Handled = true;
        }

        private async void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (!_vm.IsLoggedIn) return;

            var extraFile = Path.Combine(_vm.MinecraftFolder, "account_extra.json");
            var data = new
            {
                Xuid = _vm.Xuid ?? "N/A",
                ClientId = _vm.ClientId ?? "N/A",
                LoginTime = _vm.LoginTime.ToString("o"),
                TotalPlayTime = _vm.TotalPlayTime.ToString()
            };

            await File.WriteAllTextAsync(extraFile, JsonSerializer.Serialize(data));
        }

        private void CenterWindow()
        {
            if (this.AppWindow is not null)
            {
                var displayArea = DisplayArea.GetFromWindowId(this.AppWindow.Id, DisplayAreaFallback.Nearest);
                if (displayArea is not null)
                {
                    int width = 1200, height = 720;
                    int x = (displayArea.WorkArea.Width - width) / 2;
                    int y = (displayArea.WorkArea.Height - height) / 2;
                    this.AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
                    this.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
                }
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Đăng nhập",
                Content = "Chọn phương thức đăng nhập",
                PrimaryButtonText = "Microsoft",
                SecondaryButtonText = "Offline",
                CloseButtonText = "Hủy",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                await LoginWithMicrosoftAsync();
            else if (result == ContentDialogResult.Secondary)
                await LoginOfflineAsync();

            UpdateAccountUI();
        }
        private async Task LoginWithMicrosoftAsync()
        {
            try
            {
                var app = await MsalClientHelper.BuildApplicationWithCache(CLIENT_ID);
                var loginHandler = new JELoginHandlerBuilder()
                    .WithOAuthProvider(new MsalCodeFlowProvider(app))
                    .Build();
                var session = await loginHandler.AuthenticateInteractively();
                var mcFolder = _vm.MinecraftFolder;
                var msFile = Path.Combine(mcFolder, "ms_accounts.json");
                var extraFile = Path.Combine(mcFolder, "account_extra.json");
                await new MSessionFileStorage(msFile).SaveAsync(session);
                var extraData = new
                {
                    Xuid = "N/A",
                    ClientId = "N/A",
                    LoginTime = DateTime.Now.ToString("o"),
                    TotalPlayTime = TimeSpan.Zero.ToString()
                };
                await File.WriteAllTextAsync(extraFile, JsonSerializer.Serialize(extraData));
                _vm.Session = session;
                _vm.UserName = session.Username;
                _vm.IsLoggedIn = true;
                _vm.LoginTime = DateTime.Now;
                _vm.TotalPlayTime = TimeSpan.Zero;
                await SaveToAccountListAsync(session, "Microsoft");

                AppendLog($"Đăng nhập Microsoft: {session.Username}");
                _vm.RefreshAccountType();
                UpdateAccountUI();
            }
            catch (Exception ex)
            {
                AppendLog($"Lỗi đăng nhập: {ex.Message}", "#EF4444");
            }
        }

        private async Task LoginOfflineAsync()
        {
            var dialog = new ContentDialog
            {
                Title = "Chơi Offline",
                Content = new TextBox { PlaceholderText = "Tên người chơi..." },
                PrimaryButtonText = "Xác nhận",
                CloseButtonText = "Hủy",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var name = ((TextBox)dialog.Content).Text.Trim();
            if (string.IsNullOrEmpty(name)) name = "Player";
            var mcFolder = _vm.MinecraftFolder;
            var nameFile = Path.Combine(mcFolder, "player_name.txt");
            await File.WriteAllTextAsync(nameFile, name);
            _vm.UserName = name;
            _vm.IsLoggedIn = true;
            _vm.LoginTime = DateTime.Now;
            await SaveToAccountListAsync(null, "Offline", name);
            AppendLog($"Đăng nhập Offline: {name}");
            UpdateAccountUI();
        }

        private async Task SaveToAccountListAsync(MSession? session, string type, string? name = null)
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            var safeName = string.Join("_", (session?.Username ?? name!).Split(Path.GetInvalidFileNameChars()));
            var fileName = type == "Microsoft"
                ? $"{id}_{safeName}_ms.json"
                : $"{id}_{safeName}_offline.txt";
            var filePath = Path.Combine(AccountsFolder, fileName);
            var extraFile = Path.Combine(AccountsFolder, $"{id}_{safeName}_extra.json");

            if (type == "Microsoft")
                await new MSessionFileStorage(filePath).SaveAsync(session!);
            else
                await File.WriteAllTextAsync(filePath, name!);

            await File.WriteAllTextAsync(extraFile, JsonSerializer.Serialize(new
            {
                LastLogin = DateTime.Now.ToString("o")
            }));
        }

        private async void LoadMicrosoftAvatar(string username)
        {
            try
            {
                var url = $"https://mc-heads.net/avatar/{username}/80";
                var bitmap = new BitmapImage();
                bitmap.UriSource = new Uri(url);

                var tcs = new TaskCompletionSource<bool>();
                bitmap.ImageOpened += (s, e) => tcs.SetResult(true);
                bitmap.ImageFailed += (s, e) => tcs.SetResult(false);

                // AVATAR NHỎ
                var smallBitmap = new BitmapImage();
                smallBitmap.UriSource = new Uri(url);
                smallBitmap.DecodePixelWidth = 28;
                UserAvatar.Source = smallBitmap;

                await tcs.Task;
            }
            catch (Exception ex)
            {
                AppendLog($"Lỗi tải avatar: {ex.Message}", "#EF4444");
                var fallback = new BitmapImage(new Uri("ms-appx:///Assets/Icons/player.png"));
                UserAvatar.Source = fallback;

            }
        }

        private async void StartLoading()
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingOverlay.Opacity = 1.0;
            LoadingVideoPlayer.Visibility = Visibility.Visible;
            LoadingVideoPlayer.Opacity = 0;
            LoadingVideoPlayer.MediaPlayer.IsLoopingEnabled = false;
            LoadingVideoPlayer.MediaPlayer.Play();

            await FadeInAsync(LoadingVideoPlayer, 600);
            await Task.Delay(3500);

            var fadeOutVideo = FadeOutAsync(LoadingVideoPlayer, 800);
            LoadingGifImage.Visibility = Visibility.Visible;
            LoadingGifImage.Opacity = 0;
            var fadeInGif = FadeInAsync(LoadingGifImage, 600);

            await Task.WhenAll(fadeOutVideo, fadeInGif);
            LoadingVideoPlayer.Visibility = Visibility.Collapsed;
            await Task.Delay(3500);

            var fadeOutGif = FadeOutAsync(LoadingGifImage, 800);
            LoadingContent.Visibility = Visibility.Visible;
            LoadingContent.Opacity = 0;
            var fadeInLogo = FadeInAsync(LoadingContent, 600);

            await Task.WhenAll(fadeOutGif, fadeInLogo);
            LoadingGifImage.Visibility = Visibility.Collapsed;
            await RunRealLoadingAsync();
            await FadeOutAsync(LoadingOverlay, 500);
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        private async void TriggerChatGuideAfterDelay()
        {
            await Task.Delay(100);
            await ShowChatGuideAsync(
                message: "Chào mừng bạn đến với Voxel Launcher!\nNhấn Play để bắt đầu nhé!",
                displayDuration: TimeSpan.FromSeconds(60),
                gifPath: "ms-appx:///Assets/gif/ally.gif"
            );
        }

        private async Task FadeInAsync(UIElement element, int durationMs)
        {
            var fadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var sb = new Storyboard();
            Storyboard.SetTarget(fadeIn, element);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            sb.Children.Add(fadeIn);

            var tcs = new TaskCompletionSource<bool>();
            sb.Completed += (s, e) => tcs.SetResult(true);
            sb.Begin();

            await tcs.Task;
        }

        private async Task FadeOutAsync(UIElement element, int durationMs)
        {
            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var sb = new Storyboard();
            Storyboard.SetTarget(fadeOut, element);
            Storyboard.SetTargetProperty(fadeOut, "Opacity");
            sb.Children.Add(fadeOut);

            var tcs = new TaskCompletionSource<bool>();
            sb.Completed += (s, e) => tcs.SetResult(true);
            sb.Begin();

            await tcs.Task;
        }

        private async Task RunRealLoadingAsync()
        {
            try
            {

                LoadingText.Text = "Khởi tạo trình khởi chạy...";
                var path = new MinecraftPath();
                var http = new HttpClient();
                var parameters = MinecraftLauncherParameters.CreateDefault(path, http);
                await Task.Delay(300);

                LoadingText.Text = "Đang thiết lập cấu hình launcher...";
                parameters.JavaPathResolver = new MinecraftJavaPathResolver(path);
                var javaPath = parameters.JavaPathResolver.GetInstalledJavaVersions();
                AppendLog($"Sử dụng Java tại: {javaPath}", "#22C55E");
                await Task.Delay(1000);

                parameters.RulesEvaluator = new RulesEvaluator();
                LoadingText.Text = "Đang tải danh sách phiên bản...";
                parameters.VersionLoader = new MojangJsonVersionLoaderV2(path, http);
                var allVersions = await parameters.VersionLoader.GetVersionMetadatasAsync();
                AppendLog($"Đã tải {allVersions.Count()} phiên bản Minecraft.", "#60A5FA");
                await Task.Delay(1000);

                parameters.GameInstaller = ParallelGameInstaller.CreateAsCoreCount(http);
                var extractors = DefaultFileExtractors.CreateDefault(http, parameters.RulesEvaluator!, parameters.JavaPathResolver!);
                extractors.Asset!.AssetServer = MojangServer.ResourceDownload;
                extractors.Library!.LibraryServer = MojangServer.Library;
                parameters.FileExtractors = extractors.ToExtractorCollection();
                await Task.Delay(1000);

                LoadingText.Text = "Đang đăng nhập tài khoản...";
                await TryLoadSessionAsync();
                await Task.Delay(1000);

                LoadingText.Text = "Hoàn tất!";
                AppendLog("Launcher sẵn sàng!");
                this.DispatcherQueue.TryEnqueue(TriggerChatGuideAfterDelay);
                this.DispatcherQueue.TryEnqueue(ShowWelcomeIfNeeded);
            }
            catch (Exception ex)
            {
                LoadingText.Text = "Lỗi: " + ex.Message;
                AppendLog($"Lỗi khởi tạo: {ex}", "#EF4444");
            }
        }

        private void AppendLog(string text, string color = "#FFFFFF")
        {
            var logLine = $"[{DateTime.Now:HH:mm:ss}] {text}\n";
            lock (_logCache) _logCache.Add(logLine);

            if (MainFrame.Content is ClientPage clientPage &&
                clientPage.DispatcherQueue != null)
            {
                clientPage.DispatcherQueue.TryEnqueue(() =>
                    clientPage.AppendLogFromMain(logLine, color));
            }
        }

        private T? FindDescendant<T>(DependencyObject parent, string? name) where T : DependencyObject
        {
            if (parent == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t && (string.IsNullOrEmpty(name) || (t as FrameworkElement)?.Name == name))
                    return t;

                var result = FindDescendant<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private void Account_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(typeof(Pages.AccountPage), _vm, new DrillInNavigationTransitionInfo());
        }

        private void Info_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(typeof(Pages.AccountPage), _vm, new DrillInNavigationTransitionInfo());
        }
        private async Task TryLoadSessionAsync()
        {
            var mcFolder = _vm.MinecraftFolder;
            var msFile = Path.Combine(mcFolder, "ms_accounts.json");
            var nameFile = Path.Combine(mcFolder, "player_name.txt");
            var extraFile = Path.Combine(mcFolder, "account_extra.json");
            if (File.Exists(msFile))
            {
                try
                {
                    var session = await new MSessionFileStorage(msFile).LoadAsync();
                    if (session != null && !string.IsNullOrEmpty(session.Username))
                    {
                        _vm.Session = session;
                        _vm.UserName = session.Username;
                        _vm.IsLoggedIn = true;

                        if (File.Exists(extraFile))
                        {
                            var json = await File.ReadAllTextAsync(extraFile);
                            var data = JsonSerializer.Deserialize<JsonElement>(json);
                            _vm.LoginTime = data.TryGetProperty("LoginTime", out var lt)
                                ? DateTime.Parse(lt.GetString()!)
                                : DateTime.Now;
                        }
                        else
                        {
                            _vm.LoginTime = DateTime.Now;
                        }

                        AppendLog($"Tự động đăng nhập: {session.Username}");
                        UpdateAccountUI();
                        var config = await SettingsHelper.LoadAsync();
                        if (config.ShowLoginNotification)
                        {
                            await ShowLoginNotificationAsync(
                                username: session.Username,
                                avatarUrl: $"https://mc-heads.net/avatar/{session.Username}/64"
                            );
                        }

                        return;
                    }
                }
                catch { }
            }

            if (File.Exists(nameFile))
            {
                try
                {
                    var name = (await File.ReadAllTextAsync(nameFile)).Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        _vm.UserName = name;
                        _vm.IsLoggedIn = true;
                        _vm.LoginTime = File.GetLastWriteTime(nameFile);
                        AppendLog($"Tự động đăng nhập Offline: {name}");
                        UpdateAccountUI();
                        var config = await SettingsHelper.LoadAsync();
                        if (config.ShowLoginNotification)
                        {
                            await ShowLoginNotificationAsync(
                                username: name,
                                avatarUrl: "ms-appx:///Assets/Icons/player.png"
                            );
                        }

                        return;
                    }
                }
                catch { }
            }

            _vm.ResetAll();
            _vm.RefreshAccountType();
            UpdateAccountUI();
        }

        private async Task<List<AccountInfo>> LoadAllAccountsAsync()
        {
            var accounts = new List<AccountInfo>();
            foreach (var file in Directory.GetFiles(AccountsFolder, "*_ms.json"))
            {
                try
                {
                    var session = await new MSessionFileStorage(file).LoadAsync();
                    if (session != null && !string.IsNullOrEmpty(session.Username))
                    {
                        var extraFile = file.Replace(".json", "_extra.json");
                        var lastLogin = File.Exists(extraFile)
                            ? JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(extraFile))
                                .GetProperty("LastLogin").GetDateTime()
                            : File.GetLastWriteTime(file);

                        accounts.Add(new AccountInfo
                        {
                            Id = Path.GetFileNameWithoutExtension(file).Split('_')[0],
                            Name = session.Username,
                            Type = "Microsoft",
                            FilePath = file,
                            ExtraFile = extraFile,
                            Session = session,
                            LastLogin = lastLogin
                        });
                    }
                }
                catch { }
            }
            foreach (var file in Directory.GetFiles(AccountsFolder, "*_offline.txt"))
            {
                try
                {
                    var name = (await File.ReadAllTextAsync(file)).Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        var extraFile = file.Replace(".txt", "_extra.json");
                        var lastLogin = File.Exists(extraFile)
                            ? JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(extraFile))
                                .GetProperty("LastLogin").GetDateTime()
                            : File.GetLastWriteTime(file);

                        accounts.Add(new AccountInfo
                        {
                            Id = Path.GetFileNameWithoutExtension(file).Split('_')[0],
                            Name = name,
                            Type = "Offline",
                            FilePath = file,
                            ExtraFile = extraFile,
                            LastLogin = lastLogin
                        });
                    }
                }
                catch { }
            }

            return accounts;
        }

        private async void NotificationButton_Click(object sender, RoutedEventArgs e)
        {
            NotificationStack.Children.Clear();
            foreach (var update in _vm.PendingUpdates.OrderByDescending(u => u.SkippedAt))
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 4, 0, 4)
                };
                var stack = new StackPanel { Spacing = 8 };
                var title = new TextBlock
                {
                    Text = $"Cập nhật v{update.Version} (Bỏ qua: {update.SkippedAt:dd/MM HH:mm})",
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Colors.White)
                };
                stack.Children.Add(title);
                var webView = new Microsoft.UI.Xaml.Controls.WebView2
                {
                    Height = 120,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                await webView.EnsureCoreWebView2Async();
                webView.CoreWebView2.NavigateToString($@"
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset='utf-8'>
            <style>
            body {{ font-family: 'Segoe UI', sans-serif; padding: 8px; color: #E5E7EB; line-height:1.4; margin:0; }}
            h4 {{ color: #10B981; margin:0 0 4px 0; }}
            ul {{ padding-left: 16px; }}
            </style>
            </head>
            <body>
            {update.NotesHtml}
            </body>
            </html>");

                webView.CoreWebView2.DOMContentLoaded += async (s, e2) =>
                {
                    try
                    {
                        var heightStr = await webView.CoreWebView2.ExecuteScriptAsync("document.body.scrollHeight;");
                        if (int.TryParse(heightStr, out int height))
                        {
                            webView.Height = Math.Min(height + 16, 300);
                        }
                    }
                    catch { }
                };
                stack.Children.Add(webView);

                var btnPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var installBtn = new Button
                {
                    Content = "Cài đặt ngay",
                    Style = (Style)Application.Current.Resources["AccentButtonStyle"]
                };
                installBtn.Click += async (s, ev) =>
                {
                    NotificationOverlay.Visibility = Visibility.Collapsed;
                    await InstallPendingUpdateAsync(update);
                };

                var deleteBtn = new Button { Content = "Xóa" };
                deleteBtn.Click += (s, ev) =>
                {
                    _vm.PendingUpdates.Remove(update);
                    _vm.PendingUpdateCount = _vm.PendingUpdates.Count;
                    _ = SavePendingUpdatesAsync();
                    NotificationButton_Click(null, null);
                };

                btnPanel.Children.Add(installBtn);
                btnPanel.Children.Add(deleteBtn);
                stack.Children.Add(btnPanel);
                border.Child = stack;
                NotificationStack.Children.Add(border);
            }

            if (!_vm.PendingUpdates.Any())
            {
                NotificationStack.Children.Add(new TextBlock
                {
                    Text = "Không có cập nhật nào bị bỏ qua.",
                    Foreground = new SolidColorBrush(Colors.Gray),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                });
            }

            NotificationOverlay.Visibility = Visibility.Visible;
        }

        private void CloseNotificationOverlay_Click(object sender, RoutedEventArgs e)
        {
            NotificationOverlay.Visibility = Visibility.Collapsed;
        }

        private async Task InstallPendingUpdateAsync(PendingUpdateInfo update)
        {
            var cts = new CancellationTokenSource();
            try
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateProgressOverlay.Visibility = Visibility.Visible;
                    ViewModel.UpdateStatus = "Đang tải file cài đặt...";
                    CancelUpdateButton.Visibility = Visibility.Visible;
                    UpdateProgressBar.IsIndeterminate = true;
                });

                var client = new HttpClient();
                var exePath = Path.Combine(Path.GetTempPath(), $"VoxelLauncher_v{update.Version}.exe");
                if (File.Exists(exePath)) try { File.Delete(exePath); } catch { }
                using var response = await client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                await DownloadFileAsync(response, exePath, cts.Token, totalBytes);
                _vm.PendingUpdates.Remove(update);
                _vm.PendingUpdateCount = _vm.PendingUpdates.Count;
                await SavePendingUpdatesAsync();
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateProgressBar.IsIndeterminate = false;
                    ViewModel.UpdateStatus = "Đang khởi động trình cài đặt...";
                });

                if (!File.Exists(exePath) || new FileInfo(exePath).Length == 0)
                    throw new Exception("File tải về bị lỗi hoặc rỗng.");

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                var process = Process.Start(startInfo);
                if (process == null) throw new Exception("Không thể khởi động file cài đặt.");

                AppendLog($"Đã khởi động trình cài đặt v{update.Version} (PID: {process.Id})", "#10B981");

                await Task.Delay(2000);
                this.DispatcherQueue.TryEnqueue(() => UpdateProgressOverlay.Visibility = Visibility.Collapsed);
                Environment.Exit(0);
            }
            catch (OperationCanceledException)
            {
                AppendLog("Đã hủy cài đặt cập nhật.", "#F59E0B");
            }
            catch (Exception ex)
            {
                AppendLog($"Lỗi cài đặt: {ex.Message}", "#EF4444");
                await ShowErrorDialogAsync("Lỗi cài đặt", ex.Message);
            }
            finally
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateProgressOverlay.Visibility = Visibility.Collapsed;
                    CancelUpdateButton.Visibility = Visibility.Collapsed;
                });
                cts.Dispose();
            }
        }

        private void CancelUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            _updateCts?.Cancel();
        }

        private class AccountInfo
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
            public string Name { get; set; } = "";
            public string Type { get; set; } = "";
            public string FilePath { get; set; } = "";
            public string? ExtraFile { get; set; }
            public MSession? Session { get; set; }
            public DateTime LastLogin { get; set; } = DateTime.MinValue;
        }

        private async Task ShowLoginNotificationAsync(string username, string avatarUrl = "")
        {
            try
            {
                var builder = new AppNotificationBuilder()
                    .AddText("Đã đăng nhập tự động")
                    .AddText(username, new AppNotificationTextProperties().SetMaxLines(1))
                    .SetTimeStamp(DateTime.Now)
                    .SetAttributionText("Voxel Launcher");
                var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "logo", "serve_logor.png");
                if (File.Exists(logoPath))
                {
                    builder.SetAppLogoOverride(new Uri(logoPath), AppNotificationImageCrop.Circle);
                }
                if (!string.IsNullOrEmpty(avatarUrl) && Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri))
                {
                    builder.SetHeroImage(uri);
                }

                var notification = builder.BuildNotification();
                AppNotificationManager.Default.Show(notification);
            }
            catch (Exception ex)
            {
                AppendLog($"Lỗi gửi thông báo: {ex.Message}", "#EF4444");
            }
        }

        private async Task ShowChatGuideAsync(string message, TimeSpan displayDuration, string gifPath = "ms-appx:///Assets/gif/allay.gif")
        {
            if (_isChatGuideShowing) return;
            _isChatGuideShowing = true;
            _chatGuideCts?.Cancel();
            _chatGuideCts = new CancellationTokenSource();

            try
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    ChatTooltipText.Text = message;
                    ChatGuideGif.Source = new BitmapImage(new Uri(gifPath));
                    ChatGuideOverlay.Visibility = Visibility.Visible;
                    ChatGuideGif.Visibility = Visibility.Visible;
                    ChatTooltip.Visibility = Visibility.Visible;
                    ChatGuideGif.Opacity = 0;
                    ChatTooltip.Opacity = 0;
                });

                await FadeInAsync(ChatGuideGif, 400);
                await Task.Delay(200);
                await FadeInAsync(ChatTooltip, 400);

                await Task.Delay(displayDuration, _chatGuideCts.Token);

                await FadeOutAsync(ChatTooltip, 300);
                await FadeOutAsync(ChatGuideGif, 300);

                this.DispatcherQueue.TryEnqueue(() =>
                {
                    ChatGuideOverlay.Visibility = Visibility.Collapsed;
                    ChatGuideGif.Visibility = Visibility.Collapsed;
                    ChatTooltip.Visibility = Visibility.Collapsed;
                });
            }
            catch (OperationCanceledException)
            {
                await ForceHideChatGuideAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"Lỗi hiển thị hướng dẫn: {ex.Message}", "#EF4444");
            }
            finally
            {
                _isChatGuideShowing = false;
            }
        }

        private async Task ForceHideChatGuideAsync()
        {
            await FadeOutAsync(ChatTooltip, 200);
            await FadeOutAsync(ChatGuideGif, 200);
            this.DispatcherQueue.TryEnqueue(() =>
            {
                ChatGuideOverlay.Visibility = Visibility.Collapsed;
                ChatGuideGif.Visibility = Visibility.Collapsed;
                ChatTooltip.Visibility = Visibility.Collapsed;
            });
        }

        private async void ShowWelcomeIfNeeded()
        {
            if (File.Exists(WelcomeShownFile))
                return;
            var welcomeMessage = @"
                🎉 Chào mừng bạn đến với Voxel Launcher!

                Đây là phiên bản chính thức với nhiều cải tiến:

                ✅ Đăng nhập Microsoft & Offline
                ✅ Tự động cập nhật launcher
                ✅ Hỗ trợ Java 8/17/21
                ✅ Giao diện mượt mà, tối ưu
                ✅ Hỗ trợ mod, resource pack
                ✅ Tích hợp thông báo & hướng dẫn

                Nhấn Play để bắt đầu hành trình của bạn!
                Chúng tôi sẽ tiếp tục cập nhật thêm nhiều tính năng hay ho.

                Cảm ơn bạn đã ủng hộ dự án!
                ";
            this.DispatcherQueue.TryEnqueue(() =>
            {
                WelcomeContentText.Text = welcomeMessage.Trim();
                WelcomeOverlay.Visibility = Visibility.Visible;
            });
        }

        private async void CloseWelcome_Click(object sender, RoutedEventArgs e)
        {
            WelcomeOverlay.Visibility = Visibility.Collapsed;
            try
            {
                var data = new { Shown = true, Version = "1.0", Date = DateTime.Now.ToString("o") };
                await File.WriteAllTextAsync(WelcomeShownFile, JsonSerializer.Serialize(data));
            }
            catch { }
        }

        private void WelcomeOverlay_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (e.OriginalSource == WelcomeOverlay)
            {
                CloseWelcome_Click(null, null);
            }
        }
    }
}