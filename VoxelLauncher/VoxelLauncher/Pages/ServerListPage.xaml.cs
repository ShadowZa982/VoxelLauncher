// Pages/ServerListPage.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VoxelLauncher;
using VoxelLauncher.Controls;
using VoxelLauncher.Models;
using VoxelLauncher.Services;

namespace VoxelLauncher.Pages
{
    public sealed partial class ServerListPage : Page, INotifyPropertyChanged
    {
        private ObservableCollection<Server> _servers = new();
        private bool _isLoading = true;
        private static ContentDialog _currentDialog = null;
        private string _searchText = "";
        public event PropertyChangedEventHandler? PropertyChanged;
        private static readonly SemaphoreSlim _dialogSemaphore = new(1, 1);

        public ObservableCollection<Server> Servers
        {
            get => _servers;
            set => SetField(ref _servers, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetField(ref _isLoading, value);
        }

        public ServerListPage()
        {
            this.InitializeComponent();
            this.DataContext = this;
            Loaded += ServerListPage_Loaded;
        }

        private async void ServerListPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadServers();
        }

        private async Task LoadServers()
        {
            IsLoading = true;
            ErrorText.Visibility = Visibility.Collapsed;
            try
            {
                var response = await HttpService.GetFromJsonAsync<ServerListResponse>(
                    "https://server.mineclubvn.com/api/server");

                if (response?.Success == true && response.Data != null)
                {
                    Servers.Clear();
                    foreach (var server in response.Data)
                        Servers.Add(server);
                }
                else
                {
                    ShowError();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ServerList] Lỗi: {ex}");
                ShowError();
            }
            finally
            {
                IsLoading = false;
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetField(ref _searchText, value))
                {
                    FilterServers();
                }
            }
        }

        private void FilterServers()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                ServerListView.ItemsSource = Servers;
            }
            else
            {
                var lower = SearchText.Trim().ToLower();
                var filtered = Servers.Where(s =>
                    s.ServerName.ToLower().Contains(lower) ||
                    s.Tags.ToLower().Contains(lower)
                ).ToList();

                ServerListView.ItemsSource = filtered;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchText = SearchBox.Text;
        }

        private void ShowError()
        {
            ErrorText.Visibility = Visibility.Visible;
        }

        private void ServerCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Server server)
            {
                OpenServerDetail(server);
            }
        }

        private void ServerListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Server server)
            {
                OpenServerDetail(server);
            }
        }

        private async void OpenServerDetail(Server server)
        {
            if (!_dialogSemaphore.Wait(0))
                return; 

            try
            {
                var dialog = new ContentDialog
                {
                    Title = server.ServerName,
                    Content = new ServerDetailDialog { DataContext = server },
                    CloseButtonText = "Đóng",
                    XamlRoot = this.XamlRoot
                };

                await dialog.ShowAsync();
            }
            finally
            {
                _dialogSemaphore.Release();
            }
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

    }

}