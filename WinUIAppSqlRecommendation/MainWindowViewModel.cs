// MainWindowViewModel.cs (Complete Refactored Version)

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.SqlClient;
using Microsoft.UI.Dispatching; // WinUI 3 Dispatcher
using Microsoft.UI.Xaml.Controls; // Needed for PasswordBox
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinUIAppSqlRecommendation.Services; // Your services namespace
using WinUIAppSqlRecommendation; // Your project namespace

namespace WinUIAppSqlRecommendation
{
    public partial class MainViewModel : ObservableObject
    {
        // --- Observable Properties ---

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSqlAuthEnabled))]
        [NotifyCanExecuteChangedFor(nameof(AnalyzeSqlCommand))]
        private string _serverName = "(local)";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AnalyzeSqlCommand))]
        private string _databaseName = ""; // User needs to provide this

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSqlAuthEnabled))]
        [NotifyCanExecuteChangedFor(nameof(AnalyzeSqlCommand))]
        private bool _useWindowsAuth = true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AnalyzeSqlCommand))]
        private string _sqlUserName = "";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AnalyzeSqlCommand))]
        [NotifyCanExecuteChangedFor(nameof(RequestAnalysisCommand))]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AnalyzeSqlCommand))]
        [NotifyCanExecuteChangedFor(nameof(RequestAnalysisCommand))]
        private bool _isBusy = false; // Represents busy state primarily for SQL fetching

        // --- Private Fields ---

        private readonly DispatcherQueue _dispatcherQueue;
        // Make AiService publicly accessible if AnalysisWindow needs it directly
        // Or keep internal/private if AnalysisWindow gets it via constructor parameter
        internal IAiService? _aiService; // Changed to internal for potential access
        private CancellationTokenSource? _currentSqlFetchCts;

        // --- Collections ---
        public ObservableCollection<QueryInfo> TopQueries { get; } = new ObservableCollection<QueryInfo>();

        // --- Read-only Properties ---
        public bool IsSqlAuthEnabled => !UseWindowsAuth;

        // --- Constructor ---
        public MainViewModel(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
            InitializeAiServiceAsync(); // Start AI service initialization
        }

        // Default constructor if DispatcherQueue can be obtained from current thread
        public MainViewModel() : this(DispatcherQueue.GetForCurrentThread()) { }

        // --- AI Service Initialization ---
        private async Task InitializeAiServiceAsync()
        {
            UpdateStatus("Initializing AI Service...");
            try
            {
                // Prioritize Phi Silica if available
                var silicaService = new PhiSilicaAiService();
                await silicaService.InitializeAsync();

                // TODO: Need a reliable check on PhiSilicaAiService after InitializeAsync
                // bool silicaAvailable = silicaService.IsAvailable; // Fictional property check
                bool silicaAvailable = false; // Placeholder - Set to false to force Ollama default

                if (silicaAvailable)
                {
                    _aiService = silicaService;
                    UpdateStatus("Using local Phi Silica AI Service.");
                }
                else
                {
                    _aiService = new OllamaAiService(); // Fallback
                    UpdateStatus("Using Ollama AI Service (Phi Silica unavailable/placeholder).");
                }
            }
            catch (Exception ex)
            {
                _aiService = new OllamaAiService(); // Fallback on error
                UpdateStatus($"Error initializing AI: {ex.Message}. Using Ollama.");
                Debug.WriteLine($"AI Init Error: {ex}");
            }
            // Ensure commands re-evaluate their CanExecute state after AI service is set
            AnalyzeSqlCommand.NotifyCanExecuteChanged();
            RequestAnalysisCommand.NotifyCanExecuteChanged();
        }

        // --- Commands ---

        // Command to fetch SQL data
        [RelayCommand(CanExecute = nameof(CanAnalyzeSql))]
        private async Task AnalyzeSqlAsync(object? parameter) // Parameter is the PasswordBox
        {
            _currentSqlFetchCts?.Cancel();
            _currentSqlFetchCts = new CancellationTokenSource();
            var cancellationToken = _currentSqlFetchCts.Token;

            IsBusy = true; // Busy during SQL fetch
            UpdateStatus("Starting SQL data analysis...");
            UpdateTopQueries(null); // Clear queries on UI thread

            string? sqlPassword = null;
            // Extract password inside the command execution
            if (!UseWindowsAuth)
            {
                if (parameter is PasswordBox pwBox)
                {
                    sqlPassword = pwBox.Password;
                    if (string.IsNullOrEmpty(sqlPassword))
                    {
                        UpdateStatus("Error: SQL Password is required.");
                        IsBusy = false;
                        return;
                    }
                }
                else
                {
                    UpdateStatus("Error: PasswordBox parameter not correctly passed to command.");
                    IsBusy = false;
                    return;
                }
            }

            string connectionString = BuildConnectionString(sqlPassword);
            if (string.IsNullOrEmpty(connectionString)) { IsBusy = false; return; }

            List<QueryInfo>? queries = null;
            try
            {
                UpdateStatus("Fetching top queries from SQL Server...");
                queries = await FetchTopQueriesAsync(connectionString, cancellationToken);

                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException();

                if (queries == null || queries.Count == 0)
                {
                    UpdateStatus("No significant query stats found or error fetching data.");
                }
                else
                {
                    UpdateTopQueries(queries);
                    UpdateStatus($"SQL performance data loaded ({queries.Count} queries). Ready for AI analysis.");
                }
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("SQL data fetch canceled.");
                UpdateTopQueries(null);
            }
            catch (SqlException sqlEx)
            {
                UpdateStatus($"SQL Error: {sqlEx.Message}");
                UpdateTopQueries(null);
                Debug.WriteLine($"SQL Error: {sqlEx}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error fetching SQL data: {ex.Message}");
                UpdateTopQueries(null);
                Debug.WriteLine($"Fetch Error: {ex}");
            }
            finally
            {
                IsBusy = false; // No longer busy after SQL fetch attempt
                _currentSqlFetchCts?.Dispose();
                _currentSqlFetchCts = null;
            }
        }

        private bool CanAnalyzeSql(object? parameter) // Parameter not used in check itself
        {
            bool credentialsValid = UseWindowsAuth || !string.IsNullOrWhiteSpace(SqlUserName);
            return !IsBusy && // Only check SQL fetch busy state
                   !string.IsNullOrWhiteSpace(ServerName) &&
                   !string.IsNullOrWhiteSpace(DatabaseName) &&
                   credentialsValid;
        }

        // Command to open the analysis window for a specific query
        [RelayCommand(CanExecute = nameof(CanRequestAnalysis))]
        private void RequestAnalysis(QueryInfo? queryInfo)
        {
            if (queryInfo == null || _aiService == null)
            {
                UpdateStatus("Cannot request analysis: Query or AI service is unavailable.");
                return;
            }

            UpdateStatus($"Opening analysis window for query hash: {queryInfo.QueryText.GetHashCode()}");

            try
            {
                // Create and show the Analysis Window, passing dependencies
                var analysisWindow = new AnalysisWindow(_aiService, queryInfo, _dispatcherQueue);
                analysisWindow.Activate(); // Show the window

                // Optional: Set owner if needed (requires reference to MainWindow or interop helpers)
                // WinRT.Interop.WindowNative.SetWindowHandle(analysisWindow, mainWindowHandle);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error opening analysis window: {ex.Message}");
                Debug.WriteLine($"Analysis Window Error: {ex}");
            }
        }

        private bool CanRequestAnalysis(QueryInfo? queryInfo)
        {
            // Can request analysis if not busy fetching SQL data, AI service is ready,
            // and a query is provided.
            return !IsBusy && _aiService != null && queryInfo != null;
        }


        // --- Helper Methods ---

        // Helper to update status message on UI thread
        private void UpdateStatus(string message)
        {
            if (_dispatcherQueue.HasThreadAccess)
            {
                StatusMessage = message;
            }
            else
            {
                _dispatcherQueue.TryEnqueue(new DispatcherQueueHandler(() => { StatusMessage = message; }));
            }
        }

        // Helper to update TopQueries collection on UI thread
        private void UpdateTopQueries(List<QueryInfo>? queries)
        {
            Action updateAction = () => {
                TopQueries.Clear();
                if (queries != null)
                {
                    foreach (var q in queries) { TopQueries.Add(q); }
                }
            };

            if (_dispatcherQueue.HasThreadAccess)
            {
                updateAction();
            }
            else
            {
                _dispatcherQueue.TryEnqueue(new DispatcherQueueHandler(updateAction));
            }
        }

        private string BuildConnectionString(string? password)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = ServerName?.Trim(),
                    InitialCatalog = DatabaseName?.Trim(),
                    IntegratedSecurity = UseWindowsAuth,
                    TrustServerCertificate = true, // CAUTION: Review for production security
                    ConnectTimeout = 15
                };

                if (!UseWindowsAuth)
                {
                    // Credentials checked before calling this method now
                    builder.UserID = SqlUserName?.Trim();
                    builder.Password = password;
                    builder.IntegratedSecurity = false;
                }
                return builder.ConnectionString;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error building connection string: {ex.Message}");
                Debug.WriteLine($"Conn String Error: {ex}");
                return string.Empty;
            }
        }

        private async Task<List<QueryInfo>> FetchTopQueriesAsync(string connectionString, CancellationToken cancellationToken)
        {
            var results = new List<QueryInfo>();
            // Query remains the same
            const string query = @"
                SELECT TOP 10
                    qs.total_worker_time, qs.total_elapsed_time, qs.total_logical_reads,
                    qs.execution_count,
                    SUBSTRING(st.text, (qs.statement_start_offset/2)+1,
                        ((CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(st.text) ELSE qs.statement_end_offset END - qs.statement_start_offset)/2) + 1) AS statement_text,
                    qp.query_plan AS execution_plan_xml
                FROM sys.dm_exec_query_stats AS qs
                CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS st
                CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) AS qp
                WHERE qp.query_plan IS NOT NULL
                ORDER BY qs.total_worker_time DESC;";

            using var connection = new SqlConnection(connectionString);
            using var command = new SqlCommand(query, connection);

            await connection.OpenAsync(cancellationToken);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            int count = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                results.Add(new QueryInfo
                {
                    TotalCpuTime = reader.GetInt64(reader.GetOrdinal("total_worker_time")),
                    TotalDuration = reader.GetInt64(reader.GetOrdinal("total_elapsed_time")),
                    TotalLogicalReads = reader.GetInt64(reader.GetOrdinal("total_logical_reads")),
                    ExecutionCount = reader.GetInt64(reader.GetOrdinal("execution_count")),
                    QueryText = reader.GetString(reader.GetOrdinal("statement_text")),
                    ExecutionPlanXml = reader.IsDBNull(reader.GetOrdinal("execution_plan_xml")) ? null : reader.GetString(reader.GetOrdinal("execution_plan_xml")),
                    Recommendation = "Analysis not requested" // Initial state
                });
                count++;
                if (count >= 10) break;
            }
            return results;
        }
    }
}