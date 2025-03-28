// QueryInfo.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SqlPerformanceAiAdvisor
{
    public class QueryInfo : INotifyPropertyChanged
    {
        private string _queryText = "";
        private long _totalCpuTime;
        private long _totalLogicalReads;
        private long _totalDuration;
        private long _executionCount;
        private string? _executionPlanXml; // Nullable
        private string _recommendation = "Pending...";

        public string QueryText
        {
            get => _queryText;
            set => SetProperty(ref _queryText, value);
        }

        public long TotalCpuTime // In microseconds
        {
            get => _totalCpuTime;
            set => SetProperty(ref _totalCpuTime, value);
        }

        public long TotalLogicalReads
        {
            get => _totalLogicalReads;
            set => SetProperty(ref _totalLogicalReads, value);
        }

        public long TotalDuration // In microseconds
        {
            get => _totalDuration;
            set => SetProperty(ref _totalDuration, value);
        }

        public long ExecutionCount
        {
            get => _executionCount;
            set => SetProperty(ref _executionCount, value);
        }

        public string? ExecutionPlanXml // Can be null if plan is not available
        {
            get => _executionPlanXml;
            set => SetProperty(ref _executionPlanXml, value);
        }


        public string Recommendation
        {
            get => _recommendation;
            set => SetProperty(ref _recommendation, value);
        }

        // --- INotifyPropertyChanged Implementation ---
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}