// AnalysisWindow.xaml.cs (Removed SetMinWindowSize)
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using WinUIAppSqlRecommendation.Services; // Your services namespace
using Microsoft.UI; // Needed for WindowId
using Microsoft.UI.Windowing; // Needed for AppWindow, AppWindowPresenterKind, OverlappedPresenter
using WinRT.Interop; // Needed for WindowNative
using System.Diagnostics; // For Debug.Writeline

namespace WinUIAppSqlRecommendation
{
    public sealed partial class AnalysisWindow : Window
    {
        public AnalysisViewModel ViewModel { get; }
        private AppWindow _appWindow; // Store AppWindow instance

        public AnalysisWindow(IAiService aiService, QueryInfo queryInfo, DispatcherQueue dispatcher)
        {
            this.InitializeComponent();

            ViewModel = new AnalysisViewModel(aiService, queryInfo, dispatcher);
            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.DataContext = ViewModel;
            }
            else
            {
                Debug.WriteLine("Warning: Could not set DataContext on root FrameworkElement.");
            }

            _appWindow = GetAppWindowForCurrentWindow();
            SetWindowSize(700, 550); // Set initial size

            // Removed call to SetMinWindowSize as the method doesn't exist
            // SetMinWindowSize(500, 400);

            this.Activated += AnalysisWindow_Activated;
            this.Closed += AnalysisWindow_Closed;
        }

        private AppWindow GetAppWindowForCurrentWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            return AppWindow.GetFromWindowId(wndId);
        }

        private void SetWindowSize(int width, int height)
        {
            if (_appWindow != null)
            {
                _appWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, height));
            }
        }

        // Removed SetMinWindowSize method entirely as it's not supported
        /*
        private void SetMinWindowSize(int minWidth, int minHeight)
        {
             // OverlappedPresenter does not have SetMinMaxSize
        }
        */

        private async void AnalysisWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            this.Activated -= AnalysisWindow_Activated;
            await ViewModel.StartAnalysisStreaming();
        }

        private void AnalysisWindow_Closed(object sender, WindowEventArgs args)
        {
            ViewModel?.Cleanup();
            this.Closed -= AnalysisWindow_Closed;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}