// QueryInfo.cs (Rewritten for WinUI 3 with CommunityToolkit.Mvvm)
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input; // Keep if assigning commands directly here, otherwise optional
using System.Windows.Input; // Keep if ICommand property needed, otherwise remove

// Ensure this namespace matches your WinUI 3 project structure
namespace WinUIAppSqlRecommendation
{
    // Inherit from ObservableObject
    public partial class QueryInfo : ObservableObject
    {
        // Use [ObservableProperty] for automatic property generation
        [ObservableProperty]
        private string _queryText = "";

        [ObservableProperty]
        private long _totalCpuTime; // In microseconds

        [ObservableProperty]
        private long _totalLogicalReads;

        [ObservableProperty]
        private long _totalDuration; // In microseconds

        [ObservableProperty]
        private long _executionCount;

        [ObservableProperty]
        private string? _executionPlanXml; // Nullable

        // Notifies IsRecommendationReady when Recommendation changes
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsRecommendationReady))]
        private string _recommendation = "Pending...";

        // Notifies the externally assigned RetryCommand when IsRetrying changes
        // Ensure the Command implementation checks this property in its CanExecute logic
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
        private bool _isRetrying = false;

        // This command is now assigned externally in MainViewModel's CollectionChanged handler
        // If you keep assigning it here, ensure the command implementation can be updated externally
        // or passed in via constructor/method if needed.
        public IRelayCommand? RetryCommand { get; set; }
        // Consider making the setter private if only set externally:
        // public IRelayCommand? RetryCommand { get; internal set; }


        // Read-only property derived from Recommendation
        public bool IsRecommendationReady =>
            !string.IsNullOrEmpty(Recommendation) &&
            Recommendation != "Pending..." &&
            Recommendation != "Fetching...";

        // --- Manual INotifyPropertyChanged implementation is no longer needed ---
        // --- Command properties (like ViewRecommendationCommand) moved or handled externally ---

        // Constructor could be added if needed for initialization logic
        // public QueryInfo() { }
    }
}