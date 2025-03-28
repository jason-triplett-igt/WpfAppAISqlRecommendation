// MainViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using Microsoft.Data.SqlClient; // Use Microsoft.Data.SqlClient
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SqlPerformanceAiAdvisor.Services;
using SqlPerformanceAiAdvisor.Service; // Use the Services namespace

namespace SqlPerformanceAiAdvisor
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _serverName = "(local)"; // Default to local SQL Server
        private string _databaseName = "";
        private bool _useWindowsAuth = true;
        private string _sqlUserName = "";
        private string _statusMessage = "Ready";
        private bool _isBusy = false;
        private readonly IAiService _aiService; // Use the interface

        public ObservableCollection<QueryInfo> TopQueries { get; } = new ObservableCollection<QueryInfo>();

        public string ServerName
        {
            get => _serverName;
            set => SetProperty(ref _serverName, value);
        }

        public string DatabaseName
        {
            get => _databaseName;
            set => SetProperty(ref _databaseName, value);
        }

        public bool UseWindowsAuth
        {
            get => _useWindowsAuth;
            set
            {
                if (SetProperty(ref _useWindowsAuth, value))
                {
                    // Optionally clear SQL credentials if switching to Windows Auth
                    if (_useWindowsAuth)
                    {
                        SqlUserName = "";
                        // PasswordBox needs special handling in the View's code-behind
                    }
                    OnPropertyChanged(nameof(IsSqlAuthEnabled)); // Notify dependent property
                }
            }
        }

        public string SqlUserName
        {
            get => _sqlUserName;
            set => SetProperty(ref _sqlUserName, value);
        }

        // Note: Password should be handled securely using PasswordBox in the View
        // We'll get it from the CommandParameter

        public bool IsSqlAuthEnabled => !UseWindowsAuth;

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public ICommand AnalyzeCommand { get; }

        public MainViewModel()
        {
            _aiService = new OllamaAiService(); // Use the Ollama service
            AnalyzeCommand = new AsyncRelayCommand<object>(AnalyzeAsync, CanAnalyze);
        }

        private bool CanAnalyze(object? parameter)
        {
            // Basic validation
            return !IsBusy &&
                   !string.IsNullOrWhiteSpace(ServerName) &&
                   !string.IsNullOrWhiteSpace(DatabaseName) &&
                   (UseWindowsAuth || !string.IsNullOrWhiteSpace(SqlUserName));
            // Password validation would happen inside AnalyzeAsync
        }

        private async Task AnalyzeAsync(object? parameter)
        {
            IsBusy = true;
            StatusMessage = "Starting analysis...";
            TopQueries.Clear();

            // Securely get password if using SQL Auth
            string? sqlPassword = null;
            if (!UseWindowsAuth && parameter is System.Windows.Controls.PasswordBox pwBox)
            {
                sqlPassword = pwBox.Password;
                if (string.IsNullOrEmpty(sqlPassword))
                {
                    StatusMessage = "Error: SQL Password is required for SQL Authentication.";
                    IsBusy = false;
                    return;
                }
            }
            else if (!UseWindowsAuth)
            {
                StatusMessage = "Error: PasswordBox parameter not correctly passed.";
                IsBusy = false;
                return;
            }

            string connectionString = BuildConnectionString(sqlPassword);
            if (string.IsNullOrEmpty(connectionString))
            {
                IsBusy = false;
                return; // Error already set in BuildConnectionString
            }


            try
            {
                StatusMessage = "Fetching top queries from SQL Server...";
                var queries = await FetchTopQueriesAsync(connectionString);

                if (queries.Count == 0)
                {
                    StatusMessage = "No significant query stats found or error fetching data.";
                    IsBusy = false;
                    return;
                }

                StatusMessage = $"Fetched {queries.Count} queries. Getting AI recommendations...";

                // Add queries to UI first
                foreach (var q in queries)
                {
                    TopQueries.Add(q);
                }

                // Process AI recommendations concurrently (optional, but faster)
                List<Task> recommendationTasks = new List<Task>();
                foreach (var queryInfo in queries) // Use the already fetched list
                {
                    recommendationTasks.Add(Task.Run(async () => // Use Task.Run for CPU-bound simulation or actual network call
                    {
                        try
                        {
                            string recommendation = await _aiService.GetQueryOptimizationRecommendationAsync(queryInfo.QueryText, queryInfo.ExecutionPlanXml);
                            // Update the UI thread safely
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                queryInfo.Recommendation = recommendation;
                            });
                        }
                        catch (Exception aiEx)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                queryInfo.Recommendation = $"Error getting AI recommendation: {aiEx.Message}";
                            });
                        }
                    }));
                }

                await Task.WhenAll(recommendationTasks); // Wait for all AI calls to complete

                StatusMessage = "Analysis complete.";
            }
            catch (SqlException sqlEx)
            {
                StatusMessage = $"SQL Error: {sqlEx.Message}";
                // Log detailed error sqlEx.ToString()
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                // Log detailed error ex.ToString()
            }
            finally
            {
                IsBusy = false;
            }
        }

        private string BuildConnectionString(string? password)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = ServerName,
                InitialCatalog = DatabaseName,
                IntegratedSecurity = UseWindowsAuth,
                TrustServerCertificate = true, // Important for local/dev; review for production
                ConnectTimeout = 15 // Set a reasonable timeout
            };

            if (!UseWindowsAuth)
            {
                if (string.IsNullOrWhiteSpace(SqlUserName))
                {
                    StatusMessage = "SQL User Name cannot be empty for SQL Authentication.";
                    return string.Empty;
                }
                if (string.IsNullOrEmpty(password)) // Check password again just in case
                {
                    StatusMessage = "SQL Password cannot be empty for SQL Authentication.";
                    return string.Empty; // Already checked in AnalyzeAsync but good practice
                }
                builder.UserID = SqlUserName;
                builder.Password = password; // Add password if using SQL Auth
                builder.IntegratedSecurity = false; // Explicitly set false
            }

            return builder.ConnectionString;
        }

        private async Task<List<QueryInfo>> FetchTopQueriesAsync(string connectionString)
        {
            var results = new List<QueryInfo>();
            // Query to get top 10 queries by total CPU time
            // Uses CROSS APPLY to get text and plan
            const string query = @"
                SELECT TOP 10
                    qs.total_worker_time, -- CPU time in microseconds
                    qs.total_elapsed_time, -- Duration in microseconds
                    qs.total_logical_reads,
                    qs.execution_count,
                    SUBSTRING(st.text, (qs.statement_start_offset/2)+1,
                        ((CASE qs.statement_end_offset
                          WHEN -1 THEN DATALENGTH(st.text)
                         ELSE qs.statement_end_offset
                         END - qs.statement_start_offset)/2) + 1) AS statement_text,
                    qp.query_plan AS execution_plan_xml -- Get the XML plan
                FROM
                    sys.dm_exec_query_stats AS qs
                CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS st
                CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) AS qp
                ORDER BY
                    qs.total_worker_time DESC; -- Order by CPU time
            ";
            // Note: This requires VIEW SERVER STATE permission.
            // Note: query_plan might be NULL if the plan is not cached.

            using (var connection = new SqlConnection(connectionString))
            {
                using (var command = new SqlCommand(query, connection))
                {
                    await connection.OpenAsync(); // Open connection asynchronously
                    using (var reader = await command.ExecuteReaderAsync()) // Execute asynchronously
                    {
                        while (reader.Read()) // Read asynchronously
                        {
                            results.Add(new QueryInfo
                            {
                                TotalCpuTime = reader.GetInt64(reader.GetOrdinal("total_worker_time")),
                                TotalDuration = reader.GetInt64(reader.GetOrdinal("total_elapsed_time")),
                                TotalLogicalReads = reader.GetInt64(reader.GetOrdinal("total_logical_reads")),
                                ExecutionCount = reader.GetInt64(reader.GetOrdinal("execution_count")),
                                QueryText = reader.GetString(reader.GetOrdinal("statement_text")),
                                // Handle potential DBNull for execution plan
                                ExecutionPlanXml = reader.IsDBNull(reader.GetOrdinal("execution_plan_xml"))
                                                    ? null
                                                    : reader.GetString(reader.GetOrdinal("execution_plan_xml")),
                                Recommendation = "Fetching..." // Initial state before AI call
                            });
                        }
                    }
                }
            }
            return results;
        }


        // --- Basic INotifyPropertyChanged Implementation ---
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            // Trigger re-evaluation of command CanExecute
            if (AnalyzeCommand is RelayCommand<object> rc) rc.RaiseCanExecuteChanged();
            if (AnalyzeCommand is AsyncRelayCommand<object> arc) arc.RaiseCanExecuteChanged(); // If using AsyncRelayCommand
        }
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    // --- Basic ICommand Implementations (Or use a library like CommunityToolkit.Mvvm) ---

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Predicate<T?>? _canExecute;

        public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute((T?)parameter);
        public void Execute(object? parameter) => _execute((T?)parameter);
        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested(); // Helper
    }

    public class AsyncRelayCommand<T> : ICommand
    {
        private readonly Func<T?, Task> _execute;
        private readonly Predicate<T?>? _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<T?, Task> execute, Predicate<T?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter)
        {
            return !_isExecuting && (_canExecute == null || _canExecute((T?)parameter));
        }

        public async void Execute(object? parameter) // Note: async void is generally discouraged except for event handlers/ICommand
        {
            _isExecuting = true;
            RaiseCanExecuteChanged(); // Disable button while executing
            try
            {
                await _execute((T?)parameter);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged(); // Re-enable button
            }
        }
        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested(); // Helper
    }

}