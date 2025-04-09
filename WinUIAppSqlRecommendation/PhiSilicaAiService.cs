// PhiSilicaAiService.cs (Refactored for CS1626)
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Windows.AI.Generative; // Assuming this namespace is correct
using WinUIAppSqlRecommendation.Services; // Assuming this namespace is correct
using System.Diagnostics; // Added for Debug.WriteLine

namespace WinUIAppSqlRecommendation.Services
{
    public class PhiSilicaAiService : IAiService
    {
        private LanguageModel? _languageModel = null;
        private bool _isModelAvailableChecked = false;
        private bool _isModelActuallyAvailable = false;
        private static readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);


        public async Task InitializeAsync()
        {
            // Use semaphore to prevent concurrent initialization attempts
            await _initSemaphore.WaitAsync();
            try
            {
                if (_isModelAvailableChecked) return; // Already checked

                try
                {
                    if (LanguageModel.IsAvailable())
                    {
                        // Optional: Check if MakeAvailableAsync is needed for consent/setup
                        // var op = await LanguageModel.MakeAvailableAsync();
                        // if (op.Status != Windows.Foundation.AsyncStatus.Completed) { /* handle error */ }

                        _languageModel = await LanguageModel.CreateAsync();
                        _isModelActuallyAvailable = (_languageModel != null);
                        if (!_isModelActuallyAvailable) Debug.WriteLine("Phi Silica Service: LanguageModel.IsAvailable() was true, but instance creation failed.");
                    }
                    else
                    {
                        Debug.WriteLine("Phi Silica Service: LanguageModel.IsAvailable() returned false.");
                        _isModelActuallyAvailable = false;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Phi Silica Service: Error during initialization: {ex.Message}");
                    _isModelActuallyAvailable = false;
                    _languageModel = null;
                }
                finally
                {
                    _isModelAvailableChecked = true;
                }

                Debug.WriteLine($"Phi Silica Service: Initialization complete. Model available: {_isModelActuallyAvailable}");
            }
            finally
            {
                _initSemaphore.Release();
            }
        }

        public async IAsyncEnumerable<string> GetQueryOptimizationRecommendationStreamAsync(
            string sqlQuery,
            string? executionPlanXml,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!_isModelAvailableChecked)
            {
                await InitializeAsync();
            }

            if (!_isModelActuallyAvailable || _languageModel == null)
            {
                yield return "Phi Silica AI Service Error: Model is not available on this device.";
                yield break;
            }

            Debug.WriteLine($"Phi Silica Service: Requesting recommendation (will accumulate result)...");
            string prompt = BuildPrompt(sqlQuery, executionPlanXml);
            string? errorMessage = null;
            var responseBuilder = new StringBuilder(); // Accumulate successful response here

            try
            {
                var responseStreamOperation = _languageModel.GenerateResponseWithProgressAsync(prompt);

                await foreach (var partialResponse in responseStreamOperation.AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    // *** DO NOT yield here ***
                    if (!string.IsNullOrEmpty(partialResponse))
                    {
                        responseBuilder.Append(partialResponse); // Append chunk to builder
                    }
                }
                Debug.WriteLine("Phi Silica Service: Generation complete.");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Phi Silica Service: Operation canceled.");
                // Set error message or simply allow method to end via finally/yield break if preferred
                errorMessage = "[Operation Canceled]";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Phi Silica Service: Error during generation: {ex.Message}");
                errorMessage = $"Phi Silica AI Service Error: {ex.Message}";
            }

            // --- > YIELD ACCUMULATED RESULT OR ERROR HERE < ---
            if (errorMessage != null)
            {
                yield return errorMessage; // Yield the error message
            }
            else if (responseBuilder.Length > 0)
            {
                // Yield the single, complete response string if no error occurred
                yield return responseBuilder.ToString();
            }
            // Otherwise, yield nothing if cancelled before any error or result.
        }


        private string BuildPrompt(string sqlQuery, string? executionPlanXml)
        {
            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine("You are an expert SQL Server performance tuning assistant.");
            promptBuilder.AppendLine("Analyze the following SQL query and its associated execution plan (if provided).");
            promptBuilder.AppendLine("Provide specific, actionable recommendations to improve its performance, focusing on indexing, query structure, and potential bottlenecks.");
            promptBuilder.AppendLine("Format your recommendations clearly using bullet points.");
            promptBuilder.AppendLine("\n--- SQL Query ---");
            promptBuilder.AppendLine("```sql");
            promptBuilder.AppendLine(sqlQuery);
            promptBuilder.AppendLine("```");

            if (!string.IsNullOrWhiteSpace(executionPlanXml))
            {
                promptBuilder.AppendLine("\n--- Execution Plan (XML) ---");
                promptBuilder.AppendLine("```xml");
                promptBuilder.AppendLine(executionPlanXml.Length > 10000 ? executionPlanXml.Substring(0, 10000) + "\n... (plan truncated)" : executionPlanXml);
                promptBuilder.AppendLine("```");
            }
            else
            {
                promptBuilder.AppendLine("\n--- Execution Plan (XML) ---");
                promptBuilder.AppendLine("Not Provided");
            }

            promptBuilder.AppendLine("\n--- Recommendations ---");
            return promptBuilder.ToString();
        }
    }
}

// Keep the AsyncOperationWithProgressExtensions class
public static class AsyncOperationWithProgressExtensions
{
    // (Extension method implementation remains the same as previous step)
    public static async IAsyncEnumerable<TProgress> AsAsyncEnumerable<TResult, TProgress>(
            this Windows.Foundation.IAsyncOperationWithProgress<TResult, TProgress> operation,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Placeholder implementation - relies on underlying WinRT interop behavior
        TaskCompletionSource<TResult> tcs = new TaskCompletionSource<TResult>();
        var progressHandler = new Action<Windows.Foundation.IAsyncOperationWithProgress<TResult, TProgress>, TProgress>((op, p) => { /* Needs channel logic for true progress yield */ });
        var completionHandler = new Windows.Foundation.AsyncOperationWithProgressCompletedHandler<TResult, TProgress>((op, status) =>
        {
            switch (status)
            {
                case Windows.Foundation.AsyncStatus.Completed:
                    tcs.TrySetResult(op.GetResults());
                    break;
                case Windows.Foundation.AsyncStatus.Error:
                    tcs.TrySetException(op.ErrorCode);
                    break;
                case Windows.Foundation.AsyncStatus.Canceled:
                    tcs.TrySetCanceled();
                    break;
            }
        });

        operation.Progress = new Windows.Foundation.AsyncOperationProgressHandler<TResult, TProgress>(progressHandler);
        operation.Completed = completionHandler;

        using (cancellationToken.Register(() => operation.Cancel()))
        {
            await tcs.Task.ConfigureAwait(false);
        }
        yield break; // Simplified
    }
}