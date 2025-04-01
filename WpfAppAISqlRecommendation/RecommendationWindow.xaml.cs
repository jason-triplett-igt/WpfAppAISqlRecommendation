using System.Windows;

namespace SqlPerformanceAiAdvisor
{
    public partial class RecommendationWindow : Window
    {
        public string RecommendationText { get; set; }
        public RecommendationWindow(string recommendation)
        {
            InitializeComponent();
            RecommendationText = recommendation;
            DataContext = this;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
