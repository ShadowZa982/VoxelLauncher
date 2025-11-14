using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Text;
using VoxelLauncher.ViewModels;
using VoxelLauncher;
using WinRT.Interop;

namespace VoxelLauncher
{
    public partial class App : Application
    {
        private Window? _window;
        public Window? Window { get; private set; }
        public Window? MainWindowInstance { get; set; }
        public AppWindow? MainAppWindow { get; set; }
        public AppViewModel ViewModel { get; set; } = new();
        public static App CurrentApp => (App)Application.Current;
        //public static List<ServerConsoleWindow> OpenConsoleWindows { get; } = new();
        public static IntPtr MainWindowHandle { get; private set; }
        public App()
        {
            this.InitializeComponent();
            this.UnhandledException += (sender, e) =>
            {
                e.Handled = true;
                System.Diagnostics.Debug.WriteLine($"Lỗi: {e.Exception}");
            };
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);



        }
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var mainWindow = new MainWindow 
            {
                SystemBackdrop = new MicaBackdrop(),
                ExtendsContentIntoTitleBar = true
            };

            _window = mainWindow;
            MainWindowHandle = WindowNative.GetWindowHandle(_window);
            _window.Activate();

            _window = mainWindow;
            MainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            _window.Activate();
            MainWindowInstance = mainWindow;
            Window = _window;
            var hWnd = WindowNative.GetWindowHandle(_window);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            MainAppWindow = AppWindow.GetFromWindowId(windowId);
            if (mainWindow is MainWindow mw)
            {
                ViewModel = mw.ViewModel;
            }
            else
            {
                ViewModel = new AppViewModel();
            }
            
            var res = Application.Current.Resources;
            if (res.ContainsKey("ImageSourceConverter"))
            {
                System.Diagnostics.Debug.WriteLine("ImageSourceConverter ĐÃ TÌM THẤY!");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("ImageSourceConverter VẪN KHÔNG THẤY!");
            }

        }

        public static void InitializePicker(object picker)
        {
            InitializeWithWindow.Initialize(picker, MainWindowHandle);
        }

        private AppWindow GetAppWindow(Window window)
        {
            var hWnd = WindowNative.GetWindowHandle(window);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            return AppWindow.GetFromWindowId(windowId);
        }
    }
}