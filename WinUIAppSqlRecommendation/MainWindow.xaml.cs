// MainWindow.xaml.cs (WinUI 3) - Minor Adjustments
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics; // For Debug.WriteLine

// Add other necessary using directives
using WinUIAppSqlRecommendation.Services; // Or your AI service namespace
// using YourProject.ViewModels;

namespace WinUIAppSqlRecommendation
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }

        public MainWindow()
        {
            this.InitializeComponent();

            ViewModel = new MainViewModel(this.DispatcherQueue);

            // Set the DataContext for data binding in XAML
            // RootGrid is the main container in the XAML
            RootGrid.DataContext = ViewModel;

        }

  
    }
}