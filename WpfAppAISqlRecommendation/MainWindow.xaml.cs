// MainWindow.xaml.cs
using System.Windows;

namespace SqlPerformanceAiAdvisor
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Set the DataContext here or ensure it's set in XAML
            // DataContext = new MainViewModel(); // Uncomment if not set in XAML <Window.DataContext>
        }
    }
}