// AnalysisViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinUIAppSqlRecommendation.Services; // Your services namespace

namespace WinUIAppSqlRecommendation
{
    public partial class AnalysisViewModel : ObservableObject
    {
        private readonly IAiService _aiService;
        private readonly QueryInfo _queryInfo;
        private readonly DispatcherQueue _dispatcherQueue;
        private CancellationTokenSource? _analysisCts;

        [ObservableProperty]
        private string _analysisResult = "";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
        private bool _isAnalyzing = false;

        [ObservableProperty]
        private string _statusText = "Ready to analyze.";

        public string QueryTextPreview => _queryInfo.QueryText.Length > 200
                                            ? _queryInfo.QueryText.Substring(0, 200) + "..."
                                            : _queryInfo.QueryText;

        public AnalysisViewModel(IAiService aiService, QueryInfo queryInfo, DispatcherQueue dispatcher)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
            _queryInfo = queryInfo ?? throw new ArgumentNullException(nameof(queryInfo));
            _dispatcherQueue = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public async Task StartAnalysisStreaming()
        {
            if (IsAnalyzing) return;

            _analysisCts?.Cancel();
            _analysisCts = new CancellationTokenSource();
            var cancellationToken = _analysisCts.Token;

            IsAnalyzing = true;
            StatusText = "Requesting AI analysis...";
            AnalysisResult = "";

            var recommendationBuilder = new StringBuilder();

            try
            {
                if (_aiService == null)
                {
                    throw new InvalidOperationException("AI Service is not available.");
                }

                await foreach (var chunk in _aiService.GetQueryOptimizationRecommendationStreamAsync(
                                    _queryInfo.QueryText,
                                    _queryInfo.ExecutionPlanXml,
                                    cancellationToken)
                                .WithCancellation(cancellationToken)
                                .ConfigureAwait(false))
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    recommendationBuilder.Append(chunk);
                    UpdateOnUiThread(() =>
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            AnalysisResult = recommendationBuilder.ToString();
                            StatusText = "Streaming results...";
                        }
                    });
                }

                UpdateOnUiThread(() => {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        StatusText = "Analysis canceled.";
                        if (!AnalysisResult.EndsWith("[Canceled]")) AnalysisResult += "\n[Canceled]";
                    }
                    else
                    {
                        StatusText = "Analysis complete.";
                    }
                });

            }
            catch (OperationCanceledException)
            {
                UpdateOnUiThread(() => {
                    StatusText = "Analysis canceled.";
                    if (!AnalysisResult.EndsWith("[Canceled]")) AnalysisResult += "\n[Canceled]";
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AI Streaming Error: {ex}");
                UpdateOnUiThread(() => {
                    StatusText = $"Error during analysis: {ex.Message}";
                    AnalysisResult += $"\n[Error: {ex.Message}]";
                });
            }
            finally
            {
                UpdateOnUiThread(() => IsAnalyzing = false);
            }
        }

        [RelayCommand(CanExecute = nameof(CanCancelAnalysis))]
        private void Cancel()
        {
            UpdateOnUiThread(() => StatusText = "Canceling analysis...");
            _analysisCts?.Cancel();
        }

        private bool CanCancelAnalysis() => IsAnalyzing;

        // Helper to run code on the UI thread - CORRECTED
        private void UpdateOnUiThread(Action action)
        {
            if (_dispatcherQueue.HasThreadAccess)
            {
                action();
            }
            else
            {
                // Explicitly create the DispatcherQueueHandler delegate
                _dispatcherQueue.TryEnqueue(new DispatcherQueueHandler(action));
            }
        }

        public void Cleanup()
        {
            _analysisCts?.Cancel();
            _analysisCts?.Dispose();
            _analysisCts = null;
            Debug.WriteLine("AnalysisViewModel cleaned up.");
        }
    }
}